namespace Marquee.App;

/// Opens a folder in Explorer or a file in its default app, as the user (ExplorerShell; a fake in tests).
interface IShell
{
    void Open(string path);
}
