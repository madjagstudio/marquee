using Marquee.Core.Themes.Editing;

namespace Marquee.Core.Tests.Editing;

/// How a new value's YAML text is read before LayoutDocument places it.
public class YamlFragmentTests
{
    /// YAML's white space is spaces, tabs and line breaks; a no-break space or an ideographic space is text.
    [Theory]
    [InlineData("a ")]
    [InlineData(" a　")]
    public void A_new_value_keeps_the_unicode_spaces_around_it(string fragment)
    {
        Assert.Equal($"a: {fragment}\n", Edits.Replace("a: 1\n", "a", fragment));
        Assert.Equal(YamlTree.Text(fragment), YamlFragment.Parse($" {fragment}\t\r\n").Tree);
    }
}
