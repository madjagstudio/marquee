namespace Marquee.App.Tests;

public class BalloonClicksTests
{
    [Fact]
    public void A_click_goes_to_the_balloon_showing_once()
    {
        var clicks = new BalloonClicks();
        var clicked = 0;
        clicks.Showing(() => clicked++);

        clicks.Clicked()?.Invoke();
        clicks.Clicked()?.Invoke();

        Assert.Equal(1, clicked);
    }

    [Fact]
    public void A_balloon_that_replaces_another_takes_its_clicks()
    {
        var clicks = new BalloonClicks();
        var first = 0;
        clicks.Showing(() => first++);

        clicks.Showing(null);

        Assert.Null(clicks.Clicked());
        Assert.Equal(0, first);
    }

    [Fact]
    public void A_closed_balloon_takes_no_clicks()
    {
        var clicks = new BalloonClicks();
        clicks.Showing(() => throw new InvalidOperationException("closed"));

        clicks.Closed();

        Assert.Null(clicks.Clicked());
    }
}
