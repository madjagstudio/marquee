using Marquee.Core.Devices;
using Marquee.Core.Logging;
using Marquee.Core.Sensors;
using Marquee.Host;
using Marquee.Providers.Agents.Claude;
using SkiaSharp;

namespace Marquee.App.Tests.TestSupport;

/// The engine's machine, faked: a FakeScreen when Screen says one is plugged in, no providers, servers that don't
/// listen, and the Claude limits in a temp file.
sealed class FakeServices(string claudeLimitsPath) : IEngineServices
{
    /// Whether a screen is plugged in; CreateDevice makes a new FakeScreen each time while it is.
    public volatile bool ScreenPluggedIn = true;

    public List<FakeScreen> Screens { get; } = [];

    public bool IsElevated { get; set; }

    public IDisplayDevice? CreateDevice(EngineSettings settings)
    {
        if (!ScreenPluggedIn) return null;
        var size = settings.Orientation.CanvasSize();
        var screen = new FakeScreen(size.Width, size.Height);
        lock (Screens) Screens.Add(screen);
        return screen;
    }

    public FakeScreen LastScreen
    {
        get
        {
            lock (Screens) return Screens[^1];
        }
    }

    public ISensorProvider? CreateHardwareProvider(string source, ILog log) => null;

    /// While set, the agent providers fail every poll with it, as `claude` not being found makes the Claude limits fail.
    public volatile string? AgentFault;

    public ISensorProvider CreateAgentProvider(AgentSource source, ILog log) => new NoProvider(source.ToString(), () => AgentFault);

    public ISensorProvider CreateFpsProvider(ILog log) => new NoProvider("FPS");

    public IDisposable StartStatusLineServer(SensorStore store, ILog log) => new Nothing();

    public IDisposable StartCustomValuesServer(SensorStore store, ILog log, int port, int maxKeys) => new Nothing();

    public ClaudeLimitsFile CreateClaudeLimits(ILog log) => new(claudeLimitsPath, log);

    // No LED ring, and no L-Connect: the tray's tests are about hosting the engine, so these stay out of the way.
    public RingSender? CreateRing(EngineSettings settings, SensorStore store, ILog log) => null;

    public LConnectDetector LConnect { get; } = new(new NoLConnect());

    public IServiceControl CreateServiceControl() =>
        throw new InvalidOperationException("The tray's tests never take the screen from L-Connect.");

    /// A name of this instance's own, so a Marquee running on the same PC never sees the test's signal.
    public string ScreenSignalName { get; } = $@"Local\Marquee.Tests.ScreenOpen.{Guid.NewGuid():N}";

    sealed class NoLConnect : ILConnectStatusSource
    {
        public bool IsServiceRunning() => false;

        public bool IsAppRunning() => false;
    }

    sealed class NoProvider(string name, Func<string?>? fault = null) : ISensorProvider
    {
        public string Name => name;

        public TimeSpan Interval => TimeSpan.FromSeconds(1);

        public void Poll(SensorStore store)
        {
            if (fault?.Invoke() is { } message) throw new InvalidOperationException(message);
        }

        public void Dispose() { }
    }

    sealed class Nothing : IDisposable
    {
        public void Dispose() { }
    }
}

/// A screen that counts the frames it gets and whether it is open.
sealed class FakeScreen(int width, int height) : IDisplayDevice
{
    int _frames;
    int _open;

    public DeviceInfo Info { get; } = new("Fake screen", width, height);

    public int Frames => Volatile.Read(ref _frames);

    public bool IsOpen => Volatile.Read(ref _open) == 1;

    public void Open() => Volatile.Write(ref _open, 1);

    public void PushFrame(SKImage frame) => Interlocked.Increment(ref _frames);

    public void SetBrightness(int percent) { }

    public void Close() => Volatile.Write(ref _open, 0);

    public void Dispose() => Volatile.Write(ref _open, 0);
}
