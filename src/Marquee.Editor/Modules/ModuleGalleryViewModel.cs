using System.Diagnostics;
using System.Windows.Threading;
using Marquee.Core.Sensors;
using Marquee.Editor.Backgrounds;
using Marquee.Editor.Model;
using Marquee.Core.Themes.Editing;
using SkiaSharp;
using System.Collections.ObjectModel;
using Marquee.Editor.Session;

namespace Marquee.Editor.Modules;

public sealed record ModuleCategoryView(ModuleCategory Category, string Name, IReadOnlyList<LiveThemeTile> Tiles)
{
    public bool IsBlank => Category == ModuleCategory.BlankWidgets;
}

public sealed class ModuleGalleryViewModel : IDisposable
{
    readonly Func<SensorSnapshot> _values;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    bool _disposed;
    readonly UserModules? _userModules;
    public IEditorDialogs? Dialogs { get; init; }
    public ModuleGalleryViewModel(string folder, Func<SensorSnapshot> values, UserModules? userModules = null)
    {
        _userModules = userModules;
        _values = values;
        var tiles = new List<LiveThemeTile>();
        try
        {
            foreach (var module in ModuleLibrary.All) tiles.Add(Tile(module, folder));
            if (userModules is not null)
                foreach (var module in userModules.All()) tiles.Add(Tile(module, userModules.Folder(module.Id)));
            Categories = Enum.GetValues<ModuleCategory>().Select(category => new ModuleCategoryView(category, ModuleLibrary.CategoryName(category),
                new ObservableCollection<LiveThemeTile>(tiles.Where(tile => ((Module)tile.Item!).Category == category)))).ToList();
        }
        catch { foreach (var tile in tiles) tile.Dispose(); throw; }
        _timer.Tick += Tick;
        Refresh();
    }
    public IReadOnlyList<ModuleCategoryView> Categories { get; }
    public IReadOnlyList<Shell.MenuChoice> BlankWidgets { get; init; } = [];
    public void Rename(string name, string newName) => ChangeMine(() => _userModules!.Rename(name, newName));
    public void Delete(string name) => ChangeMine(() => _userModules!.Delete(name));
    void ChangeMine(Action change)
    {
        // Release video/font handles before moving or deleting the module's files.
        var tiles = (ObservableCollection<LiveThemeTile>)Categories.Single(c => c.Category == ModuleCategory.MyModules).Tiles;
        foreach (var tile in tiles) tile.Dispose();
        tiles.Clear();
        try { change(); } finally { ReloadMine(); }
    }
    public string? NameProblem(string name) => _userModules!.NameProblem(name);
    void ReloadMine()
    {
        var tiles = (ObservableCollection<LiveThemeTile>)Categories.Single(c => c.Category == ModuleCategory.MyModules).Tiles;
        foreach (var tile in tiles) tile.Dispose();
        tiles.Clear();
        foreach (var module in _userModules!.All()) tiles.Add(Tile(module, _userModules.Folder(module.Id)));
        Refresh();
    }
    public static LiveThemeTile Tile(Module module, string folder)
    {
        var size = ModuleLibrary.Size(module);
        var yaml = module.Yaml;
        if (module.NeedsFile)
        {
            // Before a file is chosen, show a picture/video frame using ordinary rendered shapes.
            yaml = "type: group\nat: [0, 0]\nsize: [320, 180]\nchildren:\n" +
                "  - { type: shape, shape: rect, at: [0, 0], size: [320, 180], style: { fill: \"#161616\", stroke: { width: 3, color: \"#A0A0A0\" } } }\n" +
                "  - { type: shape, shape: ellipse, at: [115, 45], size: [90, 90], style: { fill: \"#36D1DC\" } }\n";
            size = new SKSize(320, 180);
        }
        var theme = "canvas: { orientation: landscape }\nbackground: [{ fill: \"#0A0A0B\" }]\nwidgets:\n  - " + yaml.Trim().Replace("\r\n", "\n").Replace("\n", "\n    ") + "\n";
        return new(module.Name, folder, theme, module, 240, 140, SKRect.Create(-8, -8, size.Width + 16, size.Height + 16), ModuleLibrary.Description(module));
    }
    public void Start() { if (!_disposed) _timer.Start(); }
    public void Stop() => _timer.Stop();
    void Tick(object? sender, EventArgs e) => Refresh();
    public void Refresh()
    {
        if (_disposed) return;
        var values = _values();
        foreach (var tile in Categories.SelectMany(category => category.Tiles)) tile.Render(values, _clock.Elapsed);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Stop(); _timer.Tick -= Tick;
        foreach (var tile in Categories.SelectMany(category => category.Tiles)) tile.Dispose();
    }
}
