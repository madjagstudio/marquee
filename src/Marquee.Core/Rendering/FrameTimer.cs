using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Marquee.Core.Rendering;

/// Waits out the gap between frames to within about a millisecond, on a high-resolution waitable timer. A plain wait
/// rounds up to Windows' timer tick (about 15.6 ms), which makes 30 fps pacing uneven. Asking for 1 ms ticks instead
/// (timeBeginPeriod) makes the whole PC tick 1,000 times a second while Marquee runs, which keeps idle cores waking, and
/// Windows 11 doesn't promise the faster tick to a process with no visible window, such as the tray. Windows older than
/// 10 version 1803 has no high-resolution timer; there the wait falls back to asking for 1 ms ticks.
///
/// One thread at a time.
public sealed class FrameTimer : IDisposable
{
    const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x2;
    const uint TIMER_ALL_ACCESS = 0x1F0003;

    readonly Timer? _timer;
    readonly IDisposable? _resolution;

    public FrameTimer()
    {
        var handle = CreateWaitableTimerExW(IntPtr.Zero, IntPtr.Zero, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            _resolution = TimerResolution.Begin();
            return;
        }

        _timer = new Timer(handle);
    }

    /// False where Windows has no high-resolution timer and the wait asks for 1 ms ticks instead.
    public bool HighResolution => _timer is not null;

    /// Waits for `delay`, or until `token` is cancelled. False if it was cancelled.
    public bool Wait(TimeSpan delay, CancellationToken token)
    {
        if (delay <= TimeSpan.Zero) return !token.IsCancellationRequested;
        if (_timer is null) return !token.WaitHandle.WaitOne(delay);

        var due = -delay.Ticks; // negative: relative to now, in 100 ns units, as TimeSpan ticks are
        if (!SetWaitableTimer(_timer.SafeWaitHandle, in due, 0, IntPtr.Zero, IntPtr.Zero, false))
            return !token.WaitHandle.WaitOne(delay);
        return WaitHandle.WaitAny([_timer, token.WaitHandle]) == 0;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _resolution?.Dispose();
    }

    sealed class Timer : WaitHandle
    {
        public Timer(SafeWaitHandle handle) => SafeWaitHandle = handle;
    }

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    static extern SafeWaitHandle CreateWaitableTimerExW(IntPtr attributes, IntPtr name, uint flags, uint access);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetWaitableTimer(SafeWaitHandle timer, in long dueTime, int period, IntPtr completion, IntPtr argument,
        [MarshalAs(UnmanagedType.Bool)] bool resume);
}
