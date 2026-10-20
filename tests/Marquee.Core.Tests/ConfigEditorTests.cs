using Marquee.Core.Config;
using Marquee.Core.Devices;
using Marquee.Core.Tests.TestSupport;

namespace Marquee.Core.Tests;

/// Every edit is checked twice beyond its exact text: ConfigParser reads the new value with everything else unchanged
/// (for the keys it knows), and YamlDotNet reads every value but the one key unchanged (for all keys, including
/// lconnect.handover, which ConfigEditorHandoverParserTests also reads back through ConfigParser).
public class ConfigEditorTests
{
    static string SetTheme(string yaml, string theme)
    {
        var after = ConfigEditor.Set(yaml, "theme", theme);
        Assert.Equal(ConfigParser.Parse(yaml) with { Theme = theme }, ConfigParser.Parse(after));
        YamlValues.AssertOnlyChanged(yaml, after, "theme", theme);
        return after;
    }

    static string SetCodex(string yaml, string value)
    {
        var after = ConfigEditor.Set(yaml, "providers.codex", value);
        Assert.Equal(ConfigParser.Parse(yaml) with { Codex = value == "on" }, ConfigParser.Parse(after));
        YamlValues.AssertOnlyChanged(yaml, after, "providers.codex", value);
        return after;
    }

    static string SetHandover(string yaml, string value)
    {
        var after = ConfigEditor.Set(yaml, "lconnect.handover", value);
        YamlValues.AssertOnlyChanged(yaml, after, "lconnect.handover", value);
        return after;
    }

    static ConfigException Refused(string yaml, string key = "theme", string value = "example-agents") =>
        Assert.Throws<ConfigException>(() => ConfigEditor.Set(yaml, key, value));

    [Fact]
    public void Changes_a_value_and_keeps_its_comment() =>
        Assert.Equal(
            "theme: example-portrait  # the one I like\r\norientation: portrait\r\n",
            SetTheme("theme: example-agents  # the one I like\r\norientation: portrait\r\n", "example-portrait"));

    [Fact]
    public void Keeps_the_rest_of_the_file_exactly()
    {
        const string before =
            "# Marquee settings\n" +
            "\n" +
            "orientation: landscape-flipped   # mounted upside down\n" +
            "theme: example-agents\n" +
            "\n" +
            "providers: { claude: on, codex: off }\n" +
            "http:\n" +
            "  port: 50000   # the default clashes with something\n" +
            "# fps: 24\n";

        Assert.Equal(before.Replace("theme: example-agents", "theme: neon"), SetTheme(before, "neon"));
    }

    [Theory]
    [InlineData("theme: 'example-agents'\n", "theme: 'neon'\n")]
    [InlineData("theme: \"example-agents\"\n", "theme: \"neon\"\n")]
    public void A_quoted_value_stays_quoted_the_same_way(string before, string after) =>
        Assert.Equal(after, SetTheme(before, "neon"));

    [Fact]
    public void A_double_quoted_path_escapes_its_backslashes() =>
        Assert.Equal("theme: \"C:\\\\Themes\\\\Neon\"\n", SetTheme("theme: \"example-agents\"\n", @"C:\Themes\Neon"));

    [Theory]
    [InlineData("example-agents")]
    [InlineData("portrait-flipped")]
    [InlineData("my_theme.v2")]
    [InlineData("auto")]
    [InlineData("off")]
    [InlineData("on")]
    public void A_plain_word_is_written_plain(string theme) =>
        Assert.Equal($"theme: {theme}\n", SetTheme("theme: old\n", theme));

    [Theory]
    [InlineData(@"C:\Themes\Neon", @"'C:\Themes\Neon'")]
    [InlineData(@"D:\My Themes\neon", @"'D:\My Themes\neon'")]
    [InlineData("my theme", "'my theme'")]
    [InlineData("themes/neon", "'themes/neon'")]
    [InlineData("#ff8800", "'#ff8800'")]
    [InlineData("neon # bright", "'neon # bright'")]
    [InlineData("neon: bright", "'neon: bright'")]
    [InlineData("- neon", "'- neon'")]
    [InlineData("[neon]", "'[neon]'")]
    [InlineData("{neon}", "'{neon}'")]
    [InlineData("neon, bright", "'neon, bright'")]
    [InlineData("*neon", "'*neon'")]
    [InlineData("&neon", "'&neon'")]
    [InlineData("!neon", "'!neon'")]
    [InlineData("%neon", "'%neon'")]
    [InlineData("@neon", "'@neon'")]
    [InlineData("`neon`", "'`neon`'")]
    [InlineData("'neon'", "'''neon'''")]
    [InlineData("\"neon\"", "'\"neon\"'")]
    [InlineData("neon's", "'neon''s'")]
    [InlineData(" neon", "' neon'")]
    [InlineData("neon ", "'neon '")]
    [InlineData("true", "'true'")]
    [InlineData("False", "'False'")]
    [InlineData("null", "'null'")]
    [InlineData("~", "'~'")]
    [InlineData("2024", "'2024'")]
    [InlineData("1.5", "'1.5'")]
    [InlineData("0x1F", "'0x1F'")]
    [InlineData(".inf", "'.inf'")]
    [InlineData("-1", "'-1'")]
    [InlineData("2024-neon", "'2024-neon'")]
    [InlineData("néon", "'néon'")]
    public void A_value_YAML_would_read_differently_is_single_quoted(string theme, string written) =>
        Assert.Equal($"theme: {written}\n", SetTheme("theme: old\n", theme));

    [Theory]
    [InlineData("neon\nbright", "\"neon\\u000Abright\"")]
    [InlineData("neon\tbright", "\"neon\\u0009bright\"")]
    [InlineData("neon\u2028bright", "\"neon\\u2028bright\"")]
    [InlineData("C:\\neon\n", "\"C:\\\\neon\\u000A\"")]
    public void A_value_with_a_line_break_or_control_character_is_double_quoted(string theme, string written) =>
        Assert.Equal($"theme: {written}\n", SetTheme("theme: 'old'\n", theme));

    [Theory]
    [InlineData("theme:\norientation: portrait\n", "theme: neon\norientation: portrait\n")]
    [InlineData("theme:   # pick one\n", "theme: neon   # pick one\n")]
    [InlineData("theme:", "theme: neon")]
    public void Fills_in_an_empty_value(string before, string after)
    {
        // An empty theme is an error to ConfigParser, so only the result is read back through it.
        var edited = ConfigEditor.Set(before, "theme", "neon");

        Assert.Equal(after, edited);
        Assert.Equal("neon", ConfigParser.Parse(edited).Theme);
        YamlValues.AssertOnlyChanged(before, edited, "theme", "neon");
    }

    [Theory]
    [InlineData("theme: |\n  example-agents\nfps: 30\n", "theme: neon\nfps: 30\n")]
    [InlineData("theme: >-\n  example-agents\n\nfps: 30\n", "theme: neon\n\nfps: 30\n")]
    public void Replaces_a_block_scalar_and_keeps_the_lines_after_it(string before, string after) =>
        Assert.Equal(after, SetTheme(before, "neon"));

    [Fact]
    public void Returns_the_text_unchanged_when_the_value_is_already_set()
    {
        const string before = "theme: neon   # mine\r\n";

        Assert.Equal(before, SetTheme(before, "neon"));
    }

    [Theory]
    [InlineData("orientation: portrait\n", "orientation: portrait\ntheme: neon\n")]
    [InlineData("orientation: portrait\r\n# fps: 24\r\n", "orientation: portrait\r\n# fps: 24\r\ntheme: neon\r\n")]
    [InlineData("orientation: portrait\nfps: 24", "orientation: portrait\nfps: 24\ntheme: neon\n")]
    [InlineData("orientation: portrait", "orientation: portrait\r\ntheme: neon\r\n")]
    [InlineData("providers:\n  claude: off\n", "providers:\n  claude: off\ntheme: neon\n")]
    [InlineData("  orientation: portrait\n", "  orientation: portrait\n  theme: neon\n")]
    public void A_missing_key_is_added_at_the_end_of_the_file(string before, string after) =>
        Assert.Equal(after, SetTheme(before, "neon"));

    [Theory]
    [InlineData("", "theme: neon\r\n")]
    [InlineData("# my settings\n", "# my settings\ntheme: neon\n")]
    [InlineData("# my settings\r\n\r\n", "# my settings\r\n\r\ntheme: neon\r\n")]
    public void An_empty_file_gets_just_that_line(string before, string after) =>
        Assert.Equal(after, SetTheme(before, "neon"));

    [Theory]
    [InlineData("{ orientation: portrait }\n", "{ orientation: portrait, theme: neon }\n")]
    [InlineData("{ theme: old, orientation: portrait }\n", "{ theme: neon, orientation: portrait }\n")]
    [InlineData("{}\n", "{ theme: neon }\n")]
    public void A_file_written_as_one_flow_mapping_is_edited_inside_it(string before, string after) =>
        Assert.Equal(after, SetTheme(before, "neon"));

    [Theory]
    [InlineData("providers:\n  codex: on   # for now\nfps: 30\n", "providers:\n  codex: off   # for now\nfps: 30\n")]
    [InlineData("providers: { claude: on, codex: on }\n", "providers: { claude: on, codex: off }\n")]
    [InlineData("providers: { codex: \"on\" }\n", "providers: { codex: \"off\" }\n")]
    public void Changes_a_nested_value_in_block_or_flow_style(string before, string after) =>
        Assert.Equal(after, SetCodex(before, "off"));

    [Theory]
    [InlineData("providers:\n  claude: on   # mine\ntheme: neon\n", "providers:\n  claude: on   # mine\n  codex: off\ntheme: neon\n")]
    [InlineData("providers:\r\n    claude: on\r\n# end\r\n", "providers:\r\n    claude: on\r\n    codex: off\r\n# end\r\n")]
    [InlineData("providers:\n  claude: on", "providers:\n  claude: on\n  codex: off\n")]
    [InlineData("providers:\n  claude: on\n  fps: on\nhttp:\n  port: 50000\n", "providers:\n  claude: on\n  fps: on\n  codex: off\nhttp:\n  port: 50000\n")]
    public void A_missing_nested_key_is_added_at_the_end_of_its_block_mapping(string before, string after) =>
        Assert.Equal(after, SetCodex(before, "off"));

    [Theory]
    [InlineData("providers: { claude: on }\n", "providers: { claude: on, codex: off }\n")]
    [InlineData("providers: { claude: on, }\n", "providers: { claude: on, codex: off, }\n")]
    [InlineData("providers: {}\n", "providers: { codex: off }\n")]
    [InlineData("providers: { }\n", "providers: { codex: off }\n")]
    [InlineData("providers: {\n  claude: on   # mine\n}\n", "providers: {\n  claude: on, codex: off   # mine\n}\n")]
    public void A_missing_nested_key_is_added_inside_its_flow_mapping(string before, string after) =>
        Assert.Equal(after, SetCodex(before, "off"));

    [Theory]
    [InlineData("theme: neon\n", "theme: neon\nproviders:\n  codex: off\n")]
    [InlineData("theme: neon", "theme: neon\r\nproviders:\r\n  codex: off\r\n")]
    [InlineData("", "providers:\r\n  codex: off\r\n")]
    [InlineData("{ theme: neon }\n", "{ theme: neon, providers: { codex: off } }\n")]
    public void A_missing_parent_is_added_with_the_key_under_it(string before, string after) =>
        Assert.Equal(after, SetCodex(before, "off"));

    [Theory]
    [InlineData("lconnect: { handover: off }\n", "lconnect: { handover: auto }\n")]
    [InlineData("lconnect:\n  handover: ask   # for now\n", "lconnect:\n  handover: auto   # for now\n")]
    [InlineData("lconnect: {}\n", "lconnect: { handover: auto }\n")]
    [InlineData("theme: neon\r\n", "theme: neon\r\nlconnect:\r\n  handover: auto\r\n")]
    [InlineData("lconnect:   # L-Connect\ntheme: neon\n", "lconnect:   # L-Connect\n  handover: auto\ntheme: neon\n")]
    [InlineData("lconnect:", "lconnect:\r\n  handover: auto\r\n")]
    [InlineData("{ lconnect: }\n", "{ lconnect: { handover: auto } }\n")]
    public void Sets_the_handover(string before, string after) =>
        Assert.Equal(after, SetHandover(before, "auto"));

    [Theory]
    [InlineData("theme: [unclosed\n")]
    [InlineData("theme: neon\ntheme: old\n")]
    [InlineData("theme: neon\n  orientation: portrait\n")]
    [InlineData("providers: [claude, codex\nfps: 30\n")]
    public void Refuses_a_file_that_does_not_parse(string yaml)
    {
        var ex = Refused(yaml);

        Assert.Contains("Fix this first", ex.Reason);
    }

    [Fact]
    public void A_refusal_names_the_broken_line() =>
        Assert.Equal(2, Refused("theme: neon\ntheme: old\n").Line);

    [Theory]
    [InlineData("- neon\n- old\n", "theme", 1)]
    [InlineData("neon\n", "theme", 1)]
    [InlineData("theme: [neon, old]\n", "theme", 1)]
    [InlineData("theme:\n  name: neon\n", "theme", 2)]
    [InlineData("fps: 30\nproviders: off\n", "providers.codex", 2)]
    [InlineData("providers: [claude]\n", "providers.codex", 1)]
    public void Refuses_a_file_whose_shape_has_no_place_for_the_key(string yaml, string key, int line)
    {
        var ex = Refused(yaml, key, "off");

        Assert.Equal(line, ex.Line);
        Assert.Contains(key, ex.Reason);
    }

    [Theory]
    [InlineData("orientation: &o portrait\ntheme: *o\n", "theme")]
    [InlineData("base: &p { claude: on }\nproviders: *p\n", "providers.codex")]
    public void Refuses_to_edit_through_an_alias(string yaml, string key)
    {
        var ex = Refused(yaml, key, "off");

        Assert.Contains("by hand", ex.Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Theme")]
    [InlineData("lconnect.")]
    [InlineData(".handover")]
    [InlineData("a.b.c")]
    [InlineData("the me")]
    public void Rejects_a_key_that_is_not_a_setting_name(string key) =>
        Assert.Throws<ArgumentException>(() => ConfigEditor.Set("theme: neon\n", key, "x"));

    [Fact]
    public void Sets_the_orientation()
    {
        const string before = "theme: neon\norientation: landscape   # desk\n";

        var after = ConfigEditor.Set(before, "orientation", "portrait-flipped");

        Assert.Equal("theme: neon\norientation: portrait-flipped   # desk\n", after);
        Assert.Equal(ConfigParser.Parse(before) with { Orientation = Orientation.PortraitFlipped }, ConfigParser.Parse(after));
    }
}
