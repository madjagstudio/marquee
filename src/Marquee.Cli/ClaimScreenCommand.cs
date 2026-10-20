using Marquee.Core.Devices;
using Marquee.Core.Processes;
using Marquee.Host;

/// `marquee claim-screen`: a one-off L-Connect handover for a Marquee running elsewhere (`marquee run` in another window,
/// or an unelevated tray). This process never opens the screen itself: it stops L-Connect's watcher and service, waits
/// until the running Marquee, which retries the busy screen every 2 s, has it (the engine's ScreenSignal says so), then
/// starts L-Connect again, which skips the screen and keeps running the fans and RGB. L-Connect is always started again,
/// after at most LConnectHandover.ScreenTimeout. With no Marquee running there is nothing to hand the screen to, so it
/// leaves L-Connect alone. Stopping the service takes administrator rights, so it needs an administrator terminal.
/// Exits 0 once Marquee has the screen, 1 otherwise.
static class ClaimScreenCommand
{
    /// What the command needs from the PC, so tests can fake it: whether Marquee runs elevated, L-Connect's services and
    /// the screen signal's name.
    internal sealed record Machine(Func<bool> IsElevated, IServiceControl Services, string SignalName)
    {
        public static Machine Real => new(ProcessElevation.IsCurrentProcessElevated, new WindowsServiceControl(), ScreenSignal.DefaultName);
    }

    /// Null means the arguments were wrong and the caller should print usage.
    public static int? Run(string[] args) => Run(args, Machine.Real);

    internal static int? Run(string[] args, Machine machine) => args.Length == 0 ? Claim(machine) : null;

    static int Claim(Machine machine)
    {
        if (!machine.IsElevated())
        {
            Console.Error.WriteLine(Strings.ClaimScreenNeedsAdministrator("marquee claim-screen"));
            return 1;
        }

        using var signal = ScreenSignal.Open(machine.SignalName);
        if (signal is null)
        {
            Console.Error.WriteLine(Strings.ClaimScreenNoMarquee);
            return 1;
        }

        if (signal.IsSet)
        {
            Console.WriteLine(Strings.ClaimScreenAlreadyHeld);
            return 0;
        }

        // The handover logs each step as it goes; the result's message adds whether L-Connect came back, which is only
        // news when Marquee got the screen or L-Connect didn't start again.
        var result = new LConnectHandover(machine.Services, new ConsoleLog()).TakeScreen(signal.Wait);
        if (result.Outcome == HandoverOutcome.Taken)
        {
            Console.WriteLine(result.Message);
            return 0;
        }

        if (result.RestartFailed.Count > 0) Console.Error.WriteLine(result.Message);
        return 1;
    }
}
