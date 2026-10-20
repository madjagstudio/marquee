using System.Globalization;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Marquee.Core.Pipes;

/// Named pipes that only the signed-in user can open: the status-line pipe and the theme editor's sensor feed.
///
/// The lock is the user's own SID, not PipeOptions.CurrentUserOnly: that option uses the token's default owner, which
/// for an elevated administrator is BUILTIN\Administrators. An elevated Marquee (needed for CPU temperatures) would then
/// make a pipe that the user's non-elevated processes (the status-line bridge, the theme editor) can neither open nor
/// recognize. With the user's SID, any of this user's processes, elevated or not, can use the pipe; other users can't.
public static class UserPipe
{
    /// The user this process runs as. An administrator's elevated and non-elevated processes share it.
    public static SecurityIdentifier? CurrentUser
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User;
        }
    }

    /// One more instance of the server end of `name`, open only to the current user, with no buffers of its own.
    /// Throws UnauthorizedAccessException if the pipe already exists and isn't this user's (another user created the
    /// name first and let anyone add instances): Marquee would then be serving that user's pipe, whose access rules that
    /// user chose. Throws IOException when `maxInstances` instances are open already. With `firstInstance`, it also
    /// throws UnauthorizedAccessException when any instance of `name` exists already, whoever made it.
    public static NamedPipeServerStream CreateServer(string name, PipeDirection direction, int maxInstances, bool firstInstance = false) =>
        Create(name, direction, maxInstances, CurrentUser, firstInstance);

    /// CreateServer, accepting the pipe only if `owner` owns it.
    public static NamedPipeServerStream CreateServer(string name, PipeDirection direction, int maxInstances, SecurityIdentifier? owner) =>
        Create(name, direction, maxInstances, owner, firstInstance: false);

    static NamedPipeServerStream Create(string name, PipeDirection direction, int maxInstances, SecurityIdentifier? owner, bool firstInstance)
    {
        var user = CurrentUser ?? throw new UnauthorizedAccessException("this process has no Windows user to restrict the pipe to");
        var pipe = UserPipeNative.Create(name, direction, maxInstances, firstInstance, ServerDescriptor(user));
        try
        {
            if (owner is null || !IsOwnedBy(pipe, owner))
                throw new UnauthorizedAccessException($"the pipe {name} already exists and belongs to another user");
            return pipe;
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
    }

    /// Owned by `user`, with full control for `user` only (full control includes adding instances, which a server
    /// needs for its next client). The pipes are made with this and a label (ServerDescriptor), which PipeSecurity can't
    /// hold.
    public static PipeSecurity ServerSecurity(SecurityIdentifier user)
    {
        var security = new PipeSecurity();
        security.SetOwner(user);
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }

    /// ServerSecurity labelled Medium integrity with no-read-up as well as no-write-up, in its binary form. Without a
    /// label a pipe is Medium with no-write-up only, so a Low-integrity process of the same user (a sandboxed browser's,
    /// say) could still read an outbound pipe such as the sensor feed; no-read-up shuts it out. Medium, not an elevated
    /// tray's High, so the user's own non-elevated processes, the editor and the status-line bridge, keep their access.
    /// Setting a label at or below a process's own integrity needs no privilege. For an inbound pipe (the status line)
    /// it changes nothing: a Low process could already not write to it.
    internal static byte[] ServerDescriptor(SecurityIdentifier user)
    {
        var sddl = ServerSecurity(user).GetSecurityDescriptorSddlForm(AccessControlSections.Owner | AccessControlSections.Access);
        var descriptor = new RawSecurityDescriptor(sddl + MediumLabel);
        var binary = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(binary, 0);
        return binary;
    }

    /// The mandatory label every one of Marquee's pipes has (ServerDescriptor).
    public const string MediumLabel = "S:(ML;;NWNR;;;ME)";

    /// Whether `owner` owns the pipe. A client checks before it trusts what it reads or hands over what it writes:
    /// another user could create the name before Marquee starts.
    public static bool IsOwnedBy(PipeStream pipe, SecurityIdentifier owner) =>
        owner.Equals(pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)));

    /// Whether the pipe could be the user's Marquee's: owned by `owner`, and of Medium integrity or higher. A
    /// Low-integrity process of the same user (a sandboxed browser's) can make a pipe the user owns, but it can't label
    /// it above its own Low, so a client that checks only the owner could be fed by it.
    public static bool IsTrusted(PipeStream pipe, SecurityIdentifier owner) =>
        IsOwnedBy(pipe, owner) && IntegrityOf(MandatoryLabel(pipe)) >= MediumIntegrity;

    /// The pipe's mandatory integrity label in SDDL, such as MediumLabel, or empty when it has none.
    public static string MandatoryLabel(PipeStream pipe) => UserPipeNative.Label(pipe.SafePipeHandle);

    const int MediumIntegrity = 0x2000;

    /// The integrity level a label in SDDL gives (0x1000 Low, 0x2000 Medium, 0x3000 High, 0x4000 System); Medium for
    /// none, as Windows reads an object without a label, and 0 for a label it doesn't recognize.
    internal static int IntegrityOf(string label)
    {
        var start = label.IndexOf("(ML;", StringComparison.Ordinal);
        if (start < 0) return MediumIntegrity;
        var end = label.IndexOf(')', start);
        if (end < 0) return 0;
        var sid = label[(label.LastIndexOf(';', end) + 1)..end];
        return sid switch
        {
            "LW" => 0x1000,
            "ME" => MediumIntegrity,
            "MP" => 0x2100,
            "HI" => 0x3000,
            "SI" => 0x4000,
            _ when sid.StartsWith("S-1-16-", StringComparison.Ordinal)
                   && int.TryParse(sid.AsSpan(7), NumberStyles.None, CultureInfo.InvariantCulture, out var level) => level,
            _ => 0,
        };
    }
}
