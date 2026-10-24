using System.Diagnostics;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Marquee.Core.Rendering;
using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Editing;
using Marquee.Editor.Canvas;
using Marquee.Editor.Model;
using Marquee.Editor.Mvvm;
using Marquee.Editor.Resources;

namespace Marquee.Editor.Backgrounds;

/// Owns the real renderer and its assets for one picture. Only the UI thread draws it.
public sealed class LiveThemeTile : ObservableObject, IDisposable
{
    readonly ThemeAssetCache _cache = new();
    readonly LoadedTheme _theme;
    readonly ThemeRenderer _renderer;
    WriteableBitmap? _bitmap;
    public LiveThemeTile(string name, string folder, string yaml, object? item = null, int width = 240, int height = 60, SkiaSharp.SKRect? viewBox = null, string? description = null)
    {
        Name = name;
        Item = item;
        Description = description;
        try
        {
            _theme = ThemeLoader.Load(folder, yaml, _cache);
            _renderer = new ThemeRenderer(_theme, new(width, height), viewBox);
        }
        catch { _cache.Dispose(); throw; }
    }
    public string Name { get; }
    public object? Item { get; }
    public string? Description { get; }
    public WriteableBitmap? Bitmap { get => _bitmap; private set => Set(ref _bitmap, value); }
    public void Render(SensorSnapshot values, TimeSpan time)
    {
        using var frame = _renderer.Render(values, time);
        Bitmap = FrameBitmap.Copy(frame, Bitmap);
    }
    public void Dispose() { _renderer.Dispose(); _theme.Dispose(); _cache.Dispose(); }
}

public sealed class BackgroundGalleryViewModel : IDisposable
{
    readonly Func<SensorSnapshot> _values;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1d / 15) };
    bool _disposed;
    public BackgroundGalleryViewModel(BackgroundLibrary library, Func<SensorSnapshot> values)
    {
        _values = values;
        var tiles = new List<LiveThemeTile>();
        try
        {
            foreach (var entry in library.All)
                tiles.Add(new LiveThemeTile(Name(entry), Path.GetDirectoryName(entry.File)!,
                    "canvas: { orientation: landscape }\nbackground:\n  - shader: " + YamlWriter.Scalar(Path.GetFileName(entry.File), true) +
                    "\n    scale: 0.5\n    uniforms: " + BackgroundLibrary.Defaults(entry.Info) + "\nwidgets: []\n", entry));
        }
        catch { foreach (var tile in tiles) tile.Dispose(); throw; }
        Tiles = tiles;
        _timer.Tick += OnTick;
        Refresh();
    }
    public IReadOnlyList<LiveThemeTile> Tiles { get; }
    static string Name(BackgroundEntry entry) => Path.GetFileNameWithoutExtension(entry.File) switch
    {
        "aurora" => Strings.BackgroundAurora, "waves" => Strings.BackgroundWaves,
        "plasma" => Strings.BackgroundPlasma, "starfield" => Strings.BackgroundStarfield,
        "drift" => Strings.BackgroundDrift, "nebula" => Strings.BackgroundNebula, _ => entry.Info.Name!,
    };
    public bool IsRunning => _timer.IsEnabled;
    public void Start() { if (!_disposed) _timer.Start(); }
    public void Stop() => _timer.Stop();
    void OnTick(object? sender, EventArgs e) => Refresh();
    public void Refresh()
    {
        if (_disposed) return;
        var values = _values();
        foreach (var tile in Tiles) tile.Render(values, _clock.Elapsed);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Stop(); _timer.Tick -= OnTick;
        foreach (var tile in Tiles) tile.Dispose();
    }
}
