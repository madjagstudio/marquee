using Marquee.Core.Themes.Editing;

namespace Marquee.Core.Tests.Editing;

public class YamlPathTests
{
    [Theory]
    [InlineData("widgets[3].style.color")]
    [InlineData("background[0].hidden")]
    [InlineData("widgets[5].children[0].at[1]")]
    [InlineData("canvas")]
    [InlineData("text_fill")]
    [InlineData("styles.my-style")]
    [InlineData("")]
    [InlineData("[2]")]
    [InlineData("styles[\"my style\"].size")]
    [InlineData("styles[\"say \\\"hi\\\"\"]")]
    public void Round_trips_through_its_text(string text) => Assert.Equal(text, YamlPath.Parse(text).ToString());

    [Fact]
    public void Splits_into_keys_and_indexes()
    {
        var path = YamlPath.Parse("widgets[3].style");

        Assert.Equal(
            [YamlPathSegment.ForKey("widgets"), YamlPathSegment.ForIndex(3), YamlPathSegment.ForKey("style")],
            path.Segments);
    }

    [Fact]
    public void Child_and_Item_build_the_same_path_as_parsing()
    {
        var built = YamlPath.Root.Child("widgets").Item(5).Child("children").Item(0).Child("at").Item(1);

        Assert.Equal(YamlPath.Parse("widgets[5].children[0].at[1]"), built);
        Assert.Equal(YamlPath.Parse("widgets[5].children[0].at[1]").GetHashCode(), built.GetHashCode());
        Assert.NotEqual(YamlPath.Parse("widgets[5].children[0].at[0]"), built);
    }

    [Fact]
    public void A_key_that_is_not_a_plain_name_is_written_in_brackets_and_quotes()
    {
        Assert.Equal("styles[\"a.b\"]", YamlPath.Root.Child("styles").Child("a.b").ToString());
        Assert.Equal("[\"two words\"].x", YamlPath.Root.Child("two words").Child("x").ToString());
        Assert.Equal("[\"back\\\\slash\"]", YamlPath.Root.Child("back\\slash").ToString());
        Assert.Equal("a.b", YamlPath.Parse("[\"a\"].b").ToString());
    }

    [Fact]
    public void Knows_its_parent_and_last_segment()
    {
        var path = YamlPath.Parse("widgets[3].style");

        Assert.Equal(YamlPath.Parse("widgets[3]"), path.Parent);
        Assert.Equal(YamlPathSegment.ForKey("style"), path.Last);
        Assert.Null(YamlPath.Root.Parent);
        Assert.Null(YamlPath.Root.Last);
        Assert.True(YamlPath.Parse("").IsRoot);
    }

    [Theory]
    [InlineData("widgets[")]
    [InlineData("widgets[]")]
    [InlineData("widgets[-1]")]
    [InlineData("widgets[x]")]
    [InlineData("widgets[3]x")]
    [InlineData(".a")]
    [InlineData("a.")]
    [InlineData("a..b")]
    [InlineData("a b")]
    [InlineData("[\"unclosed]")]
    [InlineData("[\"\"]")]
    [InlineData("widgets[99999999999]")]
    public void Rejects_text_that_is_not_a_path(string text)
    {
        Assert.Throws<FormatException>(() => YamlPath.Parse(text));
        Assert.False(YamlPath.TryParse(text, out _));
    }

    [Fact]
    public void Rejects_an_empty_key_or_a_negative_index()
    {
        Assert.Throws<ArgumentException>(() => YamlPath.Root.Child(""));
        Assert.Throws<ArgumentOutOfRangeException>(() => YamlPath.Root.Item(-1));
    }
}
