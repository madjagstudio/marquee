using System.Globalization;
using Marquee.Cli.Tests.TestSupport;

namespace Marquee.Cli.Tests;

/// These tests swap Strings.ResourceManager, the culture and the console, so they run in the console collection (see
/// ConsoleCollection): one at a time, after the tests that run in parallel.
[Collection(nameof(ConsoleCapture))]
public class StringsTests
{
    [Fact]
    public void Messages_come_from_the_compiled_English_resources()
    {
        Assert.Equal("No supported screens found.", Strings.NoSupportedScreens);
        Assert.Equal("Sent logo.png to File sink.", Strings.SentImage("logo.png", "File sink"));
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
            // string.Format throws on a placeholder with no argument, so this catches a {3} among three parameters.
            Assert.IsType<string>(method.Invoke(null, StringResources.DummyArguments(method)));
        }
    }

    [Fact]
    public void Lookups_ask_for_the_users_UI_language()
    {
        using var culture = new CultureScope("de-DE");
        using var resources = SwappedResources.Pseudo();

        _ = Strings.NoSupportedScreens;

        Assert.Equal(CultureInfo.GetCultureInfo("de-DE"), Assert.Single(resources.Cultures));
    }

    [Fact]
    public void Numbers_in_messages_look_the_same_in_every_culture()
    {
        using var culture = new CultureScope("de-DE");

        Assert.Equal("Pushed 1000 frames, average 12.5 ms per frame", Strings.PushedFrames(1000, 12.5));
    }

    [Fact]
    public void A_missing_message_is_a_bug_that_names_the_key()
    {
        using var resources = SwappedResources.Empty();

        var error = Assert.Throws<InvalidOperationException>(() => Strings.NoSupportedScreens);

        Assert.Contains(nameof(Strings.NoSupportedScreens), error.Message);
    }

    [Fact]
    public void A_translation_reaches_the_user()
    {
        using var resources = SwappedResources.Pseudo();

        Assert.Equal("[!No supported screens found.!]", Strings.NoSupportedScreens);
        Assert.Equal("[!Sent logo.png to File sink.!]", Strings.SentImage("logo.png", "File sink"));
        Assert.StartsWith("[!Usage:", Strings.Usage);
    }

    [Fact]
    public void The_CLI_writes_its_messages_through_the_resources()
    {
        using var resources = SwappedResources.Pseudo();
        using var console = new ConsoleCapture();

        var push = PushCommand.Run("not a key", "1", []);
        // The test host isn't marquee.exe, so setup-claude stops before it touches any settings.
        var setup = SetupClaudeCommand.Run([]);

        Assert.Equal((1, 1), (push, setup));
        var lines = console.Error.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Equal(
            "[!'custom.not a key' is not a valid key: use letters, digits, _ and dots after \"custom.\" (64 characters at most).!]",
            lines[0]);
        Assert.StartsWith("[!Run setup-claude from marquee.exe itself (this is ", lines[1]);
        Assert.EndsWith("!]", lines[1]);
    }
}
