using Marquee.Core.Themes.Editing;
using Marquee.Editor.Canvas;
using Marquee.Editor.Model;
using Marquee.Editor.Mvvm;
using Marquee.Editor.Resources;

namespace Marquee.Editor.Styles;

/// Named styles and their direct users, sharing the session's selection and undo history.
public sealed class StylesViewModel : ObservableObject
{
    readonly Func<EditorCommand, bool> _run;
    readonly CanvasViewModel _canvas;
    readonly Action<string> _report;
    EditorSession? _session;
    IReadOnlyList<StyleItem> _items = [];

    public StylesViewModel(Func<EditorCommand, bool> run, CanvasViewModel canvas, Action<string> report)
    {
        _run = run;
        _canvas = canvas;
        _report = report;
        NewCommand = new RelayCommand(New, () => CanEdit);
        RenameCommand = new RelayCommand(() => BeginRename(Selected!), () => CanEdit && Selected is not null);
        DeleteCommand = new RelayCommand(Delete, () => CanEdit && Selected is not null);
    }

    public IReadOnlyList<StyleItem> Items { get => _items; private set => Set(ref _items, value); }
    public StyleItem? Selected => Items.FirstOrDefault(item => item.Name == _session?.SelectedStyle);
    public bool CanEdit => _session is not null && _canvas.CanEdit && !_canvas.IsDragging;
    public RelayCommand NewCommand { get; }
    public RelayCommand RenameCommand { get; }
    public RelayCommand DeleteCommand { get; }

    public void Attach(EditorSession session)
    {
        Detach();
        _session = session;
        session.Changed += Changed;
        Refresh();
    }

    public void Detach()
    {
        if (_session is { } session) session.Changed -= Changed;
        _session = null;
        Refresh();
    }

    public void Select(string name)
    {
        if (_session is { } session && Items.Any(item => item.Name == name))
            session.Select([YamlPath.Root.Child("styles").Child(name)]);
    }

    public static string FreeName(LayoutDocument document)
    {
        var name = "style";
        for (var n = 2; StyleCommands.StyleNameProblem(document, name) is not null; n++) name = $"style-{n}";
        return name;
    }

    void New()
    {
        if (_session is { } session && _run(new NewStyle(FreeName(session.Document))) && Selected is { } item)
            BeginRename(item);
    }

    public void BeginRename(StyleItem item)
    {
        if (!CanEdit || !Items.Contains(item)) return;
        Select(item.Name);
        foreach (var other in Items) other.IsRenaming = false;
        item.EditName = item.Name;
        item.IsRenaming = true;
    }

    public bool CommitRename(StyleItem item)
    {
        if (!CanEdit || !Items.Contains(item) || !item.IsRenaming) return false;
        item.Validate();
        if (item.Problem is not null) return false;
        if (item.EditName.Trim() == item.Name)
        {
            CancelRename(item);
            return false;
        }
        // Close before applying: the session change can remove this row while its textbox loses focus.
        item.IsRenaming = false;
        if (_run(new RenameStyle(item.Name, item.EditName.Trim()))) return true;
        item.IsRenaming = true;
        return false;
    }

    public void CancelRename(StyleItem item) => item.IsRenaming = false;

    void Delete()
    {
        if (Selected is not { } item) return;
        var command = new DeleteStyle(item.Name);
        if (_run(command) && command.Copied > 0) _report(Strings.StyleCopiedInto(item.Name, command.Copied));
    }

    void Changed(object? sender, SessionChanges changes) => Refresh();

    void Refresh()
    {
        var names = _session?.Document.GetNode(YamlPath.Root.Child("styles")) is { Kind: LayoutNodeKind.Mapping } styles
            ? styles.Keys : [];
        if (!Items.Select(item => item.Name).SequenceEqual(names))
            Items = names.Select(name => Items.FirstOrDefault(item => item.Name == name)
                ?? new StyleItem(name, candidate => _session is { } session
                    ? StyleCommands.StyleNameProblem(session.Document, candidate, name) : null)).ToList();
        foreach (var item in Items)
        {
            item.IsSelected = item.Name == _session?.SelectedStyle;
            if (!item.IsSelected) item.IsRenaming = false;
            item.UsedBy = Strings.StyleUsedBy(StyleUsers.Count(_session!.Document, item.Name));
            item.Validate();
        }
        Raise(nameof(Selected));
        Raise(nameof(CanEdit));
        NewCommand.Refresh();
        RenameCommand.Refresh();
        DeleteCommand.Refresh();
    }
}

public sealed class StyleItem(string name, Func<string, string?> problem) : ObservableObject
{
    string _editName = name;
    string _usedBy = "";
    string? _problem;
    bool _isRenaming;
    bool _isSelected;

    public string Name { get; } = name;
    public string UsedBy { get => _usedBy; internal set => Set(ref _usedBy, value); }
    public bool IsSelected { get => _isSelected; internal set => Set(ref _isSelected, value); }
    public bool IsRenaming { get => _isRenaming; internal set => Set(ref _isRenaming, value); }
    public string EditName
    {
        get => _editName;
        set { if (Set(ref _editName, value)) Validate(); }
    }
    public string? Problem { get => _problem; private set { if (Set(ref _problem, value)) Raise(nameof(HasProblem)); } }
    public bool HasProblem => Problem is not null;
    internal void Validate() => Problem = problem(EditName.Trim());
}
