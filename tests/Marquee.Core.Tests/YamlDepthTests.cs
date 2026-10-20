using Marquee.Core.Config;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Parsing;
using Marquee.Core.Tests.TestSupport;

namespace Marquee.Core.Tests;

/// Themes are shared, so a layout.yaml nested thousands of levels deep must be an error with a line, like any other
/// mistake, and not a stack overflow, which no catch can stop and which would end the tray during a hot reload.
public class YamlDepthTests
{
    /// A small stack, so a parse that recursed once per level would overflow it long before 5000 levels.
    const int SmallStack = 256 * 1024;

    /// A layout whose widgets are lists inside lists, `depth` levels in all counting the top-level mapping, on line 3.
    static string Flow(int depth) =>
        $"canvas: {{ orientation: landscape }}\n\nwidgets: {new string('[', depth - 1)}{new string(']', depth - 1)}\n";

    /// Mappings inside mappings, `depth` levels in all, each opening on its own line: level N starts on line N.
    static string Block(int depth) =>
        string.Concat(Enumerable.Range(1, depth - 1).Select(level => $"{new string(' ', 2 * (level - 1))}k{level}:\n"))
        + $"{new string(' ', 2 * (depth - 1))}leaf: 1\n";

    /// A config.yaml whose ring setting is 5000 lists deep, on line 2.
    static readonly string DeepConfig = $"theme: neon\nring: {new string('[', 5000)}{new string(']', 5000)}\n";

    /// Runs `parse` on a thread with a small stack and returns what it threw, or null.
    static Exception? OnSmallStack(Action parse)
    {
        Exception? thrown = null;
        var thread = new Thread(() =>
        {
            try
            {
                parse();
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
        }, SmallStack);
        thread.Start();
        thread.Join();
        return thrown;
    }

    [Fact]
    public void Five_thousand_levels_is_a_theme_error_not_a_stack_overflow()
    {
        var thrown = OnSmallStack(() => LayoutParser.Parse(Flow(5000)));

        var error = Assert.IsType<ThemeException>(thrown);
        Assert.Equal("layout.yaml line 3: Lists and mappings are nested more than 64 levels deep", error.Message);
    }

    [Fact]
    public void The_error_has_the_line_where_the_65th_level_opens()
    {
        var error = Layouts.Error(Block(65));

        Assert.Equal(65, error.Line);
        Assert.Equal("Lists and mappings are nested more than 64 levels deep", error.Reason);
    }

    [Fact]
    public void Sixty_four_levels_parse()
    {
        Assert.Null(OnSmallStack(() => Assert.NotNull(Yaml.Load(Flow(64)))));
        Assert.Null(OnSmallStack(() => Assert.NotNull(Yaml.Load(Block(64)))));
    }

    [Fact]
    public void The_deepest_valid_theme_is_well_inside_the_limit()
    {
        // 39 levels: groups nested as deep as they may be, then a shape's style, stroke, gradient and its colors.
        var widget = """
            { type: shape, shape: rect, at: [0, 0], size: [10, 10],
              style: { stroke: { width: 1, color: { linear: ["#000000", "#FFFFFF"] } } } }
            """;
        for (var i = 0; i < WidgetParser.MaxGroupDepth; i++)
            widget = $"{{ type: group, at: [0, 0], size: [10, 10], children: [{widget}] }}";

        var spec = Layouts.Parse($"canvas: {{ orientation: landscape }}\nwidgets: [{widget}]\n");

        Assert.IsType<GroupWidget>(Assert.Single(spec.Widgets));
    }

    [Fact]
    public void Validate_reports_it_as_the_only_error()
    {
        var (_, errors) = Layouts.Collect(Flow(5000));

        Assert.Equal(["3: Lists and mappings are nested more than 64 levels deep"], Layouts.Lines(errors));
    }

    [Fact]
    public void A_deep_config_yaml_is_a_config_error_with_its_line()
    {
        var thrown = OnSmallStack(() => ConfigParser.Parse(DeepConfig));

        var error = Assert.IsType<ConfigException>(thrown);
        Assert.Equal("config.yaml line 2: Lists and mappings are nested more than 64 levels deep", error.Message);
    }

    [Fact]
    public void The_tray_refuses_to_edit_a_deep_config_yaml()
    {
        var thrown = OnSmallStack(() => ConfigEditor.Set(DeepConfig, "theme", "x"));

        var error = Assert.IsType<ConfigException>(thrown);
        Assert.Equal(2, error.Line);
        Assert.StartsWith("Lists and mappings are nested more than 64 levels deep", error.Reason);
    }
}
