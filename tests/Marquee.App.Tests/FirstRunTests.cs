using Marquee.App.Tests.TestSupport;
using Marquee.Core.Config;

namespace Marquee.App.Tests;

public class FirstRunTests
{
    [Fact]
    public void A_config_with_no_theme_gets_example_landscape()
    {
        Assert.Equal("example-landscape", FirstRun.WithDefaultTheme(MarqueeConfig.Default).Theme);
        Assert.Equal("neon", FirstRun.WithDefaultTheme(MarqueeConfig.Default with { Theme = "neon" }).Theme);
    }

    [Fact]
    public void The_hint_is_claimed_once_and_remembered_in_the_data_folder()
    {
        using var dir = new TempDir();
        var data = dir.File("data"); // not there yet, as on a fresh install

        Assert.True(FirstRun.ClaimHint(data));
        Assert.False(FirstRun.ClaimHint(data));
        Assert.True(File.Exists(Path.Combine(data, FirstRun.HintMarker)));
    }

    [Fact]
    public void A_hint_that_cant_be_remembered_is_not_shown_at_every_start()
    {
        using var dir = new TempDir();
        var file = dir.Write("data", ""); // a file where the folder should be, so the marker can't be written

        Assert.False(FirstRun.ClaimHint(file));
    }

    [Fact]
    public void The_handover_offer_is_claimed_once_apart_from_the_hint()
    {
        using var dir = new TempDir();
        var data = dir.File("data");

        Assert.True(FirstRun.ClaimHint(data));
        Assert.True(FirstRun.ClaimHandoverOffer(data));
        Assert.False(FirstRun.ClaimHandoverOffer(data));
        Assert.True(File.Exists(Path.Combine(data, FirstRun.HandoverOfferMarker)));
    }
}
