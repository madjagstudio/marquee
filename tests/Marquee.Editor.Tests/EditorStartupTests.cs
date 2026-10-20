using Marquee.Editor.Model;
using Marquee.Editor.Resources;
using Marquee.Editor.Startup;
using Marquee.Editor.Tests.TestSupport;

namespace Marquee.Editor.Tests;

/// Before the window: the editor refuses to run elevated, then opens the theme its arguments name.
public class EditorStartupTests
{
    [Fact]
    public void Elevated_it_says_why_and_opens_nothing() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var mine = environment.AddTheme("mine");
        var elevated = environment.Environment with { IsElevated = () => true };

        var workspace = EditorStartup.Open(["mine"], elevated, SynchronizationContext.Current!);

        Assert.Null(workspace);
        Assert.Equal([Strings.RefuseElevated], environment.Dialogs.Errors);
        // Nothing was taken or written: the theme isn't locked, and the editor's folder wasn't made.
        using (var themeLock = ThemeOpening.TryLock(mine)) Assert.NotNull(themeLock);
        Assert.False(Directory.Exists(environment.Environment.Folders.Root));
    });

    [Fact]
    public void Unelevated_it_opens_the_theme_named() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var mine = environment.AddTheme("mine");

        using var workspace = EditorStartup.Open(["mine"], environment.Environment, SynchronizationContext.Current!);

        Assert.NotNull(workspace);
        Assert.Equal(mine, workspace.Session.Folder);
        Assert.Empty(environment.Dialogs.Errors);
    });

    [Fact]
    public void A_theme_that_isnt_there_is_said_and_nothing_opens() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();

        var workspace = EditorStartup.Open(["nowhere"], environment.Environment, SynchronizationContext.Current!);

        Assert.Null(workspace);
        Assert.Contains("nowhere", Assert.Single(environment.Dialogs.Errors));
    });

    [Theory]
    [InlineData(2, false, true)]   // TokenElevationTypeFull: an elevated administrator
    [InlineData(3, false, false)]  // TokenElevationTypeLimited: the same administrator, unelevated
    [InlineData(1, true, true)]    // TokenElevationTypeDefault with UAC off: an administrator has the full rights
    [InlineData(1, false, false)]  // TokenElevationTypeDefault: a standard user
    public void Elevation_is_read_from_the_tokens_elevation_type(int elevationType, bool isAdministrator, bool elevated) =>
        Assert.Equal(elevated, Elevation.IsElevated(elevationType, isAdministrator));
}
