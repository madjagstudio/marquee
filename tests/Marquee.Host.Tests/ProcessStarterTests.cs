using Marquee.Host.Updates;

namespace Marquee.Host.Tests;

/// The real ProcessStarter, with cmd.exe standing in for msiexec (no test runs msiexec).
public class ProcessStarterTests
{
    static readonly string Cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Fact]
    public async Task A_started_process_can_be_waited_for_and_gives_its_exit_code()
    {
        using var process = new ProcessStarter().Start(Cmd, "/d /c exit 1618");

        Assert.Equal(1618, await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task Cancelling_the_wait_stops_the_waiting_not_the_process()
    {
        using var process = new ProcessStarter().Start(Cmd, "/d /c exit 5");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => process.WaitForExitAsync(cancel.Token));

        Assert.Equal(5, await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void A_program_that_cant_be_started_throws()
    {
        Assert.Throws<System.ComponentModel.Win32Exception>(() =>
            new ProcessStarter().Start(Path.Combine(Path.GetTempPath(), "marquee-no-such-program-" + Guid.NewGuid().ToString("N") + ".exe"), ""));
    }
}
