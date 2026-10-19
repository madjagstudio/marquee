using System.Globalization;
using Marquee.Host.Resources;
using Marquee.Host.Tests.TestSupport;

namespace Marquee.Host.Tests;

/// These tests swap Strings.ResourceManager and the culture, so they run in a collection of their own, after every
/// other test has finished and one at a time (SwappedResourcesCollection).
[Collection(nameof(SwappedResources))]
public class StringsTests
{
    [Fact]
    public void Messages_come_from_the_compiled_English_resources()
    {
        Assert.Equal("Codex limits", Strings.CodexLimitsSource);
        Assert.Equal("Showing theme 'neon'.", Strings.ShowingTheme("neon"));
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

    [Fact]
    public void Lookups_ask_for_the_users_UI_language()
    {
        using var culture = new CultureScope("de-DE");
        using var resources = SwappedResources.Pseudo();

        _ = Strings.NoScreenFound;

        Assert.Equal(CultureInfo.GetCultureInfo("de-DE"), Assert.Single(resources.Cultures));
    }

    [Fact]
    public void A_missing_message_is_a_bug_that_names_the_key()
    {
        using var resources = SwappedResources.Empty();

        var error = Assert.Throws<InvalidOperationException>(() => Strings.NoScreenFound);

        Assert.Contains(nameof(Strings.NoScreenFound), error.Message);
    }

    [Fact]
    public void A_translation_reaches_the_log()
    {
        using var resources = SwappedResources.Pseudo();
        var log = new ListLog();

        AgentSources.Create([AgentSource.CodexLimits], log, (_, _) => throw new IOException("disk gone"));

        Assert.Equal(["error: [![!Codex limits!] is unavailable: disk gone!]"], log.Entries);
    }
}
