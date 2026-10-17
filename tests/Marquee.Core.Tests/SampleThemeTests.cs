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

    static SensorSnapshot Typical()
    {
        var store = TestSensors.Store(
            ("cpu.temp", 64), ("cpu.load", 23), ("cpu.clock", 5250),
            ("gpu.temp", 58), ("gpu.vram_used", 9.2 * 1024 * 1024 * 1024), ("ram.used_pct", 71));
        for (var i = 0; i < 120; i++)
            store.Set("gpu.load", Math.Round(55 + 35 * Math.Sin(i / 9.0) + 8 * Math.Sin(i / 2.3)));
        return store.Snapshot();
    }

    /// CPU over its hot threshold, RAM nearly full, and the GPU provider gone quiet.
    static SensorSnapshot Hot() => TestSensors.Of(("cpu.temp", 92), ("cpu.load", 96), ("cpu.clock", 5700), ("ram.used_pct", 95));

    [Theory]
    [InlineData("example-landscape")]
    [InlineData("example-portrait")]
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
}
