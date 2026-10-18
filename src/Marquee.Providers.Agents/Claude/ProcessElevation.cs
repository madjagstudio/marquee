using System.ComponentModel;
using System.Runtime.InteropServices;
using static Marquee.Providers.Agents.Claude.NativeMethods;

namespace Marquee.Providers.Agents.Claude;

/// Whether a token is elevated, from its TokenElevation information. Unlike an Administrators-role check, this is
/// right for UAC's split tokens: an administrator's unelevated processes are not elevated.
internal static class ProcessElevation
{
    static readonly Lazy<bool> Current = new(() =>
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out var token))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken failed");
        using (token) return IsElevated(token);
    });

    /// Whether Marquee itself runs elevated. A process's elevation never changes, so it is read once.
    public static bool IsCurrentProcessElevated() => Current.Value;

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
