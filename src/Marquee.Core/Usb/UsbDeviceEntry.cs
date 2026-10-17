using System.Text.RegularExpressions;

namespace Marquee.Core.Usb;

/// A present USB device and the interface path used to open it.
public sealed partial record UsbDeviceEntry(ushort VendorId, ushort ProductId, string Path)
{
    public static UsbDeviceEntry? FromPath(string path)
    {
        var match = VidPid().Match(path);
        if (!match.Success) return null;
        return new UsbDeviceEntry(
            Convert.ToUInt16(match.Groups[1].Value, 16),
            Convert.ToUInt16(match.Groups[2].Value, 16),
            path);
    }

    [GeneratedRegex("vid_([0-9a-f]{4})&pid_([0-9a-f]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex VidPid();
}
