using Marquee.Core.Config;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes.Schema;
using Marquee.Editor.Canvas;
using Marquee.Editor.Model;
using Marquee.Editor.Mvvm;
using Marquee.Editor.Resources;
using Marquee.Host;

namespace Marquee.Editor.Shell;

/// The toolbar (spec Part B, Toolbar): the theme picker and "New from an example", Add widget, undo and redo, align
/// and distribute, zoom, and "Show on screen"; "Revert to opened version", in the banner and the theme menu; and the
/// banner's "Make a copy to edit".
public sealed partial class MainViewModel
{
    public RelayCommand UndoCommand { get; private set; } = null!;
    public RelayCommand RedoCommand { get; private set; } = null!;
    public RelayCommand RevertCommand { get; private set; } = null!;
    public RelayCommand MakeCopyCommand { get; private set; } = null!;
    public RelayCommand KeepEditingCommand { get; private set; } = null!;
    public RelayCommand SaveToFolderCommand { get; private set; } = null!;

    /// Its parameter is a LayoutProblem: the widget it is in is selected.
    public RelayCommand SelectProblemCommand { get; private set; } = null!;

    public RelayCommand ShowOnScreenCommand { get; private set; } = null!;

    /// Its parameter is a ZoomMode.
    public RelayCommand ZoomCommand { get; private set; } = null!;

    /// Its parameter is a widget type.
    public RelayCommand AddWidgetCommand { get; private set; } = null!;

    /// Its parameter is an Alignment or a Distribution.
    public RelayCommand ArrangeCommand { get; private set; } = null!;

    /// Its parameter is a theme folder.
    public RelayCommand OpenThemeCommand { get; private set; } = null!;

    /// Its parameter is an example's folder.
    public RelayCommand NewFromExampleCommand { get; private set; } = null!;

    bool CanEdit => Canvas.CanEdit && !Canvas.IsDragging;

    int SelectionCount => _workspace?.Session.Selection.Count ?? 0;

    /// The widget types, as Add widget lists them: the catalog's, each added from its example.
    public IReadOnlyList<MenuChoice> WidgetChoices() =>
        ThemeSchema.Widgets.Variants.Select(type => new MenuChoice(type.Title, AddWidgetCommand, WidgetTip(type.Name), type.Name)).ToList();

    static string WidgetTip(string type) => type switch
    {
        "text" => Strings.AddTextWidgetTip,
        "value" => Strings.AddValueWidgetTip,
        "bar" => Strings.AddBarWidgetTip,
        "gauge" => Strings.AddGaugeWidgetTip,
        "graph" => Strings.AddGraphWidgetTip,
        "image" => Strings.AddImageWidgetTip,
        "video" => Strings.AddVideoWidgetTip,
        "shape" => Strings.AddShapeWidgetTip,
        "donut" => Strings.AddDonutWidgetTip,
        "group" => Strings.AddGroupWidgetTip,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "A widget needs its own tooltip."),
    };

    /// The align menu: the six alignments, then a null for a separator, then the two distributions.
    public IReadOnlyList<MenuChoice?> ArrangeChoices() =>
    [
        new(Strings.AlignLeft, ArrangeCommand, Strings.AlignLeftTip, Alignment.Left),
        new(Strings.AlignCenter, ArrangeCommand, Strings.AlignCenterTip, Alignment.Center),
        new(Strings.AlignRight, ArrangeCommand, Strings.AlignRightTip, Alignment.Right),
        new(Strings.AlignTop, ArrangeCommand, Strings.AlignTopTip, Alignment.Top),
        new(Strings.AlignMiddle, ArrangeCommand, Strings.AlignMiddleTip, Alignment.Middle),
        new(Strings.AlignBottom, ArrangeCommand, Strings.AlignBottomTip, Alignment.Bottom),
        null,
        new(Strings.DistributeHorizontally, ArrangeCommand, Strings.DistributeHorizontallyTip, Distribution.Horizontal),
        new(Strings.DistributeVertically, ArrangeCommand, Strings.DistributeVerticallyTip, Distribution.Vertical),
    ];

    /// The theme picker: the user's themes, then the installed examples, as the tray's Themes menu lists them.
    public IReadOnlyList<IReadOnlyList<MenuChoice>> ThemeChoices() =>
        ThemeCatalog.List([_environment.UserThemes, _environment.InstalledThemes])
            .Select(group => (IReadOnlyList<MenuChoice>)group.Select(theme => new MenuChoice(theme.Name, OpenThemeCommand, Strings.OpenThemeTip(theme.Name), theme.Folder)).ToList())
            .ToList();

    /// "New from an example": the installed examples.
    public IReadOnlyList<MenuChoice> ExampleChoices() =>
        ThemeCatalog.List([_environment.InstalledThemes])[0]
            .Select(theme => new MenuChoice(theme.Name, NewFromExampleCommand, Strings.CopyExampleTip(theme.Name), theme.Folder))
            .ToList();

    /// Writes this theme into config.yaml's `theme:`, and the tray switches to it.
    public void ShowOnScreen()
    {
        if (_workspace is not { } workspace) return;
        try
        {
            _screen.Show(workspace.Session.Folder);
            Report(Strings.ShownOnScreen(workspace.Name));
        }
        catch (Exception ex) when (ex is ConfigException or IOException or UnauthorizedAccessException)
        {
            Report(Strings.ShowOnScreenFailed(ex.Message));
        }
    }

    public void Undo() => Step(Strings.Undo, session => session.Undo());

    public void Redo() => Step(Strings.Redo, session => session.Redo());

    void InitializeCommands()
    {
        UndoCommand = new RelayCommand(Undo, () => !IsReadOnly && !Canvas.IsDragging && _workspace?.Session.CanUndo == true);
        RedoCommand = new RelayCommand(Redo, () => !IsReadOnly && !Canvas.IsDragging && _workspace?.Session.CanRedo == true);
        // Not only from the paused banner: the theme menu has it whenever there is an opened version to go back to.
        RevertCommand = new RelayCommand(Revert, () => _workspace is { Backup: not null } && !Canvas.IsDragging);
        MakeCopyCommand = new RelayCommand(() => CopyAndOpen(_workspace!.Session.Folder), () => CanMakeCopy);
        KeepEditingCommand = new RelayCommand(KeepEditing, () => CanKeepEditing);
        SaveToFolderCommand = new RelayCommand(SaveToFolder, () => CanSaveToFolder);
        SelectProblemCommand = new RelayCommand(problem => SelectProblem((LayoutProblem)problem!), problem => problem is LayoutProblem { Widget: not null });
        ShowOnScreenCommand = new RelayCommand(ShowOnScreen, () => _workspace is not null);
        ZoomCommand = new RelayCommand(mode => Canvas.ZoomMode = (ZoomMode)mode!, mode => mode is ZoomMode);
        AddWidgetCommand = new RelayCommand(type => Run(new AddWidget((string)type!, Canvas.Group)), type => type is string && CanEdit);
        ArrangeCommand = new RelayCommand(Arrange, how => CanEdit && SelectionCount >= (how is Distribution ? 3 : 2));
        OpenThemeCommand = new RelayCommand(folder => OpenTheme((string)folder!), folder => folder is string);
        NewFromExampleCommand = new RelayCommand(folder => NewFromExample((string)folder!), folder => folder is string);
        InitializeFileCommands();
    }

    void RefreshCommands()
    {
        UndoCommand.Refresh();
        RedoCommand.Refresh();
        RevertCommand.Refresh();
        MakeCopyCommand.Refresh();
        KeepEditingCommand.Refresh();
        AddWidgetCommand.Refresh();
        ArrangeCommand.Refresh();
        RefreshFileCommands();
        Layers.RefreshCommands();
    }

    void Arrange(object? how)
    {
        if (_workspace is not { } workspace) return;
        var selection = workspace.Session.Selection;
        switch (how)
        {
            case Alignment alignment:
                Run(new AlignWidgets(selection, alignment));
                break;
            case Distribution distribution:
                Run(new DistributeWidgets(selection, distribution));
                break;
        }
    }

    /// "Revert to opened version", from the banner or the theme menu: asked first, since it discards the changes made
    /// since the theme opened (one undo step, so undo brings them back).
    void Revert()
    {
        if (_workspace is not { Backup: { } backup } workspace || !_environment.Dialogs.AskRevert(workspace.Name)) return;
        Run(backup.Revert());
    }

    /// "Save to a folder…", the theme's folder having gone: writes the text into a folder the user picks, then opens the
    /// theme from there in place of the one that went.
    /// A layout.yaml already in that folder is replaced only when the user says so.
    public void SaveToFolder()
    {
        var dialogs = _environment.Dialogs;
        if (_workspace is not { } workspace || dialogs.PickSaveFolder() is not { } folder) return;
        try
        {
            if (workspace.SaveTo(folder) is null && (!dialogs.AskReplaceLayout(folder) || workspace.SaveTo(folder, replace: true) is null))
                return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report(Strings.SaveToFolderFailed(ex.Message));
            return;
        }

        OpenTheme(folder);
    }

    /// Selects the widget a problem is in, when it is still a widget of the theme drawn.
    void SelectProblem(LayoutProblem problem)
    {
        if (_workspace?.Session is not { } session || problem.Widget is not { } widget || session.Widget(widget) is null) return;
        session.Select([widget]);
    }

    /// Undo or redo: whole texts swapped back, which the session refuses only during a drag, or when the theme's files
    /// can't be read.
    void Step(string what, Func<EditorSession, bool> step)
    {
        if (_workspace is not { } workspace || workspace.IsReadOnly || Canvas.IsDragging) return;
        try
        {
            step(workspace.Session);
            Message = null;
        }
        catch (Exception ex) when (ex is ThemeException or LayoutEditException or InvalidOperationException or IOException
                                       or UnauthorizedAccessException)
        {
            Report(Strings.ActionFailed(what, ex.Message));
        }
    }
}
