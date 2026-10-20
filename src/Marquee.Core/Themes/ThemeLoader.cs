using Marquee.Core.Themes.Parsing;

namespace Marquee.Core.Themes;

/// A theme folder, parsed and with its assets loaded. The render loop owns it once handed over and disposes it.
public sealed class LoadedTheme : IDisposable
{
    internal LoadedTheme(string folder, ThemeSpec spec, ThemeAssets assets, IReadOnlyList<string> warnings)
    {
        Folder = folder;
        Spec = spec;
        Assets = assets;
        Warnings = warnings;
    }

    public string Folder { get; }
    public string Name => Path.GetFileName(Folder);
    public ThemeSpec Spec { get; }
    public ThemeAssets Assets { get; }

    /// Missing images or fonts, with what was used instead. The theme still renders.
    public IReadOnlyList<string> Warnings { get; }

    public bool IsDisposed { get; private set; }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        Assets.Dispose();
    }
}

public static class ThemeLoader
{
    public const string LayoutFileName = "layout.yaml";

    /// Throws ThemeException for a missing folder or an invalid layout. IOException means the file is
    /// locked mid-save; the hot-reload watcher retries those. `fpsOverride` is `--fps`: with the theme's canvas.fps
    /// otherwise, it sets the frame budget a shader layer must fit in to be kept.
    public static LoadedTheme Load(string folder, int? fpsOverride = null) =>
        Load(folder, fpsOverride, ShaderCost.Estimate, VideoCost.Measure);

    internal static LoadedTheme Load(string folder, int? fpsOverride, ShaderProbe probe, VideoProbe videoProbe)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (!Directory.Exists(full))
            throw new ThemeException($"Theme folder {full} does not exist.");
        var layout = Path.Combine(full, LayoutFileName);
        if (!File.Exists(layout))
            throw new ThemeException($"Theme folder {full} has no {LayoutFileName}.");

        var spec = LayoutParser.Parse(File.ReadAllText(layout));
        var warnings = new List<ThemeWarning>();
        var assets = ThemeAssets.Load(full, Shown(spec), warnings, FrameRate(spec.Canvas, fpsOverride), probe, videoProbe);
        return new LoadedTheme(full, spec, assets, warnings.Select(w => w.Message).ToList());
    }

    /// The spec without its hidden layers and widgets (a hidden group's children go with it), which is all the renderer
    /// draws: a hidden clip shouldn't decode, or a hidden shader compile, for nothing. `marquee validate` loads the
    /// whole spec, so a missing file is reported before anyone unhides it. Layers keep their identity, which their
    /// shaders and players are found by.
    static ThemeSpec Shown(ThemeSpec spec) => spec with
    {
        Background = spec.Background.Where(layer => !layer.Hidden).ToList(),
        Widgets = Shown(spec.Widgets),
    };

    static List<WidgetSpec> Shown(IEnumerable<WidgetSpec> widgets) =>
        widgets.Where(widget => !widget.Hidden)
            .Select(widget => widget is GroupWidget group ? group with { Children = Shown(group.Children) } : widget)
            .ToList();

    /// The rate RenderLoop will run a theme at, which sets the frame budget its shader layers must fit in: `--fps` if
    /// given, otherwise the theme's canvas.fps.
    internal static int FrameRate(CanvasSpec canvas, int? fpsOverride) => Math.Clamp(fpsOverride ?? canvas.Fps, 1, CanvasSpec.MaxFps);
}
