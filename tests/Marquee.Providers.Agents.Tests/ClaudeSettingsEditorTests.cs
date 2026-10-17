using System.Text.Json.Nodes;
using Marquee.Providers.Agents.Claude;
using Marquee.Providers.Agents.Tests.TestSupport;

namespace Marquee.Providers.Agents.Tests;

public class ClaudeSettingsEditorTests : IDisposable
{
    const string Bridge = "\"C:/Tools/Marquee/marquee.exe\" statusline";
    static readonly DateTimeOffset Now = new(2026, 10, 17, 13, 30, 0, TimeSpan.FromHours(-5));

    readonly TempDir _dir = new();

    string Settings => _dir.File("settings.json");
    string Saved => _dir.File("Marquee/claude-statusline.json");

    public void Dispose() => _dir.Dispose();

    SettingsChange Install(string bridge = Bridge, bool dryRun = false) => ClaudeSettingsEditor.Install(Settings, Saved, bridge, Now, dryRun);

    SettingsChange Undo(bool dryRun = false) => ClaudeSettingsEditor.Undo(Settings, Saved, Now, dryRun);

    JsonObject Read() => (JsonObject)JsonNode.Parse(File.ReadAllText(Settings))!;

    string[] Backups() => Directory.GetFiles(_dir.Path, "settings.json.marquee-backup-*");

    [Theory]
    [InlineData(@"C:\Tools\Marquee\marquee.exe", true, "\"C:/Tools/Marquee/marquee.exe\" statusline")]
    [InlineData(@"C:\Users\Dana\My Apps\marquee.exe", true, "\"C:/Users/Dana/My Apps/marquee.exe\" statusline")]
    [InlineData(@"C:\odd$dir\marquee.exe", true, "\"C:/odd\\$dir/marquee.exe\" statusline")]
    [InlineData(@"C:\Users\Dana\My Apps\marquee.exe", false, "& 'C:\\Users\\Dana\\My Apps\\marquee.exe' statusline")]
    [InlineData(@"C:\Users\O'Neil\marquee.exe", false, "& 'C:\\Users\\O''Neil\\marquee.exe' statusline")]
    [InlineData("C:\\Users\\O\u2019Neil\\marquee.exe", false, "& 'C:\\Users\\O\u2019\u2019Neil\\marquee.exe' statusline")]
    public void Bridge_command_is_quoted_for_the_shell(string exe, bool gitBash, string expected) =>
        Assert.Equal(expected, ClaudeSettingsEditor.BridgeCommand(exe, gitBash));

    [Theory]
    [InlineData("""{ "type": "command", "command": "\"C:/x/marquee.exe\" statusline" }""", true)]
    [InlineData("""{ "type": "command", "command": "& 'C:\\x\\Marquee.exe' statusline" }""", true)]
    [InlineData("""{ "type": "command", "command": "bash ~/.claude/statusline.sh" }""", false)]
    [InlineData("""{ "type": "command", "command": "~/marquee-statusline.sh" }""", false)]
    [InlineData("\"marquee statusline\"", false)]
    [InlineData("""{ "type": "command", "command": "bash ~/marquee/run.sh statusline" }""", false)]
    [InlineData("""{ "type": "command", "command": "marquee statusline" }""", true)]
    [InlineData("""{ "type": "command", "command": "notmarquee statusline" }""", false)]
    [InlineData("""{ "type": "command", "command": "my-marquee statusline" }""", false)]
    [InlineData("""{ "type": "command", "command": "\"C:/x/my_marquee.exe\" statusline" }""", false)]
    [InlineData("""{ "type": "command", "command": "~/bin/old.marquee statusline" }""", false)]
    public void Recognizes_its_own_status_line(string json, bool expected) =>
        Assert.Equal(expected, ClaudeSettingsEditor.IsBridge(JsonNode.Parse(json)));

    [Fact]
    public void Install_without_a_settings_file_creates_one()
    {
        var change = Install();

        Assert.True(change.Changed);
        Assert.Equal("Claude Code's status line now goes through Marquee. There was no status line before, so it stays blank. " +
            $"Created {Settings}.", change.Message);
        Assert.Equal("{\n  \"statusLine\": {\n    \"type\": \"command\",\n    \"command\": \"\\\"C:/Tools/Marquee/marquee.exe\\\" statusline\"\n  }\n}\n",
            File.ReadAllText(Settings));
        Assert.True(SavedStatusLine.TryLoad(Saved, out var previous));
        Assert.Null(previous);
    }

    [Fact]
    public void Install_keeps_every_other_key_in_order_and_saves_the_previous_status_line()
    {
        File.WriteAllText(Settings, """
            {
              "env": { "A": "1" },
              "statusLine": { "type": "command", "command": "bash ~/sl.sh", "padding": 2 },
              "theme": "dark",
              "hooks": { "Stop": [ { "hooks": [ { "type": "command", "command": "echo <done> & café" } ] } ] }
            }
            """);
        var before = File.ReadAllText(Settings);

        var change = Install();

        var settings = Read();
        Assert.Equal(["env", "statusLine", "theme", "hooks"], settings.Select(p => p.Key));
        var statusLine = settings["statusLine"]!.AsObject();
        Assert.Equal(["type", "command", "padding"], statusLine.Select(p => p.Key));
        Assert.Equal(Bridge, (string)statusLine["command"]!);
        Assert.Equal(2, (int)statusLine["padding"]!);
        Assert.Equal("echo <done> & café", (string)settings["hooks"]!["Stop"]![0]!["hooks"]![0]!["command"]!);
        Assert.Contains("echo <done> & café", File.ReadAllText(Settings)); // not escaped as \u003C
        Assert.Contains("marquee statusline runs `bash ~/sl.sh`", change.Message);

        Assert.True(SavedStatusLine.TryLoad(Saved, out var previous));
        Assert.Equal("""{"type":"command","command":"bash ~/sl.sh","padding":2}""", previous!.ToJsonString());
        Assert.Equal(before, File.ReadAllText(Assert.Single(Backups())));
    }

    [Fact]
    public void Installing_twice_changes_nothing_the_second_time()
    {
        File.WriteAllText(Settings, """{ "statusLine": { "type": "command", "command": "bash ~/sl.sh" } }""");
        Install();
        var after = File.ReadAllText(Settings);

        var change = Install();

        Assert.False(change.Changed);
        Assert.Equal(after, File.ReadAllText(Settings));
        Assert.True(SavedStatusLine.TryLoad(Saved, out var previous));
        Assert.Equal("bash ~/sl.sh", SavedStatusLine.Command(previous));
    }

    [Fact]
    public void Installing_from_a_new_location_updates_the_command_and_keeps_the_saved_status_line()
    {
        File.WriteAllText(Settings, """{ "statusLine": { "type": "command", "command": "bash ~/sl.sh" } }""");
        Install();

        var change = Install("\"D:/Marquee/marquee.exe\" statusline");

        Assert.Equal("Claude Code's status line now runs this copy of Marquee.", change.Message.Split(" The old")[0]);
        Assert.Equal("\"D:/Marquee/marquee.exe\" statusline", (string)Read()["statusLine"]!["command"]!);
        Assert.True(SavedStatusLine.TryLoad(Saved, out var previous));
        Assert.Equal("bash ~/sl.sh", SavedStatusLine.Command(previous));
    }

    [Fact]
    public void Dry_run_writes_nothing()
    {
        File.WriteAllText(Settings, """{ "theme": "dark" }""");

        var change = Install(dryRun: true);

        Assert.True(change.Changed);
        Assert.Contains("marquee.exe", change.NewSettings);
        Assert.Equal("""{ "theme": "dark" }""", File.ReadAllText(Settings));
        Assert.False(File.Exists(Saved));
        Assert.Empty(Backups());
    }

    [Theory]
    [InlineData("""{ "theme": "dark", }""")]
    [InlineData("{ // my settings\n \"theme\": \"dark\" }")]
    [InlineData("not json")]
    [InlineData("""[ "a list" ]""")]
    [InlineData("""{ "statusLine": "echo hi" }""")]
    public void Files_it_cannot_round_trip_are_refused_untouched(string contents)
    {
        File.WriteAllText(Settings, contents);

        var ex = Assert.Throws<ClaudeSetupException>(() => Install());

        Assert.Contains("Nothing was changed", ex.Message);
        Assert.Equal(contents, File.ReadAllText(Settings));
        Assert.False(File.Exists(Saved));
    }

    [Fact]
    public void Crlf_line_endings_and_a_bom_are_handled()
    {
        File.WriteAllText(Settings, "\uFEFF{\r\n  \"theme\": \"dark\"\r\n}\r\n");

        Install();

        var text = File.ReadAllText(Settings);
        Assert.Contains("\"theme\": \"dark\",\r\n", text);
        Assert.DoesNotContain("\n", text.Replace("\r\n", "")); // every newline is CRLF
        Assert.NotEqual(0xEF, File.ReadAllBytes(Settings)[0]);
    }

    [Fact]
    public void Undo_restores_the_previous_status_line_exactly()
    {
        File.WriteAllText(Settings, """{ "statusLine": { "type": "command", "command": "bash ~/sl.sh", "padding": 2 }, "theme": "dark" }""");
        Install();

        var change = Undo();

        Assert.Equal("Restored Claude Code's previous status line.", change.Message.Split(" The old")[0]);
        Assert.Equal("""{"statusLine":{"type":"command","command":"bash ~/sl.sh","padding":2},"theme":"dark"}""", Read().ToJsonString());
        Assert.False(File.Exists(Saved));
        Assert.Equal(2, Backups().Length); // one per write, even within the same second
    }

    [Fact]
    public void Undo_dry_run_changes_nothing()
    {
        File.WriteAllText(Settings, """{ "theme": "dark" }""");
        Install();
        var installed = File.ReadAllText(Settings);

        var change = Undo(dryRun: true);

        Assert.True(change.Changed);
        Assert.Equal(installed, File.ReadAllText(Settings));
        Assert.True(File.Exists(Saved));
    }

    [Fact]
    public void Undo_removes_the_status_line_when_there_was_none()
    {
        File.WriteAllText(Settings, """{ "theme": "dark" }""");
        Install();

        Undo();

        Assert.Equal("""{"theme":"dark"}""", Read().ToJsonString());
    }

    [Fact]
    public void Undo_leaves_a_status_line_changed_since_setup_alone()
    {
        File.WriteAllText(Settings, """{ "statusLine": { "type": "command", "command": "bash ~/sl.sh" } }""");
        Install();
        File.WriteAllText(Settings, """{ "statusLine": { "type": "command", "command": "bash ~/new.sh" } }""");

        var ex = Assert.Throws<ClaudeSetupException>(() => Undo());

        Assert.Contains("leaving it alone", ex.Message);
        Assert.Contains("bash ~/new.sh", File.ReadAllText(Settings));
        Assert.True(File.Exists(Saved));
    }

    [Fact]
    public void Undo_without_setup_does_nothing()
    {
        File.WriteAllText(Settings, """{ "theme": "dark" }""");

        var change = Undo();

        Assert.False(change.Changed);
        Assert.Equal("""{ "theme": "dark" }""", File.ReadAllText(Settings));
    }

    [Fact]
    public void Undo_refuses_an_unreadable_saved_file()
    {
        File.WriteAllText(Settings, """{ "theme": "dark" }""");
        Install();
        File.WriteAllText(Saved, "garbage");

        Assert.Throws<ClaudeSetupException>(() => Undo());
        Assert.True(ClaudeSettingsEditor.IsBridge(Read()["statusLine"]));
    }

    [Fact]
    public void Invalid_utf8_is_refused_untouched()
    {
        byte[] bytes = [.. "{\"a\":\"caf"u8, 0xE9, .. "\"}"u8];
        File.WriteAllBytes(Settings, bytes);

        var ex = Assert.Throws<ClaudeSetupException>(() => Install());

        Assert.Contains("Nothing was changed", ex.Message);
        Assert.Equal(bytes, File.ReadAllBytes(Settings));
        Assert.False(File.Exists(Saved));
    }

    [Fact]
    public void A_utf16_file_is_refused_untouched()
    {
        byte[] bytes = [0xFF, 0xFE, .. System.Text.Encoding.Unicode.GetBytes("{\"a\":1}")];
        File.WriteAllBytes(Settings, bytes);

        var ex = Assert.Throws<ClaudeSetupException>(() => Install());

        Assert.Contains("Nothing was changed", ex.Message);
        Assert.Equal(bytes, File.ReadAllBytes(Settings));
        Assert.False(File.Exists(Saved));
    }

    [Theory]
    [InlineData("""{"a":1,"a":2}""")]
    [InlineData("""{"a":"\ud800"}""")]
    [InlineData("""{"statusLine":{"type":"command","command":"a","command":"b"}}""")]
    [InlineData("""{"statusLine":{"type":"command","command":"a","extra":{"k":1,"k":2}}}""")]
    public void Settings_that_fail_to_load_or_write_are_refused_untouched(string contents)
    {
        File.WriteAllText(Settings, contents);

        var ex = Assert.Throws<ClaudeSetupException>(() => Install());

        Assert.Contains("Nothing was changed", ex.Message);
        Assert.Equal(contents, File.ReadAllText(Settings));
        Assert.False(File.Exists(Saved));
        Assert.Empty(Backups());
    }

    [Fact]
    public void Undo_refuses_a_repeated_key_inside_the_status_line_untouched()
    {
        Install();
        var contents = File.ReadAllText(Settings).Replace("\"type\": \"command\"", "\"type\": \"command\", \"padding\": 1, \"padding\": 2");
        File.WriteAllText(Settings, contents);

        var ex = Assert.Throws<ClaudeSetupException>(() => Undo());

        Assert.Contains("Nothing was changed", ex.Message);
        Assert.Equal(contents, File.ReadAllText(Settings));
        Assert.True(File.Exists(Saved));
    }

    [Fact]
    public void A_symlinked_settings_file_is_refused_untouched()
    {
        var target = _dir.Write("real/settings.json", """{ "theme": "dark" }""");
        try
        {
            File.CreateSymbolicLink(Settings, target);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return; // symlinks need Developer Mode or elevation on Windows
        }

        var error = Assert.Throws<ClaudeSetupException>(() => Install());

        Assert.Contains("Nothing was changed", error.Message);
        Assert.Equal("""{ "theme": "dark" }""", File.ReadAllText(target));
        Assert.False(File.Exists(Saved));
        Assert.Empty(Directory.GetFiles(_dir.Path, "*.marquee-tmp"));
    }
}
