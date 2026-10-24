using Marquee.Core;
using Marquee.Core.Config;
using Marquee.Core.Devices;
using Marquee.Core.Logging;
using Marquee.Core.Themes;
using Marquee.Editor.Backgrounds;
using Marquee.Editor.Model;
using Marquee.Editor.Model.Values;
using Marquee.Editor.Resources;
using Marquee.Editor.Session;
using Marquee.Host;
using System.Diagnostics;
using System.Windows.Threading;

namespace Marquee.Editor.Start;

public sealed record StartChoice(string Id, string Name);

public sealed class StartViewModel : IDisposable
{
    readonly EditorEnvironment _environment;
    readonly ValueSources _values;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1d / 15) };
    readonly Stopwatch _clock = Stopwatch.StartNew();
    bool _disposed;
    public StartViewModel(EditorEnvironment environment)
    {
        _environment = environment;
        Orientation = ConfigFile.Load(environment.ConfigFile, NullLog.Instance).Orientation ?? Marquee.Core.Devices.Orientation.Landscape;
        _values = environment.ValueSources();
        var tiles = new List<LiveThemeTile>();
        try
        {
            foreach (var choice in Choices.Where(choice => Directory.Exists(Path.Combine(environment.InstalledThemes, choice.Id))))
            {
                var folder = Path.Combine(environment.InstalledThemes, choice.Id);
                using var session = EditorSession.Open(folder);
                Orient(session);
                tiles.Add(new(choice.Name, folder, session.Document.Text, choice,
                    Orientation.IsPortrait() ? 72 : 264, Orientation.IsPortrait() ? 288 : 66));
            }
        }
        catch { foreach (var tile in tiles) tile.Dispose(); _values.Dispose(); throw; }
        Tiles = tiles;
        _timer.Tick += Tick; Refresh();
    }
    public IReadOnlyList<StartChoice> Choices { get; } =
    [new("start-blank", Strings.StartBlank), new("start-gaming", Strings.StartGaming), new("start-monitor", Strings.StartMonitor)];
    public Orientation Orientation { get; }
    public string Screen => Strings.ScreenTag(Orientation.IsPortrait() ? Strings.StartPortrait : Strings.StartLandscape, Orientation.CanvasSize().Width, Orientation.CanvasSize().Height);
    public IReadOnlyList<LiveThemeTile> Tiles { get; }
    public string? ChosenFolder { get; private set; }
    public string Create(string id, string name)
    {
        var choice = Choices.Single(choice => choice.Id == id);
        var folder = _environment.Opening.MakeCopy(Path.Combine(_environment.InstalledThemes, choice.Id), name);
        using var session = EditorSession.Open(folder);
        Orient(session);
        File.WriteAllBytes(session.LayoutPath, session.Document.ToBytes());
        return folder;
    }
    public bool Choose(StartChoice choice)
    {
        var source = Path.Combine(_environment.InstalledThemes, choice.Id);
        var suggested = Path.GetFileName(_environment.Opening.CopyFolderFor(source));
        if (_environment.Dialogs.AskCopyName(choice.Name, suggested, _environment.Opening.CopyNameProblem) is not { } name) return false;
        try { ChosenFolder = Create(choice.Id, name); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or ThemeException)
        { _environment.Dialogs.ShowError(Strings.CouldNotCopy(choice.Name, ex.Message)); return false; }
    }
    public bool OpenYours()
    {
        ChosenFolder = _environment.Dialogs.PickOpenFolder();
        return ChosenFolder is not null;
    }
    void Orient(EditorSession session)
    {
        var portrait = Orientation.IsPortrait();
        if ((session.Theme!.Spec.Canvas.Orientation == ThemeOrientation.Portrait) != portrait)
            session.Apply(new SetOrientation(portrait ? "portrait" : "landscape", true));
    }
    public void Start() { if (!_disposed && !_timer.IsEnabled) { _values.SetActive(true); _timer.Start(); Refresh(); } }
    public void Stop() { _timer.Stop(); _values.SetActive(false); }
    void Tick(object? sender, EventArgs e) => Refresh();
    public void Refresh()
    {
        if (_disposed) return;
        var values = _values.Snapshot(ModuleLibrary.Keys.Concat(["gpu.temp", "fps"]));
        foreach (var tile in Tiles) tile.Render(values, _clock.Elapsed);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Stop(); _timer.Tick -= Tick;
        foreach (var tile in Tiles) tile.Dispose();
        _values.Dispose();
    }
}
