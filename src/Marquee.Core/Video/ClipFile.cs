namespace Marquee.Core.Video;

/// Opens a clip so that it stays editable while it plays. Left to themselves, Media Foundation opens a file without
/// write or delete sharing, and Skia without delete sharing: a theme's author couldn't save over a playing clip, or
/// save it the way many editors do (write a new file, then rename it over the old one), and the theme would never
/// reload with the edit. A clip changed under a decoder may fail partway, which its player handles, until the reload
/// replaces it.
static class ClipFile
{
    public static FileStream Open(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
}
