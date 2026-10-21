using Marquee.Editor.Model.Values;

namespace Marquee.Editor.Model.Tests.Values;

/// The keys the samples table names outright, which the properties panel's key picker offers beside the theme's own.
public class SampleKeysTests
{
    [Fact]
    public void The_named_keys_are_offered_without_patterns_or_now()
    {
        var keys = Samples.Keys;

        Assert.Contains("cpu.temp", keys);
        Assert.Contains("fps", keys);
        Assert.Contains("claude.five_hour.pct", keys);
        Assert.DoesNotContain(keys, key => key.Contains('*'));
        Assert.DoesNotContain("now", keys);
        Assert.Equal(keys.Distinct().Count(), keys.Count);
    }
}
