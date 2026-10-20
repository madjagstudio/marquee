using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Marquee.Host;

/// The sessions a user is signed in to. Tests put a fake in its place.
public interface IUserSessions
{
    /// The IDs of the active sessions (the ones in use at a screen or over Remote Desktop) whose user is `userSid`.
    IReadOnlyList<int> SessionsOf(string userSid);
}

/// The PC's sessions, from Remote Desktop Services' API, which works on every edition (fast user switching is the same
/// thing on Home). A session's user comes from its token where this process may read it, which takes SYSTEM (the
/// installer): that is exact, with nothing to look up. Otherwise, for an administrator running `marquee task start`
/// from a terminal, it comes from the name the session was signed in with, which needs no special rights but can't
/// always be looked up (an account since deleted, no domain controller to ask, some Entra ID accounts).
public sealed class UserSessions : IUserSessions
{
    const int WTSActive = 0;
    const int WTSUserName = 5;
    const int WTSDomainName = 7;

    readonly Func<IReadOnlyList<int>> _active;
    readonly Func<int, SecurityIdentifier?> _byToken;
    readonly Func<int, SecurityIdentifier?> _byName;

    public UserSessions() : this(ActiveSessions, TokenUser, NamedUser)
    {
    }

    /// The active sessions, and the two ways of telling a session's user, null when that way can't: for tests.
    internal UserSessions(Func<IReadOnlyList<int>> active, Func<int, SecurityIdentifier?> byToken, Func<int, SecurityIdentifier?> byName)
    {
        _active = active;
        _byToken = byToken;
        _byName = byName;
    }

    public IReadOnlyList<int> SessionsOf(string userSid)
    {
        var user = new SecurityIdentifier(userSid);
        return [.. _active().Where(session => (_byToken(session) ?? _byName(session)) == user)];
    }

    static IReadOnlyList<int> ActiveSessions()
    {
        if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out var list, out var count))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var sessions = new List<int>();
        try
        {
            var size = Marshal.SizeOf<WTS_SESSION_INFO>();
            for (var i = 0; i < count; i++)
            {
                var session = Marshal.PtrToStructure<WTS_SESSION_INFO>(list + i * size);
                if (session.State == WTSActive) sessions.Add(session.SessionId);
            }
        }
        finally
        {
            WTSFreeMemory(list);
        }

        return sessions;
    }

    /// The user of the session's token, or null when this process may not read it (only SYSTEM may) or no one is
    /// signed in to it.
    internal static SecurityIdentifier? TokenUser(int sessionId)
    {
        if (!WTSQueryUserToken(sessionId, out var token)) return null;
        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
            return identity.User;
    }

    /// The session's user by the name it was signed in with, or null when no one is signed in to it (the services'
    /// session 0, a sign-in screen) or the name can't be resolved.
    static SecurityIdentifier? NamedUser(int sessionId)
    {
        var name = Text(sessionId, WTSUserName);
        if (string.IsNullOrEmpty(name)) return null;
        var domain = Text(sessionId, WTSDomainName);
        try
        {
            return (SecurityIdentifier)new NTAccount(domain ?? "", name).Translate(typeof(SecurityIdentifier));
        }
        catch (SystemException)
        {
            return null;
        }
    }

    static string? Text(int sessionId, int infoClass)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, infoClass, out var buffer, out _)) return null;
        try
        {
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WTS_SESSION_INFO
    {
        public int SessionId;
        public IntPtr WinStationName;
        public int State;
    }

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "WTSEnumerateSessionsW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool WTSEnumerateSessions(IntPtr server, int reserved, int version, out IntPtr sessions, out int count);

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "WTSQuerySessionInformationW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool WTSQuerySessionInformation(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytes);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool WTSQueryUserToken(int sessionId, out SafeAccessTokenHandle token);

    [DllImport("wtsapi32.dll")]
    static extern void WTSFreeMemory(IntPtr memory);
}
