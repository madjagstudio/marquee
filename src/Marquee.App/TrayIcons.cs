using System.Drawing;

namespace Marquee.App;

/// Marquee's icon (marquee.ico, drawn by tools/Marquee.MakeIcon), embedded so the tray needn't find a file.
static class TrayIcons
{
    /// The icon at the size closest to `size`. The notification area wants SystemInformation.SmallIconSize: 16 px at
    /// 100% scaling, 20, 24 or 32 at 125%, 150% or 200%, all of which the file holds, so Windows never scales it.
    public static Icon Load(Size size)
    {
        using var stream = typeof(TrayIcons).Assembly.GetManifestResourceStream("marquee.ico")
            ?? throw new InvalidOperationException("MarqueeTray was built without its icon.");
        return new Icon(stream, size);
    }
}
