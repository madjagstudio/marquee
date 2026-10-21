using Marquee.Core.Config;
using Marquee.Core.Tests.TestSupport;

namespace Marquee.Core.Tests;

/// What changed in ConfigEditor when it moved onto YamlSplice, the splicing it shares with the theme editor, pinned so
/// each change stays deliberate. Before, it dropped a value's tag and anchor, wrote ` , ` after a filled empty value,
/// always wrote `, ` before a new `{ }` entry, refused to add to an anchored or tagged block mapping, and edited the two
/// rare shapes it now refuses. Each refusal is a ConfigException that leaves the file as it was, which the tray shows.
/// The tagged empty value (`theme: !!str`) is in LayoutDocumentTaggedEmptyTests.
public class ConfigEditorSharedSpliceTests
{
    static string Set(string yaml, string key, string value)
    {
        var after = ConfigEditor.Set(yaml, key, value);
        YamlValues.AssertOnlyChanged(yaml.Replace("\r\n", "\n"), after.Replace("\r\n", "\n"), key, value);
        return after;
    }

    [Theory]
    [InlineData("theme: !!str old\n", "theme: !!str new\n")]
    [InlineData("theme: &t old\n", "theme: &t new\n")]
    [InlineData("theme: &t !!str old\n", "theme: &t !!str new\n")]
    public void A_value_keeps_its_tag_and_anchor(string before, string after) =>
        Assert.Equal(after, Set(before, "theme", "new"));

    [Fact]
    public void A_value_whose_anchor_an_alias_uses_is_refused() =>
        Assert.Throws<ConfigException>(() => ConfigEditor.Set("theme: &t old\nother: *t\n", "theme", "new"));

    [Theory]
    [InlineData("lconnect: { handover: , screen: on }\n")]
    [InlineData("lconnect: { handover:, screen: on }\n")]
    public void An_empty_value_in_braces_is_filled_without_a_space_before_the_comma(string before) =>
        Assert.Equal("lconnect: { handover: auto, screen: on }\n", Set(before, "lconnect.handover", "auto"));

    [Theory]
    [InlineData("lconnect: {\r\n  screen: on,\r\n  x: 1\r\n}\r\n", "lconnect: {\r\n  screen: on,\r\n  x: 1,\r\n  handover: auto\r\n}\r\n")]
    [InlineData("lconnect: {screen: on,x: 1}\n", "lconnect: {screen: on,x: 1,handover: auto}\n")]
    [InlineData("lconnect: { screen: on }\n", "lconnect: { screen: on, handover: auto }\n")]
    public void A_new_entry_in_braces_follows_the_separator_between_the_last_two(string before, string after) =>
        Assert.Equal(after, Set(before, "lconnect.handover", "auto"));

    [Theory]
    [InlineData("lconnect: &l\n  screen: on\n", "lconnect: &l\n  screen: on\n  handover: auto\n")]
    [InlineData("lconnect: !!map\n  screen: on\n", "lconnect: !!map\n  screen: on\n  handover: auto\n")]
    public void A_key_is_added_to_an_anchored_or_tagged_block_mapping_at_its_keys_column(string before, string after) =>
        Assert.Equal(after, Set(before, "lconnect.handover", "auto"));

    [Theory]
    [InlineData("lconnect: &l\n  screen: on\nx: *l\n")]
    [InlineData("a: &v on\nlconnect: { x: *v }\n")]
    [InlineData("lconnect:\n  ? screen\n  : on\n")]
    public void Shapes_it_can_not_add_to_without_touching_more_are_refused(string before) =>
        Assert.Throws<ConfigException>(() => ConfigEditor.Set(before, "lconnect.handover", "auto"));

    [Fact]
    public void The_typical_file_is_unchanged_but_for_the_value() =>
        Assert.Equal(
            "# Marquee settings\ntheme: example-showcase   # the look\nlconnect:\n  handover: auto   # or auto\n",
            Set("# Marquee settings\ntheme: example-showcase   # the look\nlconnect:\n  handover: ask   # or auto\n", "lconnect.handover", "auto"));
}
