using System.Globalization;
using System.Resources;
using Marquee.Core.Themes.Editing;
using SkiaSharp;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Parsing;

namespace Marquee.Editor.Model;

public enum ModuleCategory { Numbers, Gauges, Graphs, Time, Pictures, Shapes, MyModules, BlankWidgets }
public sealed record Module(string Id, string Name, ModuleCategory Category, string Yaml, bool NeedsFile);

internal static class ModuleText
{
    static readonly ResourceManager Resources = new("Marquee.Editor.Model.Resources.Strings", typeof(ModuleText).Assembly);
    public static string Get(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture) ?? throw new InvalidOperationException(key);
}

public static class ModuleLibrary
{
    static readonly (string Id, string Key, ModuleCategory Category)[] Entries =
    [
        ("big-number", "BigNumber", ModuleCategory.Numbers), ("number-label", "NumberLabel", ModuleCategory.Numbers),
        ("temperature-dial", "TemperatureDial", ModuleCategory.Gauges), ("arc-gauge", "ArcGauge", ModuleCategory.Gauges),
        ("segmented-ring", "SegmentedRing", ModuleCategory.Gauges), ("usage-bar", "UsageBar", ModuleCategory.Gauges), ("segmented-bar", "SegmentedBar", ModuleCategory.Gauges),
        ("load-history", "LoadHistory", ModuleCategory.Graphs), ("two-line-history", "TwoLineHistory", ModuleCategory.Graphs), ("memory-donut", "MemoryDonut", ModuleCategory.Graphs),
        ("clock", "Clock", ModuleCategory.Time), ("clock-date", "ClockDate", ModuleCategory.Time),
        ("image", "Image", ModuleCategory.Pictures), ("video", "Video", ModuleCategory.Pictures),
        ("card", "Card", ModuleCategory.Shapes), ("label", "Label", ModuleCategory.Shapes),
    ];
    public static IReadOnlyList<Module> All { get; } = Entries.Select(entry =>
    {
        using var stream = typeof(ModuleLibrary).Assembly.GetManifestResourceStream($"Marquee.Editor.Model.Modules.{entry.Id}.yaml")!;
        using var reader = new StreamReader(stream);
        return new Module(entry.Id, ModuleText.Get(entry.Key), entry.Category, reader.ReadToEnd(), entry.Category == ModuleCategory.Pictures);
    }).ToList();
    public static IReadOnlyList<string> Keys { get; } = All.SelectMany(module => ThemeKeys.Of(LayoutParser.Parse(
        "canvas: { orientation: landscape }\nwidgets:\n  - " + module.Yaml.Trim().Replace("\r\n", "\n").Replace("\n", "\n    ") + "\n")))
        .Distinct(StringComparer.Ordinal).ToList();
    public static string CategoryName(ModuleCategory category) => ModuleText.Get(category.ToString());
    public static string Description(Module module) => Entries.FirstOrDefault(e => e.Id == module.Id) is var entry && entry.Key is not null
        ? ModuleText.Get(entry.Key + "Description") : module.Name;
    public static SKSize Size(Module module)
    {
        var document = LayoutDocument.Open(module.Yaml);
        var path = YamlPath.Root.Child("size");
        return new(Number(path.Item(0)), Number(path.Item(1)));
        float Number(YamlPath at) => document.TryGetScalar(at, out var value) ? float.Parse(value, CultureInfo.InvariantCulture) : 240;
    }
}
