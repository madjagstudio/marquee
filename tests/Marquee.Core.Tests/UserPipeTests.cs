using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Marquee.Core.Pipes;

namespace Marquee.Core.Tests;

public class UserPipeTests
{
    static string UniquePipe() => "marquee-test-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void The_current_user_is_this_process_users_sid() =>
        Assert.Equal(WindowsIdentity.GetCurrent().User, UserPipe.CurrentUser);

    [Fact]
    public void Server_security_names_the_user_not_the_token_owner()
    {
        var user = new SecurityIdentifier("S-1-5-21-1000-2000-3000-1001");

        var security = UserPipe.ServerSecurity(user);

        Assert.Equal(user, security.GetOwner(typeof(SecurityIdentifier)));
        var rule = Assert.Single(security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>());
        Assert.Equal((user, AccessControlType.Allow, PipeAccessRights.FullControl), (rule.IdentityReference, rule.AccessControlType, rule.PipeAccessRights));
    }

    [Fact]
    public void A_server_is_owned_by_the_user_and_open_only_to_the_user()
    {
        using var server = UserPipe.CreateServer(UniquePipe(), PipeDirection.Out, 1);

        var security = server.GetAccessControl();
        var user = WindowsIdentity.GetCurrent().User;
        Assert.Equal(user, security.GetOwner(typeof(SecurityIdentifier)));
        var rule = Assert.Single(security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>());
        Assert.Equal((user, AccessControlType.Allow, PipeAccessRights.FullControl), (rule.IdentityReference, rule.AccessControlType, rule.PipeAccessRights));
        Assert.True(UserPipe.IsOwnedBy(server, user!));
    }

    [Fact]
    public void An_outbound_server_can_only_write()
    {
        using var server = UserPipe.CreateServer(UniquePipe(), PipeDirection.Out, 1);

        Assert.Equal((false, true), (server.CanRead, server.CanWrite));
    }

    [Fact]
    public void A_server_refuses_a_pipe_someone_else_owns()
    {
        // Another user can create the pipe name first and let anyone add instances; Marquee would then be serving that
        // user's pipe. Simulated by expecting a different owner than the one the pipe gets.
        var someoneElse = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

        Assert.Throws<UnauthorizedAccessException>(() => UserPipe.CreateServer(UniquePipe(), PipeDirection.Out, 1, someoneElse));
    }

    /// Without a label, a pipe is Medium with no-write-up only, so a Low-integrity process of the same user (a sandboxed
    /// browser's, say) could read an outbound pipe such as the sensor feed. No-read-up shuts it out too.
    [Fact]
    public void A_server_carries_a_medium_label_with_no_read_up_and_no_write_up()
    {
        var name = UniquePipe();
        using var server = UserPipe.CreateServer(name, PipeDirection.Out, 1);
        using var client = new NamedPipeClientStream(".", name, PipeDirection.In);
        client.Connect(TimeSpan.FromSeconds(30));

        Assert.EndsWith("(ML;;NWNR;;;ME)", UserPipe.MandatoryLabel(server));
        Assert.EndsWith("(ML;;NWNR;;;ME)", UserPipe.MandatoryLabel(client));
    }

    /// A Low-integrity process of the user (a sandboxed browser's) can make a pipe the user owns, but only one labelled
    /// Low, since no process can raise a label above its own: a client that trusts only a pipe of Medium or higher
    /// can't be fed by it. Lowering a label needs no privilege, so the test makes such a pipe itself.
    [Fact]
    public void A_pipe_below_medium_integrity_is_not_trusted_though_the_user_owns_it()
    {
        var user = WindowsIdentity.GetCurrent().User!;
        var low = UniquePipe();
        var sddl = UserPipe.ServerSecurity(user).GetSecurityDescriptorSddlForm(AccessControlSections.Owner | AccessControlSections.Access);
        var descriptor = new RawSecurityDescriptor(sddl + "S:(ML;;NW;;;LW)");
        var binary = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(binary, 0);
        using var squatter = UserPipeNative.Create(low, PipeDirection.Out, 1, firstInstance: false, binary);
        var marquee = UniquePipe();
        using var server = UserPipe.CreateServer(marquee, PipeDirection.Out, 1);
        var unlabelled = UniquePipe();
        using var plain = new NamedPipeServerStream(unlabelled, PipeDirection.Out, 1);

        Assert.Equal((true, false), Trust(low, user));
        Assert.Equal((true, true), Trust(marquee, user));
        Assert.Equal((true, true), Trust(unlabelled, user)); // Medium without a label, as this test process is
    }

    static (bool Owned, bool Trusted) Trust(string name, SecurityIdentifier user)
    {
        using var client = new NamedPipeClientStream(".", name, PipeDirection.In);
        client.Connect(TimeSpan.FromSeconds(30));
        return (UserPipe.IsOwnedBy(client, user), UserPipe.IsTrusted(client, user));
    }

    [Theory]
    [InlineData("", 0x2000)]
    [InlineData("S:(ML;;NW;;;LW)", 0x1000)]
    [InlineData("S:AI(ML;;NWNR;;;ME)", 0x2000)]
    [InlineData("S:(ML;;NW;;;MP)", 0x2100)]
    [InlineData("S:(ML;;NW;;;HI)", 0x3000)]
    [InlineData("S:(ML;;NW;;;SI)", 0x4000)]
    [InlineData("S:(ML;;NW;;;S-1-16-12288)", 0x3000)]
    [InlineData("S:(ML;;NW;;;S-1-16-0)", 0)]
    [InlineData("S:(ML;;NW;;;XX)", 0)]
    public void A_label_reads_as_its_integrity_level(string label, int level) =>
        Assert.Equal(level, UserPipe.IntegrityOf(label));

    [Fact]
    public void The_server_descriptor_is_server_security_with_the_label()
    {
        var user = new SecurityIdentifier("S-1-5-21-1000-2000-3000-1001");

        var descriptor = new RawSecurityDescriptor(UserPipe.ServerDescriptor(user), 0);

        var sections = AccessControlSections.Owner | AccessControlSections.Access;
        Assert.Equal(UserPipe.ServerSecurity(user).GetSecurityDescriptorSddlForm(sections), descriptor.GetSddlForm(sections));
        // .NET reads a label ACE as a custom one: SYSTEM_MANDATORY_LABEL_ACE_TYPE, NWNR (no-write-up and no-read-up), and
        // the Medium integrity SID, S-1-16-8192.
        var label = Assert.IsType<CustomAce>(Assert.Single(descriptor.SystemAcl!));
        var medium = new SecurityIdentifier("S-1-16-8192");
        var sid = new byte[medium.BinaryLength];
        medium.GetBinaryForm(sid, 0);
        var data = label.GetOpaque()!;
        Assert.Equal((AceType)0x11, label.AceType);
        Assert.Equal(0x3, BitConverter.ToInt32(data, 0));
        Assert.Equal(sid, data[4..]);
    }

    [Fact]
    public void Instance_limits_and_first_instances_are_kept()
    {
        var shared = UniquePipe();
        using var first = UserPipe.CreateServer(shared, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances);
        using var second = UserPipe.CreateServer(shared, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances);
        var single = UniquePipe();
        using var only = UserPipe.CreateServer(single, PipeDirection.Out, 1);

        Assert.Throws<UnauthorizedAccessException>(() => UserPipe.CreateServer(shared, PipeDirection.In, 1, firstInstance: true));
        Assert.Throws<IOException>(() => UserPipe.CreateServer(single, PipeDirection.Out, 1));
    }
}
