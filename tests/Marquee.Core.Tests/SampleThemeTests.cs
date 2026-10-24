using System.Runtime.CompilerServices;
using Marquee.Core.Rendering;
using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using Marquee.Core.Tests.TestSupport;
using Marquee.Core.Video;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// The themes shipped in themes/ render as approved, with fixed sensor values and animation time 0.
public class SampleThemeTests
{
    /// The showcase's clip, drawn by tools/Marquee.MakeClip.
    const string ShowcaseClip = "video/radar.mp4";

    static string ThemeFolder(string name, [CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", "..", "themes", name));

    /// Without the shader cost check: whether a shader fits the frame budget depends on the machine (a slower PC skips
    /// the aurora by design), and ShaderCostTests covers the check itself.
    static LoadedTheme Load(string name) => ThemeLoader.Load(ThemeFolder(name), null, TestProbes.Free, TestProbes.Free);

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
            ("claude.seven_day_model.pct", 64), ("claude.seven_day_model.resets_at", now + 3 * 86400 + 5 * 3600),
            ("claude.context.pct", 31), ("claude.updated_at", now - 180),
            ("claude.sessions.total", 3), ("claude.sessions.busy", 1), ("claude.sessions.idle", 2),
            ("codex.primary.pct", 52), ("codex.primary.window_min", 10080), ("codex.primary.resets_at", now + 4 * 86400 + 3600),
            ("codex.sessions.total", 1), ("codex.sessions.busy", 1), ("codex.sessions.idle", 0),
        })
            store.Set(key, value);
        store.Set("claude.seven_day_model.name", "Opus");
        store.Set("custom.status", "deploy ok");
        return store;
    }

    /// CPU over its hot threshold, RAM nearly full, and the GPU provider gone quiet.
    static SensorSnapshot Hot() => TestSensors.Of(("cpu.temp", 92), ("cpu.load", 96), ("cpu.clock", 5700), ("ram.used_pct", 95));

    public static TheoryData<string> ExampleThemes
    {
        get
        {
            var folders = Directory.GetDirectories(ThemeFolder(""), "example-*");
            Assert.NotEmpty(folders);
            return new(folders.Select(folder => Path.GetFileName(folder)).OrderBy(name => name, StringComparer.Ordinal));
        }
    }

    /// The start screen's starting points, which New theme… copies into a user's themes.
    public static TheoryData<string> StartThemes
    {
        get
        {
            var folders = Directory.GetDirectories(ThemeFolder(""), "start-*");
            Assert.NotEmpty(folders);
            return new(folders.Select(folder => Path.GetFileName(folder)).OrderBy(name => name, StringComparer.Ordinal));
        }
    }

    [Theory]
    [MemberData(nameof(StartThemes))]
    public void Start_themes_load_without_warnings(string name)
    {
        using var theme = Load(name);

        Assert.Empty(theme.Warnings);
        Assert.Empty(Validations.Lines(Validations.Validate(ThemeFolder(name))));
    }

    [Theory]
    [MemberData(nameof(ExampleThemes))]
    public void Sample_themes_explicitly_set_a_frame_rate_and_ring(string name)
    {
        var yaml = File.ReadAllText(Path.Combine(ThemeFolder(name), "layout.yaml"));
        // Parsing defaults a missing fps to 30, so check that the example actually teaches the setting.
        Assert.True(YamlValues.Read(yaml).ContainsKey("canvas.fps"), $"{name} must set canvas.fps.");
        var spec = Layouts.Parse(yaml);
        Assert.InRange(spec.Canvas.Fps, 1, CanvasSpec.MaxFps);
        Assert.True(spec.Ring is not null, $"{name} must have a ring block.");
    }

    [Theory]
    [MemberData(nameof(ExampleThemes))]
    public void Sample_themes_load_without_warnings(string name)
    {
        if (name == "example-showcase" && !MediaFoundation.IsAvailable) return; // Its clip can't play there.
        using var theme = Load(name);

        Assert.Empty(theme.Warnings);
        Assert.Empty(Validations.Lines(Validations.Validate(ThemeFolder(name))));
    }

    [Theory]
    [InlineData("example-landscape")]
    [InlineData("example-portrait")]
    public void Sample_theme_with_typical_values(string name)
    {
        using var theme = Load(name);
        using var frame = Frames.Render(theme, Typical());

        Snapshots.Match(frame, name);
    }

    [Theory]
    [InlineData("example-landscape")]
    [InlineData("example-portrait")]
    public void Sample_theme_running_hot_with_a_stale_gpu(string name)
    {
        using var theme = Load(name);
        using var frame = Frames.Render(theme, Hot());

        Snapshots.Match(frame, $"{name}-hot");
    }

    /// Typical vitals with a CPU load history for the showcase's chart, and RAM in bytes. No game is in front, so the
    /// FPS keys are missing.
    static SensorSnapshot Showcase()
    {
        var store = TypicalStore();
        for (var i = 0; i < 120; i++)
            store.Set("cpu.load", Math.Round(30 + 14 * Math.Sin(i / 13.0 + 1) + 6 * Math.Sin(i / 3.1)));
        store.Set("cpu.load", 23);
        store.Set("ram.used", 22.7 * 1024 * 1024 * 1024);
        return store.Snapshot();
    }

    /// A game in front: the CPU over its hot threshold under heavy load, RAM nearly full, and the GPU provider gone
    /// quiet.
    static SensorSnapshot ShowcaseHot()
    {
        var store = TestSensors.Store(
            ("cpu.temp", 92), ("cpu.clock", 5700), ("ram.used_pct", 95), ("ram.used", 30.4 * 1024 * 1024 * 1024),
            ("fps", 141), ("fps.1pct_low", 97));
        for (var i = 0; i < 120; i++)
            store.Set("cpu.load", Math.Round(80 + 12 * Math.Sin(i / 7.0) + 4 * Math.Sin(i / 2.2)));
        store.Set("cpu.load", 96);
        return store.Snapshot();
    }

    /// The showcase at the test sensors' fixed time in the pinned zone and culture (Friday, 17:30), with its clip on its
    /// first frame, which the player has decoded by the time the theme has loaded.
    static SKImage RenderShowcase(SensorSnapshot sensors)
    {
        using var theme = Load("example-showcase");
        using var renderer = new ThemeRenderer(theme) { Locale = TestClock.Locale() };
        using var frame = renderer.Render(sensors, TimeSpan.Zero);
        return Frames.ThroughSink(frame);
    }

    [Fact]
    public void Showcase_theme_with_typical_values()
    {
        if (!MediaFoundation.IsAvailable) return; // Its clip can't play there; the video tests check the warning.
        using var frame = RenderShowcase(Showcase());

        Snapshots.Match(frame, "example-showcase");
    }

    [Fact]
    public void Showcase_theme_running_hot_in_a_game_with_a_stale_gpu()
    {
        if (!MediaFoundation.IsAvailable) return;
        using var frame = RenderShowcase(ShowcaseHot());

        Snapshots.Match(frame, "example-showcase-hot");
    }

    /// The clip is committed beside the theme, so it stays small enough for the repository and the installer, and is a
    /// loop of a few seconds.
    [Fact]
    public void Showcase_clip_is_under_a_megabyte_and_a_few_seconds_long()
    {
        var clip = Path.Combine(ThemeFolder("example-showcase"), ShowcaseClip);

        Assert.InRange(new FileInfo(clip).Length, 1, 1024 * 1024 - 1);
        if (!MediaFoundation.IsAvailable) return;
        using var decoder = VideoDecoders.Open(clip);
        var end = TimeSpan.Zero;
        while (decoder.Next() is { } frame) end = frame.Start + frame.Duration;
        Assert.InRange(end, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Agents_theme_with_claude_and_codex_working()
    {
        using var theme = Load("example-agents");
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
        using var theme = Load("example-agents");
        using var frame = Frames.Render(theme, store.Snapshot(), TimeSpan.FromSeconds(12));

        Snapshots.Match(frame, "example-agents-two-windows");
    }

    /// A Codex secondary window that is neither 5-hour nor weekly is labeled with its length.
    [Fact]
    public void Agents_theme_labels_a_codex_window_by_its_length()
    {
        var store = AgentsStore();
        var now = TestSensors.UnixTime;
        foreach (var (key, value) in new (string, double)[]
        {
            ("codex.primary.pct", 12), ("codex.primary.window_min", 300), ("codex.primary.resets_at", now + 2 * 3600 + 30 * 60),
            ("codex.secondary.pct", 45), ("codex.secondary.window_min", 1440), ("codex.secondary.resets_at", now + 9 * 3600),
        })
            store.Set(key, value);
        using var theme = Load("example-agents");
        using var frame = Frames.Render(theme, store.Snapshot(), TimeSpan.FromSeconds(12));

        Snapshots.Match(frame, "example-agents-daily-window");
    }

    [Fact]
    public void Agents_theme_before_any_agent_data()
    {
        using var theme = Load("example-agents");
        using var frame = Frames.Render(theme, Typical(), TimeSpan.FromSeconds(12));

        Snapshots.Match(frame, "example-agents-no-data");
    }
}
