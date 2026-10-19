namespace Marquee.Providers.Fps.Tests.TestSupport;

static class Wait
{
    /// Long enough for a loaded CI runner to start a thread or a process.
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// Polls until the condition holds or Timeout passes. For tests of background threads.
    public static bool Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            Thread.Sleep(10);
        }

        return condition();
    }
}
