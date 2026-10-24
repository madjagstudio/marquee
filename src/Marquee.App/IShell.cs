namespace Marquee.App;

/// Opens a folder in Explorer, a file in its default app or a web page in the browser, as the user (ExplorerShell; a
/// fake in tests).
interface IShell
{
    void Open(string path);
    void Open(string path, string arguments);
}
