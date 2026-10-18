using Marquee.Core.Sensors;
using Marquee.Providers.Agents.Claude;
using Marquee.Providers.Agents.Tests.TestSupport;

namespace Marquee.Providers.Agents.Tests;

public class ClaudeSessionsProviderTests : IDisposable
{
    const long BusyStart = 134367036000000000;
    const long IdleStart = 134367046000000000;

    readonly TempDir _dir = new();

    // The fixture processes, as Windows would report them. Tests remove or change entries to simulate exits.
    readonly Dictionary<int, RunningProcess> _processes = new()
    {
        [41000] = new(DateTime.FromFileTimeUtc(BusyStart)),
        [42000] = new(DateTime.FromFileTimeUtc(IdleStart)),
        [43000] = new(DateTime.FromFileTimeUtc(IdleStart)),
    };

    public void Dispose() => _dir.Dispose();

    void Copy(params string[] fixtures)
    {
        foreach (var name in fixtures)
            File.Copy(Fixture.Path($"claude-sessions/{name}"), _dir.File(name));
    }

    (object? Total, object? Busy, object? Idle) Poll(string? folder = null, params string[] hostNames)
    {
        var store = new SensorStore(() => Clock.T0);
        using var provider = new ClaudeSessionsProvider(folder ?? _dir.Path,
            pid => _processes.TryGetValue(pid, out var p) ? p : null, hostNames.Length > 0 ? hostNames : ["DEVBOX"]);
        provider.Poll(store);
        return (Clock.Read(store, "claude.sessions.total"), Clock.Read(store, "claude.sessions.busy"), Clock.Read(store, "claude.sessions.idle"));
    }

    [Fact]
    public void Parses_the_observed_format()
    {
        var session = ClaudeSessionFile.Parse(Fixture.Text("claude-sessions/busy.json"));

        Assert.Equal(new ClaudeSessionFile(41000, "busy", BusyStart, "win32:devbox"), session);
    }

    [Fact]
    public void Only_pid_is_required() =>
        Assert.Equal(new ClaudeSessionFile(43000, null, null, null), ClaudeSessionFile.Parse(Fixture.Text("claude-sessions/minimal.json")));

    [Theory]
    [InlineData("")]
    [InlineData("{\"pid\":")]
    [InlineData("[41000]")]
    [InlineData("{\"status\":\"busy\"}")]
    [InlineData("{\"pid\":\"41000\"}")]
    [InlineData("{\"pid\":-3}")]
    [InlineData("{\"pid\":4.5}")]
    public void Anything_without_a_pid_is_not_a_session(string json) => Assert.Null(ClaudeSessionFile.Parse(json));

    [Fact]
    public void Counts_busy_idle_and_total()
    {
        Copy("busy.json", "idle.json", "minimal.json");

        Assert.Equal((3.0, 1.0, 1.0), Poll());
    }

    [Fact]
    public void Sessions_whose_process_exited_are_not_counted()
    {
        Copy("busy.json", "idle.json");
        _processes.Remove(41000);

        Assert.Equal((1.0, 0.0, 1.0), Poll());
    }

    [Fact]
    public void A_reused_pid_is_not_the_session()
    {
        Copy("busy.json");
        _processes[41000] = new(DateTime.FromFileTimeUtc(BusyStart).AddMinutes(5));

        Assert.Equal((0.0, 0.0, 0.0), Poll());
    }

    [Fact]
    public void A_process_whose_start_time_is_hidden_still_counts()
    {
        Copy("busy.json");
        _processes[41000] = new(null);

        Assert.Equal((1.0, 1.0, 0.0), Poll());
    }

    [Fact]
    public void Sessions_from_another_pc_are_not_counted()
    {
        File.WriteAllText(_dir.File("1.json"), Fixture.Text("claude-sessions/busy.json").Replace("win32:devbox", "win32:laptop"));

        Assert.Equal((0.0, 0.0, 0.0), Poll());
    }

    [Theory]
    [InlineData("MY-VERY-LONG-WO")] // the 15-character NetBIOS form of the name
    [InlineData("my-very-long-workstation-name")] // the full host name
    [InlineData("OTHER", "MY-VERY-LONG-WORKSTATION-NAME")] // any of this PC's names will do, in any case
    public void A_long_host_name_is_this_pc_by_either_of_its_names(params string[] hostNames)
    {
        File.WriteAllText(_dir.File("1.json"), Fixture.Text("claude-sessions/busy.json").Replace("win32:devbox", "win32:my-very-long-workstation-name"));

        Assert.Equal((1.0, 1.0, 0.0), Poll(null, hostNames));
    }

    [Fact]
    public void A_different_long_host_name_is_not_this_pc()
    {
        File.WriteAllText(_dir.File("1.json"), Fixture.Text("claude-sessions/busy.json").Replace("win32:devbox", "win32:other-long-workstation"));

        Assert.Equal((0.0, 0.0, 0.0), Poll(null, "MY-VERY-LONG-WO"));
    }

    [Fact]
    public void An_empty_file_is_skipped_like_a_vanished_one_not_taken_for_a_format_change()
    {
        File.WriteAllText(_dir.File("rewriting.json"), "");

        Assert.Equal((0.0, 0.0, 0.0), Poll());
    }

    [Fact]
    public void A_blank_file_is_skipped_too()
    {
        File.WriteAllText(_dir.File("blank.json"), "  \r\n ");

        Assert.Equal((0.0, 0.0, 0.0), Poll());
    }

    [Fact]
    public void Other_files_are_ignored_and_a_bad_file_is_skipped()
    {
        Copy("busy.json");
        File.WriteAllText(_dir.File("41000.0123abcd.key"), "not a session");
        File.WriteAllText(_dir.File("half-written.json"), "{\"pid\":420");

        Assert.Equal((1.0, 1.0, 0.0), Poll());
    }

    [Fact]
    public void No_folder_means_no_sessions() => Assert.Equal((0.0, 0.0, 0.0), Poll(_dir.File("missing")));

    [Fact]
    public void When_no_file_can_be_read_the_format_has_changed()
    {
        File.WriteAllText(_dir.File("a.json"), "{\"processId\":1}");
        File.WriteAllText(_dir.File("b.json"), "{\"processId\":2}");

        var ex = Assert.Throws<FormatException>(() => Poll());

        Assert.StartsWith("none of the 2 files in", ex.Message);
    }

    [Fact]
    public void Keys_go_stale_when_polling_stops()
    {
        Copy("busy.json");
        var now = Clock.T0;
        var store = new SensorStore(() => now);
        new ClaudeSessionsProvider(_dir.Path, pid => _processes.TryGetValue(pid, out var p) ? p : null, ["devbox"]).Poll(store);

        now = Clock.T0.AddSeconds(10);

        Assert.Null(Clock.Read(store, "claude.sessions.total"));
    }

    [Fact]
    public void The_real_process_lookup_finds_this_process()
    {
        // The default constructor's lookup, checked against a process that is certainly running: this one.
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        var folder = _dir.Path;
        File.WriteAllText(Path.Combine(folder, "me.json"),
            $"{{\"pid\":{self.Id},\"procStart\":\"{self.StartTime.ToUniversalTime().ToFileTimeUtc()}\",\"pidDomain\":\"win32:{System.Net.Dns.GetHostName().ToLowerInvariant()}\",\"status\":\"idle\"}}");
        var store = new SensorStore(() => Clock.T0);

        new ClaudeSessionsProvider(folder, ClaudeSessionsProvider.FindProcess, ClaudeSessionsProvider.ThisPc()).Poll(store);

        Assert.Equal(1.0, Clock.Read(store, "claude.sessions.idle"));
    }
}
