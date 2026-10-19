using Marquee.Core.Logging;

/// Timestamped lines: information to stdout, warnings and errors to stderr.
sealed class ConsoleLog : ILog
{
    readonly object _gate = new();

    public void Info(string message) => Write(Console.Out, message);
    public void Warn(string message) => Write(Console.Error, Strings.LogWarning(message));
    public void Error(string message) => Write(Console.Error, Strings.LogError(message));

    void Write(TextWriter writer, string message)
    {
        lock (_gate) writer.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
    }
}
