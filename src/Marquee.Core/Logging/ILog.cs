namespace Marquee.Core.Logging;

/// Where the app reports what it is doing. The CLI writes to the console; rolling file logs arrive with the tray app.
public interface ILog
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}

public sealed class NullLog : ILog
{
    public static readonly NullLog Instance = new();

    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message) { }
}
