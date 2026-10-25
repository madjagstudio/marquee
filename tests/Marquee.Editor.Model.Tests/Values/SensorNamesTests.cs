using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Marquee.Editor.Model.Values;

namespace Marquee.Editor.Model.Tests.Values;

public class SensorNamesTests
{
    [Theory]
    [InlineData("cpu.temp", "CPU temperature", SensorCategory.Cpu, "°C")]
    [InlineData("claude.seven_day.pct", "Claude Code: weekly limit used", SensorCategory.ClaudeCode, "%")]
    [InlineData("fps.1pct_low", "FPS: 1% low", SensorCategory.Gaming, "fps")]
    [InlineData("fan.case_front", "Fan: Case Front", SensorCategory.Fans, "RPM")]
    [InlineData("custom.build_count", "Your value: Build Count", SensorCategory.Yours, null)]
    [InlineData("weather.wind_speed", "Weather Wind Speed", SensorCategory.Other, null)]
    public void Names_include_the_category_and_unit(string key, string name, SensorCategory category, string? unit) =>
        Assert.Equal(new SensorName(key, name, category, unit), SensorNames.For(key));

    [Fact]
    public void Byte_readings_show_as_sizes_like_the_bytes_helper()
    {
        Assert.Equal("3.3 GB", SensorNames.Format("gpu.vram_used", Marquee.Core.Sensors.SensorValue.Of(3529506816)));
        Assert.Equal("512 B", SensorNames.Format("ram.used", Marquee.Core.Sensors.SensorValue.Of(512)));
    }

    [Fact]
    public void All_concrete_sample_and_provider_keys_have_deliberate_names()
    {
        var root = Repository();
        var files = new[] { "Marquee.Providers.Hardware", "Marquee.Providers.Fps", "Marquee.Providers.Agents" }
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, "src", folder), "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar));
        var keys = files.SelectMany(file => Regex.Matches(File.ReadAllText(file),
                "\"(?<key>(?:cpu|gpu|ram|claude|codex|fps)\\.[a-z0-9_.]+|fps|frametime)\"")
            .Select(match => match.Groups["key"].Value))
            .Where(key => key != "claude.exe")
            // These prefixes are expanded by the agents providers when publishing windows.
            .SelectMany(key => key is "claude.five_hour" or "claude.seven_day" or "claude.seven_day_model"
                ? new[] { key + ".pct", key + ".resets_at" }
                : key is "codex.primary" or "codex.secondary"
                    ? new[] { key + ".pct", key + ".resets_at", key + ".window_min" } : new[] { key })
            .Concat(Samples.Keys).Append("now").Distinct();
        foreach (var key in keys)
        {
            var named = SensorNames.For(key);
            Assert.NotEqual(SensorCategory.Other, named.Category);
            Assert.NotEqual(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(key.Replace('.', ' ').Replace('_', ' ')), named.Name);
        }
    }

    [Fact]
    public void Groups_follow_category_order_then_names_alphabetically()
    {
        var groups = SensorNames.Grouped(["custom.z", "gpu.temp", "cpu.temp", "now", "cpu.clock", "custom.a"]);
        Assert.Equal([SensorCategory.Cpu, SensorCategory.Gpu, SensorCategory.TimeAndDate, SensorCategory.Yours], groups.Select(g => g.Key));
        Assert.Equal(["CPU clock speed", "CPU temperature"], groups.First().Select(n => n.Name));
        Assert.Equal(["Your value: A", "Your value: Z"], groups.Last().Select(n => n.Name));
    }

    static string Repository([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../.."));
}
