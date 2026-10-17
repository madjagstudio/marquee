namespace Marquee.Core.Tests.TestSupport;

static class Wait
{
    /// Polls until the condition holds or five seconds pass. For tests of background threads.
    public static bool Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            Thread.Sleep(10);
        }

        return condition();
    }
}
