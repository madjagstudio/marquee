using System.Windows.Input;
using Marquee.Core.Themes.Editing;
using Marquee.Editor.Canvas;
using Marquee.Editor.Model;
using Marquee.Editor.Mvvm;
using Marquee.Editor.Resources;

namespace Marquee.Editor.Layers;

/// The layers panel, over LayersModel:
/// - widgets front to back, indented under folding groups, then background layers with their own add menu;
/// - selection shared with the canvas: a click selects, Ctrl-click adds or removes, Shift-click takes the run of rows
///   between; and the canvas's selection shows here;
/// - rename in place (writes `name:`), hide and lock (write `hidden:` and `locked:`);
/// - drag to reorder and into or out of groups (LayersModel.Drop, then MoveLayer);
/// - the menu: group, ungroup, copy, paste, duplicate and delete, shared with the canvas's keys.
/// Rows of widgets that use an anchor or alias are shown read-only.
///
/// Made and used on the UI thread.
public sealed class LayersViewModel : ObservableObject
{
    readonly Func<EditorCommand, bool> _run;
    readonly CanvasViewModel _canvas;
    readonly Action<string> _report;
    readonly HashSet<YamlPath> _expanded = [];
    EditorSession? _session;
    LayersModel? _model;
    LayerItem? _anchor;
    IReadOnlyList<LayerItem> _widgets = [];
    IReadOnlyList<LayerItem> _background = [];

    public LayersViewModel(Func<EditorCommand, bool> run, CanvasViewModel canvas, Action<string> report,
        Action<LayerKindChoice> addLayer, Action delete)
    {
        _run = run;
        _canvas = canvas;
        _report = report;
        GroupCommand = new RelayCommand(_canvas.GroupSelection, () => CanEdit && _canvas.HasSelection);
        UngroupCommand = new RelayCommand(_canvas.UngroupSelection, () => CanEdit && SelectedGroups);
        CopyCommand = new RelayCommand(_canvas.CopySelection, () => _canvas.HasSelection);
        PasteCommand = new RelayCommand(_canvas.Paste, () => CanEdit && _canvas.CanPaste);
        DuplicateCommand = new RelayCommand(_canvas.DuplicateSelection, () => CanEdit && _canvas.HasSelection);
        DeleteCommand = new RelayCommand(delete, () => CanEdit && (_canvas.HasSelection || _session?.SelectedLayer is not null));
        AddFillLayerCommand = new RelayCommand(() => addLayer(LayerKindChoice.Fill), () => CanEdit);
        AddImageLayerCommand = new RelayCommand(() => addLayer(LayerKindChoice.Image), () => CanEdit);
        AddVideoLayerCommand = new RelayCommand(() => addLayer(LayerKindChoice.Video), () => CanEdit);
        AddShaderLayerCommand = new RelayCommand(() => addLayer(LayerKindChoice.Shader), () => CanEdit);
        RenameCommand = new RelayCommand(item => BeginRename((LayerItem)item!), item => item is LayerItem { IsBackground: false } layer && CanChange(layer));
    }

    /// The widget rows shown, front to back, a folded group's children left out.
    public IReadOnlyList<LayerItem> Widgets
    {
        get => _widgets;
        private set => Set(ref _widgets, value);
    }

    /// The background layers, front to back.
    public IReadOnlyList<LayerItem> Background
    {
        get => _background;
        private set
        {
            if (Set(ref _background, value)) Raise(nameof(HasBackground));
        }
    }

    public bool HasBackground => Background.Count > 0;

    /// Whether the panel may change the theme: not read-only, not paused, not mid-drag on the canvas.
    public bool CanEdit => _canvas.CanEdit && !_canvas.IsDragging;

    public RelayCommand GroupCommand { get; }

    public RelayCommand UngroupCommand { get; }

    public RelayCommand CopyCommand { get; }

    public RelayCommand PasteCommand { get; }

    public RelayCommand DuplicateCommand { get; }

    public RelayCommand DeleteCommand { get; }

    public RelayCommand AddFillLayerCommand { get; }
    public RelayCommand AddImageLayerCommand { get; }
    public RelayCommand AddVideoLayerCommand { get; }
    public RelayCommand AddShaderLayerCommand { get; }
    public RelayCommand? BackgroundGalleryCommand { get; set; }

    /// Selects a layer by its YAML index (the rows display front to back).
    public void SelectBackground(int index)
    {
        if (Background.FirstOrDefault(row => row.Path.Last!.Value.Index == index) is { } item)
            Click(item, ModifierKeys.None);
    }

    /// Its parameter is the LayerItem to rename.
    public RelayCommand RenameCommand { get; }

    /// The row for `path`, shown or folded away; null when there is none.
    public LayerItem? Find(YamlPath path) => Widgets.Concat(Background).FirstOrDefault(item => item.Path == path);

    public void Attach(EditorSession session)
    {
        Detach();
        _session = session;
        _expanded.Clear();
        session.Changed += OnSessionChanged;
        Rebuild();
    }

    public void Detach()
    {
        if (_session is null) return;
        _session.Changed -= OnSessionChanged;
        _session = null;
        Rebuild();
    }

    /// A click on `item` with `modifiers` held: selects it alone; with Ctrl, adds or removes it; with Shift, selects
    /// the rows from the last one clicked to it. A background layer is always selected on its own.
    public void Click(LayerItem item, ModifierKeys modifiers)
    {
        if (_session is not { } session) return;
        if (item.IsBackground)
        {
            _anchor = null;
            Select([item.Path]);
            return;
        }
        var selection = session.SelectedLayer is null ? session.Selection : [];
        IReadOnlyList<YamlPath> chosen;
        if (modifiers.HasFlag(ModifierKeys.Shift) && _anchor is { } anchor && Widgets.Contains(anchor))
        {
            var (from, to) = (IndexOf(anchor), IndexOf(item));
            chosen = Widgets.Skip(Math.Min(from, to)).Take(Math.Abs(to - from) + 1).Select(row => row.Path).ToList();
        }
        else if (modifiers.HasFlag(ModifierKeys.Control))
        {
            chosen = selection.Contains(item.Path) ? selection.Where(path => path != item.Path).ToList() : [.. selection, item.Path];
            _anchor = item;
        }
        else
        {
            chosen = [item.Path];
            _anchor = item;
        }

        Select(chosen);
    }

    /// A right-click: the row joins the selection unless it is in it already, so the menu works on what was clicked.
    public void ContextClick(LayerItem item)
    {
        if (_session is { } session && !session.Selection.Contains(item.Path)) Click(item, ModifierKeys.None);
    }

    /// Folds or unfolds a group's children.
    public void ToggleExpanded(LayerItem group)
    {
        if (!group.IsGroup) return;
        if (!_expanded.Remove(group.Path)) _expanded.Add(group.Path);
        Rebuild();
    }

    /// F2 or a double-click on the name: the rename box opens with the name in it.
    public void BeginRename(LayerItem item)
    {
        if (item.IsBackground || !CanChange(item)) return;
        foreach (var other in Widgets) other.IsRenaming = false;
        item.EditName = item.Name;
        item.IsRenaming = true;
    }

    /// Enter: writes the name typed as `name:`; an empty one removes it, so a made-up name shows again. A made-up
    /// name left as it was isn't written.
    public bool CommitRename(LayerItem item)
    {
        if (!item.IsRenaming) return false;
        item.IsRenaming = false;
        var name = item.EditName.Trim();
        if (name == item.Name.Trim() && (item.IsNamed || name.Length > 0)) return false;
        return _run(new RenameLayer(item.Path, name));
    }

    /// Esc: the rename box closes, and nothing is written.
    public void CancelRename(LayerItem item) => item.IsRenaming = false;

    /// The eye: hides or shows the widget or background layer.
    public bool SetHidden(LayerItem item, bool hidden) =>
        item.Hidden != hidden && CanChange(item) && _run(new SetLayerFlag([item.Path], LayerFlag.Hidden, hidden));

    /// The lock: locks or unlocks it.
    public bool SetLocked(LayerItem item, bool locked) =>
        item.Locked != locked && CanChange(item) && _run(new SetLayerFlag([item.Path], LayerFlag.Locked, locked));

    /// Whether `item` could be dropped `place` the row `target`: the move LayersModel works out, or null for a drop that
    /// goes nowhere.
    public MoveLayer? DropFor(LayerItem item, LayerItem target, DropPlace place)
    {
        if (!CanEdit || !item.CanMove || item.IsBackground != target.IsBackground) return null;
        if (!item.IsBackground) return _model?.Drop(item.Path, target.Path, place);
        if (place == DropPlace.Into || item.Path == target.Path) return null;
        var from = item.Path.Last!.Value.Index;
        var at = target.Path.Last!.Value.Index;
        if (from < at) at--;
        var index = place == DropPlace.Above ? at + 1 : at;
        return index == from ? null : new MoveLayer(item.Path, index);
    }

    /// Drops `item` `place` the row `target`, as one undo step. False when the drop goes nowhere.
    public bool Drop(LayerItem item, LayerItem target, DropPlace place) =>
        DropFor(item, target, place) is { } move && _run(move);

    /// Where a drop lands on a row `height` high, at `y` from its top: the top quarter above it, the bottom quarter
    /// below, the middle into a group; a widget's row splits at its middle.
    public static DropPlace PlaceAt(double y, double height, bool isGroup)
    {
        if (!isGroup) return y < height / 2 ? DropPlace.Above : DropPlace.Below;
        if (y < height / 4) return DropPlace.Above;
        return y > height * 3 / 4 ? DropPlace.Below : DropPlace.Into;
    }

    /// Asks the commands again whether they can run: the selection or the clipboard changed.
    public void RefreshCommands()
    {
        GroupCommand.Refresh();
        UngroupCommand.Refresh();
        CopyCommand.Refresh();
        PasteCommand.Refresh();
        DuplicateCommand.Refresh();
        DeleteCommand.Refresh();
        RenameCommand.Refresh();
        AddFillLayerCommand.Refresh();
        AddImageLayerCommand.Refresh();
        AddVideoLayerCommand.Refresh();
        AddShaderLayerCommand.Refresh();
    }

    bool SelectedGroups => _session is { } session && session.Selection.Any(path => session.Widget(path)?.IsGroup == true);

    bool CanChange(LayerItem item) => CanEdit && item.IsEditable;

    int IndexOf(LayerItem item)
    {
        for (var i = 0; i < Widgets.Count; i++)
            if (ReferenceEquals(Widgets[i], item))
                return i;
        return -1;
    }

    void Select(IReadOnlyList<YamlPath> paths)
    {
        if (_session is not { } session) return;
        try
        {
            session.Select(paths);
        }
        catch (ArgumentException ex)
        {
            _report(Strings.ActionFailed(Strings.ActionSelect, ex.Message));
        }
    }

    void OnSessionChanged(object? sender, SessionChanges changes)
    {
        // A widget selected on the canvas inside a folded group unfolds it, so the selection shows here.
        var unfolded = Unfold();
        if (unfolded || changes.HasFlag(SessionChanges.Theme) || changes.HasFlag(SessionChanges.Document)) Rebuild();
        else if (changes.HasFlag(SessionChanges.Selection)) ShowSelection();
        Raise(nameof(CanEdit));
        RefreshCommands();
    }

    /// Unfolds the groups the selected widgets are in; true when one was folded.
    bool Unfold()
    {
        var unfolded = false;
        foreach (var path in _session?.Selection ?? [])
            for (var group = CanvasGeometry.GroupOf(path); group is not null; group = CanvasGeometry.GroupOf(group))
                unfolded |= _expanded.Add(group);
        return unfolded;
    }

    void Rebuild()
    {
        _model = _session is { } session ? LayersModel.Of(session) : null;
        var rows = new List<LayerItem>();
        if (_model is not null) Add(rows, _model.Widgets, 0);
        Widgets = rows;
        Background = _model?.Background.Select(node => new LayerItem(node, 0, isExpanded: false)).ToList() ?? [];
        if (_anchor is not null) _anchor = rows.FirstOrDefault(row => row.Path == _anchor.Path);
        ShowSelection();
    }

    void Add(List<LayerItem> rows, IReadOnlyList<LayerNode> nodes, int depth)
    {
        foreach (var node in nodes)
        {
            var expanded = _expanded.Contains(node.Path);
            rows.Add(new LayerItem(node, depth, expanded));
            if (node.Kind == LayerKind.Group && expanded) Add(rows, node.Children, depth + 1);
        }
    }

    void ShowSelection()
    {
        var selected = _session?.Selection.ToHashSet() ?? [];
        foreach (var row in Widgets.Concat(Background)) row.IsSelected = selected.Contains(row.Path);
    }
}
