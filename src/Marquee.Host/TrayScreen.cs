namespace Marquee.Host;

/// Says that the tray app wants the screen, so `marquee run` can say who has it instead of the generic "the screen is
/// busy". The marker is a named mutex that exists while the tray holds it (TrayNames.Screen): from the moment the
/// tray's engine starts until Pause or Quit, including while it waits for the screen to be plugged in, since it takes
/// the screen as soon as it appears.
///
/// Only the mutex's existence counts; nobody owns it. Ownership belongs to one thread, and the tray pauses and resumes
/// from whichever thread runs its engine calls, while a handle can be made and closed on any thread. The object goes
/// away when the last handle closes, so a tray that crashes lets go of it too.
public static class TrayScreen
{
    /// Makes the marker; disposing the result lets it go.
    public static IDisposable Hold(string name) => new Mutex(initiallyOwned: false, name);

    /// Whether some process holds the marker. A marker this process may not open counts as held: the tray runs
    /// elevated, and the objects an elevated process makes refuse an unelevated `marquee run`, which still means the
    /// tray is there.
    public static bool IsHeld(string name)
    {
        try
        {
            if (!Mutex.TryOpenExisting(name, out var marker)) return false;
            marker.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }
}
