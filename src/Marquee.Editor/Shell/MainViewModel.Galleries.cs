using Marquee.Core.Themes.Editing;
using Marquee.Editor.Backgrounds;
using Marquee.Editor.Model;
using Marquee.Editor.Mvvm;
using Marquee.Editor.Resources;

namespace Marquee.Editor.Shell;

public sealed partial class MainViewModel
{
    public event Action<bool>? BackgroundGalleryRequested;
    void InitializeGalleries()
    {
        Layers.BackgroundGalleryCommand = new RelayCommand(() => BackgroundGalleryRequested?.Invoke(false), () => CanEdit);
        Properties.BackgroundGalleryCommand = new RelayCommand(() => BackgroundGalleryRequested?.Invoke(true), () => CanEdit && _workspace?.Session.SelectedLayer is not null);
    }
    public BackgroundGalleryViewModel? CreateBackgroundGallery() => _workspace is { } workspace
        ? new(new BackgroundLibrary(Path.Combine(_environment.InstalledThemes, "backgrounds")), workspace.Session.Values) : null;

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
