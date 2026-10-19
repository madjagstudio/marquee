namespace Marquee.Providers.Fps;

/// One running PresentMon, as PresentMonProcess sees it: the CSV it writes to stdout and how it ended. The real one is
/// a child process (PresentMonChild); tests use one that feeds CSV text.
public interface IPresentMonRun : IDisposable
{
    /// Its stdout. ReadLine returns null once it has exited, or once Dispose has stopped it.
    TextReader Output { get; }

    /// Its exit code once it has exited, else null.
    int? ExitCode { get; }

    /// The last line it wrote to stderr that wasn't blank, or null. Says why it exited, when it says anything.
    string? LastError { get; }
}
