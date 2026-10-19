namespace Marquee.Core.Logging;

/// Sends each line to every log in turn: `marquee run` logs to the console and to the file at once.
public sealed class TeeLog(params ILog[] logs) : ILog
{
    public void Info(string message) => Each(log => log.Info(message));
    public void Warn(string message) => Each(log => log.Warn(message));
    public void Error(string message) => Each(log => log.Error(message));

    void Each(Action<ILog> write)
    {
        foreach (var log in logs)
        {
            try
            {
                write(log);
            }
            catch (Exception)
            {
                // One log failing (a closed console, say) must not cost the others the line, nor take Marquee down.
            }
        }
    }
}
