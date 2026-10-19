using System.Security.AccessControl;
using System.Security.Principal;

namespace Marquee.Core.Files;

/// Folders under %ProgramData%\Marquee that only administrators and SYSTEM can change, for what an elevated Marquee
/// writes: its log and the Claude limits (MarqueePaths.ElevatedDataRoot), and later the downloaded updates. Each folder
/// is made with its permissions set as it is made: not inherited, full control for SYSTEM and Administrators, and
/// read-only for the user (the last folder) or for all users (the ones above it), so "Open logs" works unelevated.
/// What Marquee writes inside inherits the same.
///
/// A folder that already exists is used only if it is already that safe: owned by Administrators or SYSTEM, and with
/// no one else allowed to change it or what it holds. Anything else is refused, never reset. %ProgramData% lets every
/// user make folders, so a program could make %ProgramData%\Marquee before Marquee does, plant links in it, and keep a
/// handle to it open; resetting the permissions wouldn't close that handle or remove what was planted, so nothing in
/// such a folder can be trusted again. The refusal says to delete the folder, after which Marquee makes it again.
public static class SecureFolder
{
    static readonly SecurityIdentifier System = new(WellKnownSidType.LocalSystemSid, null);
    static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);
    static readonly SecurityIdentifier CreatorOwner = new(WellKnownSidType.CreatorOwnerSid, null);

    /// Every right that changes a folder, what it holds, or who may: anyone with one of them could plant a link.
    /// GENERIC_ALL and GENERIC_WRITE appear raw in rules that only a folder's contents inherit.
    const int ChangeRights = (int)(FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteExtendedAttributes
        | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.WriteAttributes | FileSystemRights.Delete
        | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership) | 0x40000000 | 0x10000000;

    /// Makes `path`, a folder below %ProgramData%\Marquee, and the folders above it from %ProgramData% down, or checks
    /// they are already safe. Throws UnsafePathException when one isn't, ArgumentException for a path anywhere else.
    /// Making a folder owned by Administrators takes an elevated process.
    public static void Ensure(string path)
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var marquee = Path.Combine(programData, "Marquee");
        if (!IsBelow(Path.GetFullPath(path), marquee))
            throw new ArgumentException($"{path} is not a folder in {marquee}.", nameof(path));

        using var identity = WindowsIdentity.GetCurrent();
        Ensure(programData, path, Administrators, identity.User!);
    }

    /// Ensure below any root, which is trusted as it is. `owner` makes and owns the folders and has full control, as
    /// SYSTEM and Administrators do; `reader` may read the last folder. Tests own their folders, under a temp root.
    internal static void Ensure(string root, string path, SecurityIdentifier owner, SecurityIdentifier reader)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!IsBelow(full, root)) throw new ArgumentException($"{path} is not a folder in {root}.", nameof(path));

        var trusted = new HashSet<SecurityIdentifier> { owner, System, Administrators };
        var parts = full[(root.Length + 1)..].Split(Path.DirectorySeparatorChar);
        var folder = root;
        for (var i = 0; i < parts.Length; i++)
        {
            folder = Path.Combine(folder, parts[i]);
            if (Attributes(folder) is null)
                new DirectoryInfo(folder).Create(Permissions(owner, i == parts.Length - 1 ? reader : Users));
            if (Problem(folder, trusted) is { } problem)
                throw new UnsafePathException(
                    $"Marquee won't keep files in {folder}: {problem}, so a program not running as administrator could " +
                    "redirect what Marquee writes there as administrator. Delete the folder (an administrator can) and " +
                    "Marquee makes it again, safely.");
        }
    }

    internal static DirectorySecurity Permissions(SecurityIdentifier owner, SecurityIdentifier reader)
    {
        const InheritanceFlags Everything = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        var security = new DirectorySecurity();
        security.SetOwner(owner);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(System, FileSystemRights.FullControl, Everything, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Administrators, FileSystemRights.FullControl, Everything, PropagationFlags.None, AccessControlType.Allow));
        // Administrators for Marquee, so this adds nothing there.
        security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, Everything, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(reader, FileSystemRights.ReadAndExecute, Everything, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }

    /// Why the folder isn't safe, or null when it is.
    static string? Problem(string folder, HashSet<SecurityIdentifier> trusted)
    {
        // The entry itself: a junction's own attributes, not its target's.
        var attributes = Attributes(folder);
        if (attributes is null) return "it disappeared as it was made";
        if (attributes.Value.HasFlag(FileAttributes.ReparsePoint)) return "it is a junction or symbolic link";
        if (!attributes.Value.HasFlag(FileAttributes.Directory)) return "it is a file, not a folder";

        var security = new DirectoryInfo(folder).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
        // An owner can always change the permissions, and only an administrator can make Administrators the owner.
        if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !trusted.Contains(owner))
            return $"it is owned by {Name(security.GetOwner(typeof(SecurityIdentifier)))}, not by Administrators";

        foreach (FileSystemAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)))
        {
            var who = (SecurityIdentifier)rule.IdentityReference;
            // CREATOR OWNER stands for whoever makes something inside, and only the trusted can.
            if (rule.AccessControlType != AccessControlType.Allow || trusted.Contains(who) || who == CreatorOwner) continue;
            if (((int)rule.FileSystemRights & ChangeRights) != 0) return $"{Name(who)} can change it or what it holds";
        }

        return null;
    }

    static FileAttributes? Attributes(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    static bool IsBelow(string path, string folder) =>
        path.Length > folder.Length + 1
        && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
        && path[folder.Length] == Path.DirectorySeparatorChar;

    static string Name(IdentityReference? who)
    {
        if (who is null) return "no one";
        try
        {
            return who.Translate(typeof(NTAccount)).Value;
        }
        catch (SystemException)
        {
            // An account that no longer exists, or no domain to ask: the SID still says who.
            return who.Value;
        }
    }
}
