namespace Marquee.App.Tests.TestSupport;

static class Wait
{
    /// Long enough for a loaded CI runner to deliver a file-system event and start a render thread.
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

    /// Until, for async tests.
    public static async Task<bool> UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }

        return condition();
    }
}
