using System.ComponentModel;
using System.Runtime.InteropServices;
using static Marquee.Core.Processes.ProcessNative;

namespace Marquee.Core.Processes;

/// Whether a token is elevated, from its TokenElevation information. Unlike an Administrators-role check, this is
/// right for UAC's split tokens: an administrator's unelevated processes are not elevated.
public static class ProcessElevation
{
    public const int TokenElevationTypeDefault = 1;
    public const int TokenElevationTypeFull = 2;
    public const int TokenElevationTypeLimited = 3;

    static readonly Lazy<bool> Current = new(() =>
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out var token))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken failed");
        using (token) return IsElevated(token);
    });

    /// Whether Marquee itself runs elevated. A process's elevation never changes, so it is read once.
    public static bool IsCurrentProcessElevated() => Current.Value;

    /// The current process's TokenElevationType (see ElevationType).
    public static int CurrentElevationType()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out var token))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken failed");
        using (token) return ElevationType(token);
    }

    public static bool IsElevated(KernelHandle token)
    {
        if (!GetTokenInformation(token, TokenElevation, out var elevated, sizeof(int), out _))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetTokenInformation(TokenElevation) failed");
        return elevated != 0;
    }

    /// The token's TokenElevationType: TokenElevationTypeDefault (UAC off, or a standard user), Full (an elevated
    /// administrator) or Limited (an administrator's filtered token).
    public static int ElevationType(KernelHandle token)
    {
        if (!GetTokenInformation(token, TokenElevationType, out var type, sizeof(int), out _))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetTokenInformation(TokenElevationType) failed");
        return type;
    }
}
