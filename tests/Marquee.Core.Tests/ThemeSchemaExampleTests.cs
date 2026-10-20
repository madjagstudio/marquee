using Marquee.Core.Tests.TestSupport;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Schema;
using Marquee.Core.Video;
using SkiaSharp;
using static Marquee.Core.Tests.TestSupport.Validations;

namespace Marquee.Core.Tests;

/// Each widget type's Example, which the theme editor starts a new widget from, is a widget of that type that
/// `marquee validate` passes and the renderer draws.
public class ThemeSchemaExampleTests
{
    public static TheoryData<string> Types => new(ThemeSchema.Widgets.Names);

    [Theory]
    [MemberData(nameof(Types))]
    public void Example_validates_and_renders(string type)
    {
        var example = ThemeSchema.Widget(type)!.Example;
        Assert.True(example is not null, $"The {type} widget has no Example; add one to ThemeSchema.WidgetExample");
        Assert.StartsWith($"type: {type}\n", example.ReplaceLineEndings("\n"), StringComparison.Ordinal);

        // The example as the only widget: its first line after `- `, the others indented to match.
        var lines = example.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
        var layout = "canvas: { orientation: landscape }\nwidgets:\n" + string.Join("\n", lines.Select((l, i) => (i == 0 ? "  - " : "    ") + l)) + "\n";
        using var theme = new TempTheme(layout);
        var widgets = Layouts.Parse(layout).Widgets;
        foreach (var image in Images(widgets))
            theme.AddImage(image, SKColors.Teal);
        var clips = Clips(widgets).ToList();
        // Nothing can write a clip without Media Foundation; MediaFoundationDecoderTests checks the error it gives.
        if (clips.Count > 0 && !MediaFoundation.IsAvailable) return;
        foreach (var clip in clips)
        {
            var path = Path.Combine(theme.Folder, clip);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            ClipWriter.Write(path, 64, 36, SKColors.Teal, SKColors.Teal);
        }

        Assert.Empty(Lines(Validate(theme.Folder)));
        using var loaded = theme.Load();
        using var frame = Frames.Render(loaded, TestSensors.Of(("cpu.load", 42), ("cpu.temp", 55), ("gpu.load", 30)));
        Assert.Equal((1920, 480), (frame.Width, frame.Height));
    }

    /// The image files the widgets show, which the test writes so the example's file is there.
    static IEnumerable<string> Images(IEnumerable<WidgetSpec> widgets) =>
        widgets.SelectMany(w => w switch
        {
            ImageWidget image => [image.Path],
            GroupWidget group => Images(group.Children),
            _ => [],
        });

    /// The clips the widgets play, which the test writes as H.264 MP4s.
    static IEnumerable<string> Clips(IEnumerable<WidgetSpec> widgets) =>
        widgets.SelectMany(w => w switch
        {
            VideoWidget video => [video.Path],
            GroupWidget group => Clips(group.Children),
            _ => [],
        });
}
