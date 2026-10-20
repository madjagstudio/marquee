namespace Marquee.Host.Tests;

/// The marker the tray holds while it wants the screen. Each test uses a name of its own, so they never meet the real
/// tray's marker or each other's.
public class TrayScreenTests
{
    static string UniqueName() => $@"Local\Marquee-Test-Screen-{Guid.NewGuid():N}";

    [Fact]
    public void The_marker_is_held_until_it_is_let_go()
    {
        var name = UniqueName();

        using (TrayScreen.Hold(name))
            Assert.True(TrayScreen.IsHeld(name));

        Assert.False(TrayScreen.IsHeld(name));
    }

    [Fact]
    public void Nobody_holds_a_marker_that_was_never_made()
    {
        Assert.False(TrayScreen.IsHeld(UniqueName()));
    }

    [Fact]
    public void Looking_does_not_keep_the_marker_alive()
    {
        var name = UniqueName();
        var held = TrayScreen.Hold(name);
        Assert.True(TrayScreen.IsHeld(name));

        held.Dispose();

        Assert.False(TrayScreen.IsHeld(name));
    }

    [Fact]
    public void The_names_are_per_user_and_session()
    {
        Assert.StartsWith(@"Local\Marquee-Tray-S-1-", TrayNames.Instance);
        Assert.StartsWith(@"Local\Marquee-Tray-Show-S-1-", TrayNames.ShowMenu);
        Assert.StartsWith(@"Local\Marquee-Screen-S-1-", TrayNames.Screen);
    }
}
