using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Marquee.Editor.Startup;

/// Whether the editor runs with an administrator's full rights, which it refuses: files it saved then would belong to
/// Administrators, and the user's own unelevated editor (and the tray's hot reload of them) could be locked out.
///
/// UAC's split token says it (TokenElevationType): Full is an elevated administrator, Limited the same administrator
/// unelevated. With UAC off, every token is Default, and an administrator's then has the full rights, so Default
/// counts as elevated for a member of Administrators.
public static class Elevation
{
    const int TokenElevationType = 18;
    const int TokenElevationTypeDefault = 1;
    const int TokenElevationTypeFull = 2;

    /// Whether this process is elevated.
    public static bool IsCurrentProcessElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return IsElevated(TypeOf(identity.AccessToken), new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator));
    }

    /// Whether a token of TokenElevationType `elevationType` has an administrator's full rights, given whether it
    /// counts as a member of Administrators (an unelevated split token doesn't).
    public static bool IsElevated(int elevationType, bool isAdministrator) =>
        elevationType == TokenElevationTypeFull || (elevationType == TokenElevationTypeDefault && isAdministrator);

    static int TypeOf(SafeAccessTokenHandle token)
    {
        if (!GetTokenInformation(token, TokenElevationType, out var type, sizeof(int), out _))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetTokenInformation(TokenElevationType) failed");
        return type;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool GetTokenInformation(SafeAccessTokenHandle token, int infoClass, out int information, int length, out int returnLength);
}
