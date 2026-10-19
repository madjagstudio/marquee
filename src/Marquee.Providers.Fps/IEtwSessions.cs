namespace Marquee.Providers.Fps;

/// The machine's ETW (event tracing) sessions, which PresentMon records in. The real one is EtwSessions; tests use a
/// list.
interface IEtwSessions
{
    /// The names of the sessions running now. Throws Win32Exception if Windows won't list them.
    IReadOnlyList<string> Names();

    /// Stops the named session; one that isn't running is fine. Throws Win32Exception if Windows won't stop it.
    void Stop(string name);
}
