using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace Marquee.Editor.Tests.TestSupport;

/// WPF on a test thread. xUnit runs tests on pool threads (MTA), and WPF needs a single-threaded apartment with a
/// dispatcher: Run gives the test one of its own, with the dispatcher's synchronisation context as the UI context the
/// editor posts to, and rethrows on the test's thread what the test threw there.
static class Sta
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    public static void Run(Action test)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            try
            {
                test();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        })
        {
            IsBackground = true,
            Name = "STA test",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(DefaultTimeout)) throw new TimeoutException($"The STA test didn't finish in {DefaultTimeout}.");
        failure?.Throw();
    }

    /// Runs what the dispatcher has queued, down to Background priority: bindings, layout, rendering, and what other
    /// threads posted to the UI context.
    public static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    /// Pumps until `done`, for work another thread posts (a save's state, a signal); false when it hasn't happened in
    /// `timeout`.
    public static bool PumpUntil(Func<bool> done, TimeSpan? timeout = null)
    {
        var until = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (!done())
        {
            if (DateTime.UtcNow > until) return false;
            Pump();
            Thread.Sleep(10);
        }

        return true;
    }
}
