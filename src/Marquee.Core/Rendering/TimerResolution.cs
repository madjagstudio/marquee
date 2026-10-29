using System.Runtime.InteropServices;

namespace Marquee.Core.Rendering;

/// Windows rounds sleeps up to its timer tick (about 15.6 ms by default), which would make 30 fps pacing uneven.
/// FrameTimer asks for 1 ms ticks while it lives, but only on Windows without a high-resolution waitable timer.
static class TimerResolution
{
    [DllImport("winmm.dll")]
    static extern uint timeBeginPeriod(uint milliseconds);

    [DllImport("winmm.dll")]
    static extern uint timeEndPeriod(uint milliseconds);

    public static IDisposable Begin()
    {
        timeBeginPeriod(1);
        return new Release();
    }

    sealed class Release : IDisposable
    {
        public void Dispose() => timeEndPeriod(1);
    }
}
