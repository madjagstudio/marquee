using System.Globalization;
using Marquee.Core.Logging;

/// Timestamped lines: information to stdout, warnings and errors to stderr.
sealed class ConsoleLog : ILog
{
    readonly object _gate = new();

    public void Info(string message) => Write(Console.Out, message);
    public void Warn(string message) => Write(Console.Error, Strings.LogWarning(message));
    public void Error(string message) => Write(Console.Error, Strings.LogError(message));

    /// `[HH:mm:ss] message`, as FileLog writes it. In a custom format ":" stands for the culture's time separator, which
    /// some cultures write as ".", so the time is formatted culture-invariant.
    internal static string Line(DateTime now, string message) =>
        $"[{now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}] {message}";

    void Write(TextWriter writer, string message)
    {
        lock (_gate) writer.WriteLine(Line(DateTime.Now, message));
    }
}
