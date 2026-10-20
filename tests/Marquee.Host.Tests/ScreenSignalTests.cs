namespace Marquee.Host.Tests;

/// The named "a Marquee has the screen" event, under a name of each test's own, so a Marquee running on the same PC
/// never sees it.
public class ScreenSignalTests
{
    static string NewName() => $@"Local\Marquee.Tests.ScreenOpen.{Guid.NewGuid():N}";

    [Fact]
    public void Open_finds_nothing_while_no_Marquee_has_made_the_signal()
    {
        Assert.Null(ScreenSignal.Open(NewName()));
    }

    [Fact]
    public void Another_process_sees_the_engine_set_and_reset_it()
    {
        var name = NewName();
        using var engine = ScreenSignal.Create(name);
        Assert.NotNull(engine);
        using var claimScreen = ScreenSignal.Open(name);
        Assert.NotNull(claimScreen);
        Assert.False(claimScreen.IsSet);

        engine.Set();
        Assert.True(claimScreen.IsSet);
        Assert.True(claimScreen.Wait(TimeSpan.Zero));

        engine.Reset();
        Assert.False(claimScreen.Wait(TimeSpan.FromMilliseconds(20)));
    }

    [Fact]
    public void A_second_engine_shares_the_first_ones_signal()
    {
        var name = NewName();
        using var first = ScreenSignal.Create(name);
        using var second = ScreenSignal.Create(name);

        first!.Set();

        Assert.True(second!.IsSet);
    }

    [Fact]
    public void The_signal_goes_away_with_the_last_engine_that_held_it()
    {
        var name = NewName();
        ScreenSignal.Create(name)!.Dispose();

        Assert.Null(ScreenSignal.Open(name));
    }

    [Fact]
    public void A_name_another_kind_of_object_has_taken_gives_no_signal()
    {
        var name = NewName();
        using var mutex = new Mutex(false, name);

        Assert.Null(ScreenSignal.Create(name));
        Assert.Null(ScreenSignal.Open(name));
    }
}
