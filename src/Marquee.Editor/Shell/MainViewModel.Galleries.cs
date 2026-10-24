using Marquee.Core.Themes.Editing;
using Marquee.Editor.Backgrounds;
using Marquee.Editor.Model;
using Marquee.Editor.Mvvm;
using Marquee.Editor.Resources;
using Marquee.Editor.Properties;

namespace Marquee.Editor.Shell;

public sealed partial class MainViewModel
{
    public event Action<bool>? BackgroundGalleryRequested;
    public event Action? ModuleGalleryRequested;
    public RelayCommand ModuleGalleryCommand { get; private set; } = null!;
    void InitializeGalleries()
    {
        ModuleGalleryCommand = new RelayCommand(() => ModuleGalleryRequested?.Invoke(), () => CanEdit);
        Canvas.ModuleDropped = (module, point) => AddGalleryModule(module, point);
        Layers.BackgroundGalleryCommand = new RelayCommand(() => BackgroundGalleryRequested?.Invoke(false), () => CanEdit);
        Properties.BackgroundGalleryCommand = new RelayCommand(() => BackgroundGalleryRequested?.Invoke(true), () => CanEdit && _workspace?.Session.SelectedLayer is not null);
    }
    public Modules.ModuleGalleryViewModel? CreateModuleGallery() => _workspace is { } workspace
        ? new(workspace.Session.Folder, () => workspace.Values.Snapshot(workspace.Values.KnownKeys().Concat(ModuleLibrary.Keys)))
            { BlankWidgets = [.. WidgetChoices(), .. FileChoices()] } : null;

    public void AddGalleryModule(Module module, SkiaSharp.SKPoint? point = null)
    {
        if (!CanEdit || _workspace is not { } workspace) return;
        var session = workspace.Session;
        var local = point is { } position ? CanvasGeometry.Of(session).ToLocal(position, Canvas.Group) : (SkiaSharp.SKPoint?)null;
        if (!module.NeedsFile) { Run(new AddModule(module, local, Canvas.Group)); return; }
        var kind = module.Id == "video" ? AssetKind.Video : AssetKind.Image;
        if (_environment.Dialogs.PickImportFile(kind) is not { } file) return;
        try
        {
            new AssetImport(session.Folder).AddModule(session, module, file, kind, local, Canvas.Group);
            PropertyChoices.Forget();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or AssetImportException or Marquee.Core.Themes.ThemeException or LayoutEditException)
        { _environment.Dialogs.ShowError(Strings.ImportRefused(module.Name, ex.Message)); }
    }
    public BackgroundGalleryViewModel? CreateBackgroundGallery() => _workspace is { } workspace
        ? new(new BackgroundLibrary(Path.Combine(_environment.InstalledThemes, "backgrounds")), () => workspace.Values.Snapshot(["cpu.load"])) : null;

    public void ChooseBackground(BackgroundEntry? entry, string? still, bool replace)
    {
        if (!CanEdit || _workspace is not { } workspace) return;
        var layer = replace ? workspace.Session.SelectedLayer : null;
        try
        {
            if (entry is not null) BackgroundLibrary.Use(workspace.Session, entry.File, layer);
            else if (still == "Gradient") Run(new GalleryGradient(layer, AddLayer.AddIndex(workspace.Session)));
            else if (Enum.TryParse<LayerKindChoice>(still, out var kind))
            {
                if (replace) ChangeBackgroundKind(kind); else AddBackground(kind);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or Marquee.Core.Themes.ThemeException or LayoutEditException)
        { _environment.Dialogs.ShowError(Strings.ImportRefused(entry?.Info.Name ?? Strings.BackgroundGallery, ex.Message)); }
    }
    sealed record GalleryGradient(YamlPath? Replace, int Index) : EditorCommand
    {
        public override string Label => Strings.StillGradient;
        public override CommandResult Run(CommandContext context)
        {
            var result = Replace is { } layer ? new ChangeLayerKind(layer, LayerKindChoice.Fill, null).Run(context)
                : new AddLayer(LayerKindChoice.Fill, null, Index).Run(context);
            var path = Replace ?? YamlPath.Root.Child("background").Item(Index);
            return new(result.Document.ReplaceValue(path.Child("fill"), "{ linear: [\"#0A0A0B\", \"#36D1DC\"] }"), [path]);
        }
    }
}
