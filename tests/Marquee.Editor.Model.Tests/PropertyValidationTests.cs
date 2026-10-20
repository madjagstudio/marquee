using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes.Schema;

namespace Marquee.Editor.Model.Tests;

/// Each kind of key is checked against the catalog before it is written, with the reason a value is refused.
public class PropertyValidationTests
{
    static readonly CheckContext Context = new(["meter", "hot"]);

    static string? Check(SchemaKey key, YamlValue value, CheckContext? context = null) => PropertyValues.Check(key, value, context ?? Context);

    static SchemaKey Widget(string type, string key) => ThemeSchema.WidgetKeys(type).Single(k => k.Name == key);

    static SchemaKey Style(string key) => ThemeSchema.Style.Key(key)!;

    static YamlList List(params string[] items) => new(items.Select(item => (YamlValue)item).ToList());

    [Fact]
    public void Numbers_are_checked_against_their_range()
    {
        Assert.Null(Check(Style("opacity"), "0.5"));
        Assert.Null(Check(Style("opacity"), "1e-1"));
        Assert.Equal("opacity must be between 0 and 1", Check(Style("opacity"), "1.5"));
        Assert.Equal("radius must be at least 0", Check(Style("radius"), "-1"));
        Assert.Equal("opacity must be a number, not 'half'", Check(Style("opacity"), "half"));
        Assert.Equal("opacity must be a number, not 'NaN'", Check(Style("opacity"), "NaN"));
        Assert.Equal("opacity is empty", Check(Style("opacity"), ""));
        Assert.Equal("opacity must be a single value, not a list or mapping", Check(Style("opacity"), List("1")));
        Assert.Null(Check(Widget("bar", "max"), "-1000000"));
    }

    [Fact]
    public void An_arcs_sweep_cant_be_0()
    {
        var shapeSweep = ThemeSchema.WidgetKeys("shape").Single(k => k.Name == "sweep");
        foreach (var sweep in new[] { Widget("gauge", "sweep"), shapeSweep })
        {
            Assert.Null(Check(sweep, "-90"));
            Assert.Equal("sweep can't be 0", Check(sweep, "0"));
            Assert.Equal("sweep can't be 0", Check(sweep, "-0.0"));
        }
    }

    [Fact]
    public void Whole_numbers_must_be_whole()
    {
        Assert.Null(Check(Widget("graph", "points"), "60"));
        Assert.Equal("points must be a whole number", Check(Widget("graph", "points"), "60.5"));
        Assert.Equal($"points must be between 2 and {ThemeSchema.Widget("graph")!.Key("points")!.Max}", Check(Widget("graph", "points"), "1"));
    }

    [Fact]
    public void Booleans_enums_and_font_weights_take_their_words()
    {
        Assert.Null(Check(Widget("text", "hidden"), "true"));
        Assert.Equal("hidden must be true or false, not 'yes'", Check(Widget("text", "hidden"), "yes"));
        Assert.Null(Check(Style("align"), "center"));
        Assert.Equal("align must be one of: left, center, right; not 'middle'", Check(Style("align"), "middle"));
        Assert.Null(Check(Style("weight"), "semibold"));
        Assert.Null(Check(Style("weight"), "650"));
        Assert.Equal("weight must be between 100 and 900, or one of: light, normal, medium, semibold, bold", Check(Style("weight"), "950"));
    }

    [Fact]
    public void Colors_and_paints_take_hex_and_gradients()
    {
        Assert.Null(Check(Style("color"), "#FF5A36"));
        Assert.Null(Check(Style("color"), "#FF5A3680"));
        Assert.Null(Check(Style("color"), "transparent"));
        Assert.StartsWith("color must be a color", Check(Style("color"), "red"));
        Assert.StartsWith("color must be a single value", Check(Style("color"), YamlValue.Parse("{ linear: [\"#000000\", \"#FFFFFF\"] }")));

        Assert.Null(Check(Style("fill"), YamlValue.Parse("{ linear: [\"#000000\", \"#FFFFFF\"], angle: 90 }")));
        Assert.Null(Check(Style("fill"), "#123"));
        Assert.Equal("fill: a gradient needs at least 2 colors", Check(Style("fill"), YamlValue.Parse("{ linear: [\"#000000\"] }")));
        Assert.Equal("fill: stops needs one number per color: 2, not 3",
            Check(Style("fill"), YamlValue.Parse("{ radial: [\"#000000\", \"#FFFFFF\"], stops: [0, 0.5, 1] }")));
        Assert.StartsWith("fill must be a color, or a gradient", Check(Style("fill"), YamlValue.Parse("{ conic: [\"#000000\"] }")));
    }

    [Fact]
    public void Texts_keep_to_their_length()
    {
        var name = Widget("text", "name");
        Assert.Null(Check(name, "Clock"));
        Assert.Null(Check(name, new string('x', name.MaxLength!.Value)));
        // Counted as people see characters: an emoji with a skin tone is one.
        Assert.Null(Check(name, string.Concat(Enumerable.Repeat("\U0001F44D\U0001F3FD", name.MaxLength.Value))));
        Assert.Equal($"name is too long (the limit is {name.MaxLength} characters)", Check(name, new string('x', name.MaxLength.Value + 1)));
        Assert.Equal("name is only spaces", Check(name, "   "));
    }

    [Fact]
    public void Expressions_keys_templates_and_formats_must_parse()
    {
        Assert.Null(Check(Widget("bar", "source"), "max(cpu.load, gpu.load)"));
        Assert.StartsWith("source: ", Check(Widget("bar", "source"), "cpu.load +"));
        Assert.Null(Check(Widget("graph", "source"), "gpu.load"));
        Assert.Equal("source must be a single sensor key, such as gpu.load", Check(Widget("graph", "source"), "gpu.load * 2"));
        Assert.Null(Check(Widget("text", "text"), "CPU {cpu.load:0}%"));
        Assert.StartsWith("text: ", Check(Widget("text", "text"), "CPU {cpu.load"));
        Assert.Null(Check(Widget("value", "format"), "{0:0.0}°C"));
        Assert.Null(Check(Widget("value", "format"), "time:HH:mm"));
        Assert.StartsWith("format: ", Check(Widget("value", "format"), "no placeholder"));
    }

    [Fact]
    public void A_key_that_takes_false_takes_it()
    {
        var labels = ThemeSchema.Ticks.Key("labels")!;
        Assert.Null(Check(labels, "false"));
        Assert.Null(Check(labels, "{0:0}"));
        Assert.StartsWith("labels: ", Check(labels, "true"));
    }

    [Fact]
    public void Files_stay_in_the_theme_folder_with_their_types()
    {
        var image = Widget("image", "src");
        var video = Widget("video", "src");
        Assert.Null(Check(image, "images/logo.png"));
        Assert.StartsWith("src must be a path inside the theme folder", Check(image, "../other/logo.png"));
        Assert.StartsWith("src must be a path inside the theme folder", Check(image, "C:/logo.png"));
        Assert.Null(Check(video, "video/clip.MP4"));
        Assert.StartsWith("src must be a ", Check(video, "video/clip.avi"));
    }

    [Fact]
    public void Points_and_sizes_are_two_numbers()
    {
        Assert.Null(Check(Widget("text", "at"), List("-10", "20.5")));
        Assert.Equal("at must be two numbers, [x, y]", Check(Widget("text", "at"), List("10")));
        Assert.Equal("at must be two numbers, [x, y]", Check(Widget("text", "at"), "10"));
        Assert.Null(Check(Widget("text", "size"), List("100", "1")));
        Assert.Equal("size must be at least 1", Check(Widget("text", "size"), List("100", "0")));
        Assert.Null(Check(Widget("shape", "size"), List("100", "0"), Context with { Min = 0 }));
    }

    [Fact]
    public void Style_names_must_be_the_themes()
    {
        Assert.Null(Check(ThemeSchema.Style.Key("extends")!, "meter"));
        Assert.Equal("extends: there is no style named 'cool' in styles:", Check(ThemeSchema.Style.Key("extends")!, "cool"));
        Assert.Null(Check(Widget("text", "style"), "hot"));
        Assert.Null(Check(Widget("text", "style"), YamlValue.Parse("{ size: 30, stroke: { width: 2 } }")));
        Assert.Equal("style.size must be between 1 and 1000", Check(Widget("text", "style"), YamlValue.Parse("{ size: 0 }")));
        Assert.Equal("style has no key 'colour'", Check(Widget("text", "style"), YamlValue.Parse("{ colour: \"#FFFFFF\" }")));
    }

    [Fact]
    public void Blocks_and_lists_check_what_is_in_them()
    {
        Assert.Null(Check(Widget("gauge", "needle"), YamlValue.Parse("{ width: 8, length: 0.7 }")));
        Assert.Equal("needle.length must be between 0.1 and 1", Check(Widget("gauge", "needle"), YamlValue.Parse("{ length: 2 }")));
        Assert.Equal("needle has no key 'size'", Check(Widget("gauge", "needle"), YamlValue.Parse("{ size: 2 }")));
        Assert.StartsWith("needle must be a mapping", Check(Widget("gauge", "needle"), "8"));

        var slices = Widget("donut", "slices");
        Assert.Null(Check(slices, YamlValue.Parse("[{ source: cpu.load }, { source: gpu.load }]")));
        Assert.Equal("slices needs at least 2 items", Check(slices, YamlValue.Parse("[{ source: cpu.load }]")));
        Assert.Equal("slices has no key 'value'", Check(slices, YamlValue.Parse("[{ source: cpu.load }, { value: 3 }]")));

        var stops = ThemeSchema.Gradient.Common.Key("stops")!;
        Assert.Null(Check(stops, List("0", "0.5", "1")));
        Assert.Equal("stops must be between 0 and 1", Check(stops, List("0", "1.5")));
    }

    [Fact]
    public void A_values_spans_may_show_the_value()
    {
        var text = ThemeSchema.TextSpan.Key("text")!;
        Assert.Null(Check(text, "{0:0}", Context with { WithValue = true }));
        Assert.Null(Check(text, "{cpu.temp:0}"));
    }
}
