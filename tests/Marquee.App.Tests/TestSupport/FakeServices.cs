using System.ServiceProcess;
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

    /// Whether another program has the screen: opening it fails as busy until this is false.
    public volatile bool ScreenInUse;

    public List<FakeScreen> Screens { get; } = [];

    public bool IsElevated { get; set; }

    public IDisplayDevice? CreateDevice(EngineSettings settings, ILog log)
    {
        if (!ScreenPluggedIn) return null;
        var size = settings.Orientation.CanvasSize();
        var screen = new FakeScreen(size.Width, size.Height, () => ScreenInUse);
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

    public IDisposable StartSensorFeedServer(SensorStore store, ILog log) => new Nothing();

    public ClaudeLimitsFile CreateClaudeLimits(ILog log) => new(claudeLimitsPath, log);

    // No LED ring, and no L-Connect: the tray's tests are about hosting the engine, so these stay out of the way.
    public RingSender? CreateRing(EngineSettings settings, SensorStore store, ILog log) => null;

    public LConnectDetector LConnect { get; } = new(new NoLConnect());

    /// Whether L-Connect's service is installed, as the tray asks the services once at start.
    public volatile bool LConnectInstalled = true;

    /// Reads L-Connect's service as running while LConnectInstalled says it is installed, but refuses to stop or start
    /// it: the tray's tests never take the screen from L-Connect, and a handover reports that as failed.
    public IServiceControl CreateServiceControl() => new NoServiceControl(this);

    /// A name of this instance's own, so a Marquee running on the same PC never sees the test's signal.
    public string ScreenSignalName { get; } = $@"Local\Marquee.Tests.ScreenOpen.{Guid.NewGuid():N}";

    sealed class NoServiceControl(FakeServices services) : IServiceControl
    {
        const string Refused = "The tray's tests never take the screen from L-Connect.";

        public ServiceControllerStatus? Status(string name) =>
            name == LConnectHandover.ServiceName && services.LConnectInstalled ? ServiceControllerStatus.Running : null;

        public void Stop(string name) => throw new InvalidOperationException(Refused);

        public void Start(string name) => throw new InvalidOperationException(Refused);

        public bool WaitFor(string name, ServiceControllerStatus status, TimeSpan timeout) => throw new InvalidOperationException(Refused);
    }

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

/// A screen that counts the frames it gets and whether it is open. While `inUse` says so, opening it fails as a
/// screen L-Connect holds does.
sealed class FakeScreen(int width, int height, Func<bool>? inUse = null) : IDisplayDevice
{
    int _frames;
    int _open;
    int _opens;
    int _closes;

    public int Opens => Volatile.Read(ref _opens);

    public int Closes => Volatile.Read(ref _closes);

    public DeviceInfo Info { get; } = new("Fake screen", width, height);

    public int Frames => Volatile.Read(ref _frames);

    public bool IsOpen => Volatile.Read(ref _open) == 1;

    public void Open()
    {
        if (inUse?.Invoke() == true) throw new DeviceBusyException("Another program is using the screen.");
        Volatile.Write(ref _open, 1);
        Interlocked.Increment(ref _opens);
    }

    public void PushFrame(SKImage frame) => Interlocked.Increment(ref _frames);

    public void SetBrightness(int percent) { }

    public void Close()
    {
        Volatile.Write(ref _open, 0);
        Interlocked.Increment(ref _closes);
    }

    public void Dispose() => Volatile.Write(ref _open, 0);
}
