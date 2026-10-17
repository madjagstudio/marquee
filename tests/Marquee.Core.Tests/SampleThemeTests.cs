using System.Runtime.CompilerServices;
using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using Marquee.Core.Tests.TestSupport;

namespace Marquee.Core.Tests;

/// The themes shipped in themes/ render as approved, with fixed sensor values and animation time 0.
public class SampleThemeTests
{
    static string ThemeFolder(string name, [CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", "..", "themes", name));

    static SensorSnapshot Typical() => TypicalStore().Snapshot();

    static SensorStore TypicalStore()
    {
        var store = TestSensors.Store(
            ("cpu.temp", 64), ("cpu.load", 23), ("cpu.clock", 5250),
            ("gpu.temp", 58), ("gpu.vram_used", 9.2 * 1024 * 1024 * 1024), ("ram.used_pct", 71));
        for (var i = 0; i < 120; i++)
            store.Set("gpu.load", Math.Round(55 + 35 * Math.Sin(i / 9.0) + 8 * Math.Sin(i / 2.3)));
        return store;
    }

    /// Typical vitals plus Claude Code and Codex mid-week: one session of each working, limits partly used.
    static SensorSnapshot Agents() => AgentsStore().Snapshot();

    static SensorStore AgentsStore()
    {
        var store = TypicalStore();
        var now = TestSensors.UnixTime;
        foreach (var (key, value) in new (string, double)[]
        {
            ("claude.five_hour.pct", 42), ("claude.five_hour.resets_at", now + 2 * 3600 + 14 * 60),
            ("claude.seven_day.pct", 86), ("claude.seven_day.resets_at", now + 3 * 86400 + 5 * 3600),
            ("claude.context.pct", 31), ("claude.updated_at", now - 180),
            ("claude.sessions.total", 3), ("claude.sessions.busy", 1), ("claude.sessions.idle", 2),
            ("codex.primary.pct", 52), ("codex.primary.window_min", 10080), ("codex.primary.resets_at", now + 4 * 86400 + 3600),
            ("codex.sessions.total", 1), ("codex.sessions.busy", 1), ("codex.sessions.idle", 0),
        })
            store.Set(key, value);
        store.Set("custom.status", "deploy ok");
        return store;
    }

    /// CPU over its hot threshold, RAM nearly full, and the GPU provider gone quiet.
    static SensorSnapshot Hot() => TestSensors.Of(("cpu.temp", 92), ("cpu.load", 96), ("cpu.clock", 5700), ("ram.used_pct", 95));

    [Theory]
    [InlineData("example-landscape")]
    [InlineData("example-portrait")]
    [InlineData("example-agents")]
    public void Sample_themes_load_without_warnings(string name)
    {
        using var theme = ThemeLoader.Load(ThemeFolder(name));

        Assert.Empty(theme.Warnings);
    }

    [Theory]
    [InlineData("example-landscape")]
    [InlineData("example-portrait")]
    public void Sample_theme_with_typical_values(string name)
    {
        using var theme = ThemeLoader.Load(ThemeFolder(name));
        using var frame = Frames.Render(theme, Typical());

        Snapshots.Match(frame, name);
    }

    [Theory]
    [InlineData("example-landscape")]
    [InlineData("example-portrait")]
    public void Sample_theme_running_hot_with_a_stale_gpu(string name)
    {
        using var theme = ThemeLoader.Load(ThemeFolder(name));
        using var frame = Frames.Render(theme, Hot());

        Snapshots.Match(frame, $"{name}-hot");
    }

    [Fact]
    public void Agents_theme_with_claude_and_codex_working()
    {
        using var theme = ThemeLoader.Load(ThemeFolder("example-agents"));
        using var frame = Frames.Render(theme, Agents(), TimeSpan.FromSeconds(12));

        Snapshots.Match(frame, "example-agents");
    }

    /// A plan whose Codex limits are a 5-hour window (primary) and a weekly one (secondary).
    [Fact]
    public void Agents_theme_with_a_five_hour_and_a_weekly_codex_window()
    {
        var store = AgentsStore();
        var now = TestSensors.UnixTime;
        foreach (var (key, value) in new (string, double)[]
        {
            ("codex.primary.pct", 12), ("codex.primary.window_min", 300), ("codex.primary.resets_at", now + 2 * 3600 + 30 * 60),
            ("codex.secondary.pct", 30), ("codex.secondary.window_min", 10080), ("codex.secondary.resets_at", now + 3 * 86400 + 21 * 3600),
        })
            store.Set(key, value);
        using var theme = ThemeLoader.Load(ThemeFolder("example-agents"));
        using var frame = Frames.Render(theme, store.Snapshot(), TimeSpan.FromSeconds(12));

        Snapshots.Match(frame, "example-agents-two-windows");
    }

    [Fact]
    public void Agents_theme_before_any_agent_data()
    {
        using var theme = ThemeLoader.Load(ThemeFolder("example-agents"));
        using var frame = Frames.Render(theme, Typical(), TimeSpan.FromSeconds(12));

        Snapshots.Match(frame, "example-agents-no-data");
    }
}
