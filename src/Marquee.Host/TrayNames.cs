using System.Security.Principal;

namespace Marquee.Host;

/// The names of the kernel objects the tray app shares with other Marquee processes. Each is in the session's Local\
/// namespace and ends with the user's SID, so two users signed in at once (fast user switching) each get their own
/// tray, and nothing of another user's can answer for this one.
public static class TrayNames
{
    static readonly Lazy<string> UserSid = new(() =>
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User!.Value;
    });

    /// A mutex that exists while a tray runs: a second MarqueeTray.exe finds it and hands over to the first.
    public static string Instance => $@"Local\Marquee-Tray-{UserSid.Value}";

    /// An event the running tray waits on: setting it opens the tray's menu.
    public static string ShowMenu => $@"Local\Marquee-Tray-Show-{UserSid.Value}";

    /// The marker that says the tray is showing the screen (TrayScreen), which `marquee run` checks.
    public static string Screen => $@"Local\Marquee-Screen-{UserSid.Value}";
}
