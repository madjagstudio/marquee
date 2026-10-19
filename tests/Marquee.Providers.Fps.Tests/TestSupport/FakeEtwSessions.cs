using System.ComponentModel;
using Marquee.Providers.Fps;

namespace Marquee.Providers.Fps.Tests.TestSupport;

/// The machine's ETW sessions as a list the test fills. Stopping one takes it off the list and records it, after
/// calling OnStop, so a test can check what else had happened by then. ListFails and StopFails make the calls throw,
/// as Windows' would when they fail.
sealed class FakeEtwSessions : IEtwSessions
{
    public List<string> Running { get; } = [];

    public List<string> Stopped { get; } = [];

    public Action<string>? OnStop { get; set; }

    public bool ListFails { get; set; }

    public HashSet<string> StopFails { get; } = [];

    public IReadOnlyList<string> Names() =>
        ListFails ? throw new Win32Exception(5) : Running.ToList();

    public void Stop(string name)
    {
        OnStop?.Invoke(name);
        if (StopFails.Contains(name)) throw new Win32Exception(5);
        Running.Remove(name);
        Stopped.Add(name);
    }
}
