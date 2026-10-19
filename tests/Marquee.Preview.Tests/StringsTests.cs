using System.Globalization;
using Marquee.Preview.Resources;
using Marquee.Preview.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Preview.Tests;

/// These tests swap Strings.ResourceManager and the culture, so they share one class: xUnit runs a class's tests one
/// at a time.
public class StringsTests
{
    [Fact]
    public void Messages_come_from_the_compiled_English_resources()
    {
        Assert.Equal("Waiting for the first frame…", Strings.WaitingForFirstFrame);
        Assert.Equal("Marquee preview: example-landscape", Strings.WindowTitle("example-landscape"));
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
            // string.Format throws on a placeholder with no argument, so this catches a {1} with one parameter.
            Assert.IsType<string>(method.Invoke(null, StringResources.DummyArguments(method)));
        }
    }

    [Fact]
    public void Lookups_ask_for_the_users_UI_language()
    {
        using var culture = new CultureScope("de-DE");
        using var resources = SwappedResources.Pseudo();

        _ = Strings.WaitingForFirstFrame;

        Assert.Equal(CultureInfo.GetCultureInfo("de-DE"), Assert.Single(resources.Cultures));
    }

    [Fact]
    public void A_missing_message_is_a_bug_that_names_the_key()
    {
        using var resources = SwappedResources.Empty();

        var error = Assert.Throws<InvalidOperationException>(() => Strings.WaitingForFirstFrame);

        Assert.Contains(nameof(Strings.WaitingForFirstFrame), error.Message);
    }

    [Fact]
    public void A_translation_reaches_the_window()
    {
        using var resources = SwappedResources.Pseudo();

        // Built but never shown: the title and the status text are set in the constructor.
        using var window = new PreviewWindow("example-landscape", new SKSizeI(1920, 480));

        Assert.Equal("[!Marquee preview: example-landscape!]", window.Text);
        var status = window.Controls.OfType<StatusStrip>().Single().Items[0];
        Assert.Equal("[!Waiting for the first frame…!]", status.Text);
    }
}
