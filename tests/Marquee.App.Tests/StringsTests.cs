using System.Globalization;
using Marquee.App.Resources;
using Marquee.App.Tests.TestSupport;
using Marquee.Core.Devices;
using Marquee.Core.Logging;
using Marquee.Host;

namespace Marquee.App.Tests;

/// These tests swap Strings.ResourceManager and the culture, so they run in a collection of their own, after every
/// other test has finished and one at a time (SwappedResourcesCollection).
[Collection(nameof(SwappedResources))]
public class StringsTests
{
    [Fact]
    public void Messages_come_from_the_compiled_English_resources()
    {
        Assert.Equal("Open themes folder", Strings.MenuOpenThemesFolder);
        Assert.Equal("example-landscape · 30 fps · Universal Screen 8.8", Strings.StatusShowing("example-landscape", 29.6, "Universal Screen 8.8"));
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
    public void Numbers_stay_culture_invariant_in_any_language()
    {
        using var culture = new CultureScope("de-DE");

        Assert.Equal("29.6 fps (target 30) · render 4.0 ms · send 20.5 ms · 100 frames sent", Strings.PreviewStatus(29.6, 30, 4, 20.5, 100));
    }

    [Fact]
    public void Lookups_ask_for_the_users_UI_language()
    {
        using var culture = new CultureScope("de-DE");
        using var resources = SwappedResources.Pseudo();

        _ = Strings.MenuQuit;

        Assert.Equal(CultureInfo.GetCultureInfo("de-DE"), Assert.Single(resources.Cultures));
    }

    [Fact]
    public void A_missing_message_is_a_bug_that_names_the_key()
    {
        using var resources = SwappedResources.Empty();

        var error = Assert.Throws<InvalidOperationException>(() => Strings.MenuQuit);

        Assert.Contains(nameof(Strings.MenuQuit), error.Message);
    }

    [Fact]
    public void A_translation_reaches_the_menu()
    {
        using var resources = SwappedResources.Pseudo();
        using var dir = new TempDir();
        using var worker = new EngineWorker();
        var engine = new FakeTrayEngine();
        engine.Showing(dir.Folder("example-landscape"));
        var paths = new TrayPaths(dir.File("config.yaml"), new ThemeLocator([dir.File("user")], dir.Path), dir.File("logs"), dir.File("data"));
        using var updates = new UpdateRig(dir);
        var model = new TrayModel(engine, paths, new FakeShell(), new FakeHost(), worker, updates.Updates, NullLog.Instance);

        var menu = model.BuildMenu();

        Assert.Equal("[!example-landscape · 30 fps · Universal Screen 8.8!]", menu[0].Text);
        Assert.Equal("[!Quit!]", menu[^1].Text);
        Assert.All(menu.Where(item => !item.IsSeparator), item => Assert.StartsWith("[!", item.Text));
    }

    [Fact]
    public async Task A_translation_reaches_the_handover_and_update_items_and_balloons()
    {
        using var resources = SwappedResources.Pseudo();
        using var rig = new TrayRig();
        rig.Engine.RaiseScreenBusy(new ScreenBusyInfo(LConnectState.Service, HandoverAction.Prompt, CanTakeOver: true));
        await rig.Model.CheckForUpdates().WaitAsync(Wait.Timeout);

        var menu = rig.Model.BuildMenu();

        Assert.Equal("[!L-Connect is using the screen!]", menu[0].Text);
        Assert.Equal("[!Take over the screen from L-Connect!]", Assert.Single(menu, item => item.Key == MenuKeys.TakeOver).Text);
        Assert.Equal("[!Always take over at start!]", Assert.Single(menu, item => item.Key == MenuKeys.AlwaysTakeOver).Text);
        Assert.Equal("[!Install Marquee 0.2.0…!]", Assert.Single(menu, item => item.Key == MenuKeys.InstallUpdate).Text);
        Assert.All(rig.Host.Balloons, balloon =>
        {
            Assert.StartsWith("[!", balloon.Title);
            Assert.StartsWith("[!", balloon.Text);
        });
        Assert.Equal(2, rig.Host.Balloons.Count);
    }
}
