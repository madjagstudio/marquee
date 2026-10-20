using Marquee.Editor.Resources;
using Marquee.Editor.Tests.TestSupport;

namespace Marquee.Editor.Tests;

/// The editor's text comes from Strings.resx, through a Strings member of the same name for each entry.
public class StringsTests
{
    [Fact]
    public void Messages_come_from_the_compiled_English_resources()
    {
        Assert.Equal("Show on screen", Strings.ShowOnScreen);
        Assert.Equal("mine – Marquee Theme Editor", Strings.WindowTitle("mine"));
        Assert.Equal("3 problems", Strings.Problems(3));
    }

    [Fact]
    public void The_resources_and_the_Strings_members_match_both_ways()
    {
        var keys = StringResources.English(Strings.ResourceManager).Keys.Order(StringComparer.Ordinal);
        var members = StringResources.MessageNames(typeof(Strings)).Order(StringComparer.Ordinal);

        Assert.Equal(keys, members);
    }

    [Fact]
    public void Every_message_has_text()
    {
        foreach (var (key, text) in StringResources.English(Strings.ResourceManager))
            Assert.False(string.IsNullOrWhiteSpace(text), $"{key} is empty.");
    }

    [Fact]
    public void Plain_messages_have_no_placeholders()
    {
        foreach (var property in StringResources.Properties(typeof(Strings)))
            Assert.True(StringResources.PlaceholderCount((string)property.GetValue(null)!) == 0, $"{property.Name} has a placeholder; make it a method.");
    }

    [Fact]
    public void Each_method_takes_one_argument_per_placeholder()
    {
        var english = StringResources.English(Strings.ResourceManager);
        foreach (var method in StringResources.Methods(typeof(Strings)))
        {
            Assert.Equal(method.GetParameters().Length, StringResources.PlaceholderCount(english[method.Name]));
            // string.Format throws on a placeholder with no argument, so this catches a {2} among two parameters.
            Assert.IsType<string>(method.Invoke(null, StringResources.DummyArguments(method)));
        }
    }
}
