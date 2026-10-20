using System.Diagnostics;
using System.Security.Principal;

namespace Marquee.Host.Tests;

/// Finding the sessions a user is signed in to, with the sessions and the two ways of telling their users faked: the
/// token, which only SYSTEM can read, and the name the session was signed in with.
public class UserSessionsTests
{
    static readonly SecurityIdentifier Alice = new("S-1-5-21-1000-2000-3000-1001");
    static readonly SecurityIdentifier Bob = new("S-1-5-21-1000-2000-3000-1002");

    /// The token is exact; a name has to be looked up, which can fail or, for an account renamed or an Entra ID one,
    /// find another account.
    [Fact]
    public void A_sessions_user_comes_from_its_token_when_it_can_be_read()
    {
        var sessions = new UserSessions(() => [1, 2], byToken: id => id == 1 ? Alice : Bob, byName: _ => Bob);

        Assert.Equal([1], sessions.SessionsOf(Alice.Value));
        Assert.Equal([2], sessions.SessionsOf(Bob.Value));
    }

    /// An administrator running `marquee task start` from a terminal can't read other sessions' tokens.
    [Fact]
    public void Without_its_token_a_sessions_user_comes_from_its_name()
    {
        var sessions = new UserSessions(() => [1, 2, 3], byToken: _ => null, byName: id => id switch { 1 => Alice, 3 => Alice, _ => null });

        Assert.Equal([1, 3], sessions.SessionsOf(Alice.Value));
        Assert.Empty(sessions.SessionsOf(Bob.Value));
    }

    /// Read-only: this process's own session, whose token only SYSTEM may read.
    [Fact]
    public void Reading_a_sessions_token_takes_SYSTEM()
    {
        using var me = WindowsIdentity.GetCurrent();
        Assert.False(me.IsSystem);

        Assert.Null(UserSessions.TokenUser(Process.GetCurrentProcess().SessionId));
    }
}
