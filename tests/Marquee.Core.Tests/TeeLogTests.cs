using Marquee.Core.Logging;

namespace Marquee.Core.Tests;

public class TeeLogTests
{
    /// Records "name level: message" into a list shared with the other logs, so the test sees the order across logs.
    sealed class SharedLog(string name, List<string> lines) : ILog
    {
        public void Info(string message) => lines.Add($"{name} info: {message}");
        public void Warn(string message) => lines.Add($"{name} warn: {message}");
        public void Error(string message) => lines.Add($"{name} error: {message}");
    }

    sealed class ThrowingLog : ILog
    {
        public void Info(string message) => throw new IOException("console closed");
        public void Warn(string message) => throw new IOException("console closed");
        public void Error(string message) => throw new IOException("console closed");
    }

    [Fact]
    public void Each_line_goes_to_every_log_in_order()
    {
        var lines = new List<string>();
        var tee = new TeeLog(new SharedLog("a", lines), new SharedLog("b", lines));

        tee.Info("one");
        tee.Warn("two");
        tee.Error("three");

        Assert.Equal(
            new[] { "a info: one", "b info: one", "a warn: two", "b warn: two", "a error: three", "b error: three" },
            lines);
    }

    [Fact]
    public void A_throwing_log_does_not_stop_the_others()
    {
        var lines = new List<string>();
        var tee = new TeeLog(new SharedLog("a", lines), new ThrowingLog(), new SharedLog("b", lines));

        tee.Info("one");
        tee.Warn("two");
        tee.Error("three");

        Assert.Equal(
            new[] { "a info: one", "b info: one", "a warn: two", "b warn: two", "a error: three", "b error: three" },
            lines);
    }
}
