namespace Marquee.Host;

/// A named event that is set while a Marquee in this Windows session has the screen open.
/// Each engine holds it and sets or resets it as its screen opens and closes. `marquee
/// claim-screen` stops L-Connect for a Marquee running in another window, and waits on it to know when to start L-Connect
/// again. The name is in the session's Local namespace, so another signed-in user's Marquee doesn't answer for this one.
/// A named kernel object lasts only while a process holds it, so a signal that can be opened means a Marquee is running.
/// Any program of the same user can set it too, which at worst makes claim-screen start L-Connect again early.
public sealed class ScreenSignal : IDisposable
{
    public const string DefaultName = @"Local\Marquee.ScreenOpen";

    readonly EventWaitHandle _event;

    ScreenSignal(EventWaitHandle handle) => _event = handle;

    /// Whether a Marquee has the screen.
    public bool IsSet => _event.WaitOne(0);

    /// The engine's side: opens the signal, or makes it if no other Marquee has. Null when it can't: another kind of
    /// object has the name, or an elevated Marquee made it and this unelevated one may not change it. That engine then
    /// just doesn't signal, which only matters to claim-screen, and the elevated one runs its own handover anyway.
    public static ScreenSignal? Create(string name)
    {
        try
        {
            return new(new EventWaitHandle(false, EventResetMode.ManualReset, name));
        }
        catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    /// Claim-screen's side: the signal a running Marquee made, or null when no Marquee is running in this session (or
    /// the name belongs to something else, or to an object claim-screen may not open).
    public static ScreenSignal? Open(string name)
    {
        try
        {
            return EventWaitHandle.TryOpenExisting(name, out var handle) ? new(handle) : null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public void Set() => _event.Set();

    public void Reset() => _event.Reset();

    /// Waits up to `timeout` for a Marquee to have the screen; false when none has it by then.
    public bool Wait(TimeSpan timeout) => _event.WaitOne(timeout);

    public void Dispose() => _event.Dispose();
}
