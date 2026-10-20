using System.Security.AccessControl;
using System.Security.Principal;
using Marquee.Core.Files;
using Marquee.Core.Tests.TestSupport;

namespace Marquee.Core.Tests;

/// The real folder is under %ProgramData% and owned by Administrators, which only an elevated process can make. These
/// tests make the same folders under a temp root, owned by the unelevated user running them, who stands in for
/// Administrators; the reader is a made-up user.
public class SecureFolderTests
{
    static readonly SecurityIdentifier Me = WindowsIdentity.GetCurrent().User!;
    static readonly SecurityIdentifier Reader = new("S-1-5-21-1000-2000-3000-1001");
    static readonly SecurityIdentifier System = new(WellKnownSidType.LocalSystemSid, null);
    static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);
    static readonly SecurityIdentifier Everyone = new(WellKnownSidType.WorldSid, null);

    [Fact]
    public void Makes_each_missing_folder_writable_only_by_administrators_and_SYSTEM_and_readable_by_its_user()
    {
        using var root = new TempFolder();
        var marquee = Path.Combine(root.Path, "Marquee");
        var users = Path.Combine(marquee, "users");
        var mine = Path.Combine(users, Reader.Value);

        SecureFolder.Ensure(root.Path, mine, Me, Reader);

        foreach (var above in new[] { marquee, users })
        {
            Assert.True(IsProtected(above), above);
            Assert.Equal(Me, Owner(above));
            Assert.Equal(Expected((System, FileSystemRights.FullControl), (Administrators, FileSystemRights.FullControl),
                (Me, FileSystemRights.FullControl), (Users, FileSystemRights.ReadAndExecute)), Rules(above));
        }

        Assert.True(IsProtected(mine));
        Assert.Equal(Me, Owner(mine));
        Assert.Equal(Expected((System, FileSystemRights.FullControl), (Administrators, FileSystemRights.FullControl),
            (Me, FileSystemRights.FullControl), (Reader, FileSystemRights.ReadAndExecute)), Rules(mine));
    }

    [Fact]
    public void What_is_made_inside_gets_the_same_permissions()
    {
        using var root = new TempFolder();
        var mine = Path.Combine(root.Path, "Marquee", "users", Reader.Value);
        SecureFolder.Ensure(root.Path, mine, Me, Reader);

        var logs = Directory.CreateDirectory(Path.Combine(mine, "logs")).FullName;
        File.WriteAllText(Path.Combine(logs, "marquee.log"), "x");

        Assert.Equal(Rules(mine), Rules(logs));
        Assert.Equal(Rules(mine), Rules(Path.Combine(logs, "marquee.log")));
    }

    [Fact]
    public void A_folder_already_made_safe_is_used_as_it_is()
    {
        using var root = new TempFolder();
        var mine = Path.Combine(root.Path, "Marquee", "users", Reader.Value);
        SecureFolder.Ensure(root.Path, mine, Me, Reader);
        File.WriteAllText(Path.Combine(mine, "claude-limits.json"), "{}");

        SecureFolder.Ensure(root.Path, mine, Me, Reader);

        Assert.Equal("{}", File.ReadAllText(Path.Combine(mine, "claude-limits.json")));
    }

    /// Made first by someone else, with the rule %ProgramData% passes down: every user can add to it. Its permissions
    /// are not reset; it is refused.
    [Fact]
    public void A_folder_others_can_write_is_refused_not_reset()
    {
        using var root = new TempFolder();
        var info = Directory.CreateDirectory(Path.Combine(root.Path, "Marquee"));
        var security = info.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.CreateDirectories | FileSystemRights.CreateFiles,
            InheritanceFlags.ContainerInherit, PropagationFlags.None, AccessControlType.Allow));
        info.SetAccessControl(security);
        var marquee = info.FullName;
        var before = Rules(marquee);

        var ex = Assert.Throws<UnsafePathException>(() =>
            SecureFolder.Ensure(root.Path, Path.Combine(marquee, "users", Reader.Value), Me, Reader));

        Assert.Contains(marquee, ex.Message);
        Assert.Contains("Delete", ex.Message);
        Assert.Equal(before, Rules(marquee));
        Assert.False(IsProtected(marquee));
        Assert.False(Directory.Exists(Path.Combine(marquee, "users")));
    }

    /// Admin-only on its own, but a rule for everyone that only its contents inherit: what Marquee writes there would
    /// be writable by anyone.
    [Fact]
    public void A_folder_whose_contents_others_could_write_is_refused()
    {
        using var root = new TempFolder();
        var mine = Path.Combine(root.Path, "Marquee", "users", Reader.Value);
        SecureFolder.Ensure(root.Path, mine, Me, Reader);
        var info = new DirectoryInfo(mine);
        var security = info.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(Everyone, FileSystemRights.Modify,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.InheritOnly, AccessControlType.Allow));
        info.SetAccessControl(security);

        var ex = Assert.Throws<UnsafePathException>(() => SecureFolder.Ensure(root.Path, mine, Me, Reader));

        Assert.Contains(mine, ex.Message);
    }

    /// Only an administrator (or SYSTEM) can make an administrator the owner; an owner can always change the
    /// permissions, so a folder anyone else owns is refused, whatever its permissions say today.
    [Fact]
    public void A_folder_owned_by_anyone_but_the_owner_given_administrators_or_SYSTEM_is_refused()
    {
        using var root = new TempFolder();
        var mine = Path.Combine(root.Path, "Marquee", "users", Reader.Value);
        SecureFolder.Ensure(root.Path, mine, Me, Reader);

        var ex = Assert.Throws<UnsafePathException>(() => SecureFolder.Ensure(root.Path, mine, System, Reader));

        Assert.Contains("owned by", ex.Message);
    }

    [Fact]
    public void A_junction_on_the_path_is_refused_and_not_followed()
    {
        using var root = new TempFolder();
        using var elsewhere = new TempFolder();
        var marquee = Path.Combine(root.Path, "Marquee");
        Links.MakeJunction(marquee, elsewhere.Path);
        try
        {
            var ex = Assert.Throws<UnsafePathException>(() =>
                SecureFolder.Ensure(root.Path, Path.Combine(marquee, "users", Reader.Value), Me, Reader));

            Assert.Contains(marquee, ex.Message);
            Assert.Empty(Directory.EnumerateFileSystemEntries(elsewhere.Path));
        }
        finally
        {
            Links.RemoveJunction(marquee);
        }
    }

    [Fact]
    public void A_file_where_a_folder_should_be_is_refused()
    {
        using var root = new TempFolder();
        File.WriteAllText(Path.Combine(root.Path, "Marquee"), "");

        Assert.Throws<UnsafePathException>(() =>
            SecureFolder.Ensure(root.Path, Path.Combine(root.Path, "Marquee", "users", Reader.Value), Me, Reader));
    }

    [Theory]
    [InlineData(@"Z:\Elsewhere\Marquee")]
    [InlineData(@"Z:\RootAndMore\Marquee")]
    [InlineData(@"Z:\Root\..\Elsewhere")]
    [InlineData(@"Z:\Root")]
    public void Only_folders_below_the_root_are_made(string path)
    {
        Assert.Throws<ArgumentException>(() => SecureFolder.Ensure(@"Z:\Root", path, Me, Reader));
    }

    /// The public entry point is for %ProgramData%\Marquee and below only; anything else is refused before any folder
    /// is touched, so this runs safely unelevated.
    [Fact]
    public void The_machine_entry_point_takes_only_folders_under_ProgramData_Marquee()
    {
        using var root = new TempFolder();

        Assert.Throws<ArgumentException>(() => SecureFolder.Ensure(Path.Combine(root.Path, "Marquee")));
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        Assert.Throws<ArgumentException>(() => SecureFolder.Ensure(Path.Combine(programData, "Other")));
        Assert.Throws<ArgumentException>(() => SecureFolder.Ensure(Path.Combine(programData, "Marquee", "..", "Other")));
        Assert.Throws<ArgumentException>(() => SecureFolder.Ensure(Path.Combine(programData, "MarqueeOther", "x")));
        Assert.Throws<ArgumentException>(() => SecureFolder.Ensure(Path.Combine(programData, "Marquee")));
        Assert.False(Directory.Exists(Path.Combine(root.Path, "Marquee")));
    }

    static bool IsProtected(string folder) => new DirectoryInfo(folder).GetAccessControl().AreAccessRulesProtected;

    static SecurityIdentifier Owner(string folder) =>
        (SecurityIdentifier)new DirectoryInfo(folder).GetAccessControl().GetOwner(typeof(SecurityIdentifier))!;

    /// Every allow rule as "SID: rights", sorted. .NET adds Synchronize to the rights of every allow rule; it is left
    /// out on both sides.
    static List<string> Rules(string path)
    {
        FileSystemSecurity security = Directory.Exists(path) ? new DirectoryInfo(path).GetAccessControl() : new FileInfo(path).GetAccessControl();
        return Expected(security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Select(rule => ((SecurityIdentifier)rule.IdentityReference, rule.FileSystemRights))
            .ToArray());
    }

    static List<string> Expected(params (SecurityIdentifier Who, FileSystemRights Rights)[] rules) =>
        rules.Select(rule => $"{rule.Who.Value}: {rule.Rights & ~FileSystemRights.Synchronize}").Order(StringComparer.Ordinal).ToList();
}
