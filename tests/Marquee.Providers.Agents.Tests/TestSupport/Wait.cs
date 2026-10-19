namespace Marquee.Providers.Agents.Tests.TestSupport;

static class Wait
{
    /// Long enough for a loaded CI runner to schedule a pipe server's background loop and to end a killed process.
    /// Waits return as soon as their condition holds, so a generous limit costs nothing when the machine is quick.
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
