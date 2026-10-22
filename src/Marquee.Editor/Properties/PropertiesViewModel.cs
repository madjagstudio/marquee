using System.Globalization;
using Marquee.Core.Expressions;
using Marquee.Core.Sensors;
using Marquee.Editor.Canvas;
using Marquee.Editor.Model;
using Marquee.Editor.Model.Values;
using Marquee.Editor.Mvvm;
using Marquee.Editor.Resources;
using Marquee.Editor.Session;
using Marquee.Editor.Styles;

namespace Marquee.Editor.Properties;

/// The properties panel: the selected widgets' keys from the catalog (PropertyModel), then the Style section in its
/// groups, with the named style the widget uses and "Clear overrides", editable rules, "Split into spans", and
/// copy and paste style. Each row is a PropertyRowViewModel, whose control writes through PropertyModel as one undo
/// step.
///
/// The rows follow the session: after each change the model is built again, and when the same rows come back (the
/// same selection, keys and items, as after a value is typed or a slider moves) each row takes its new value in place,
/// so a control being dragged or typed in is never replaced under the mouse. Otherwise the rows are made afresh.
///
/// Made and used on the UI thread.
public sealed class PropertiesViewModel : ObservableObject
{
    readonly Func<EditorCommand, bool> _run;
    readonly Action<string> _report;
    readonly CanvasViewModel _canvas;
    readonly IEditorDialogs _dialogs;
    readonly Func<string, bool> _orientation;
    readonly Action<LayerKindChoice> _changeKind;
    ShaderPanelViewModel? _shader;
    EditorWorkspace? _workspace;
    PropertyModel? _model;
    string _shape = "";
    Dictionary<string, PropertyRowViewModel> _byName = [];
    IReadOnlyList<PropertyRowViewModel> _rows = [];
    IReadOnlyList<PropertySectionViewModel> _sections = [];
    IReadOnlyList<string> _rules = [];
    string _heading = "";
    string? _styleLabel;
    string? _styleChoice;
    bool _hasSelection;
    bool _nested;
    readonly string? _configFile;
    public RulesViewModel? RulesList { get; private set; }
    public RingViewModel? Ring { get; private set; }

    public PropertiesViewModel(Func<EditorCommand, bool> run, Action<string> report, CanvasViewModel canvas, IEditorDialogs dialogs,
        Func<string, bool> orientation, Action<LayerKindChoice> changeKind, string? configFile = null)
    {
        _run = run;
        _report = report;
        _canvas = canvas;
        _dialogs = dialogs;
        _orientation = orientation;
        _changeKind = changeKind;
        _configFile = configFile;
        ClearOverridesCommand = new RelayCommand(() => Write(() => Model!.ClearOverrides()), () => CanEdit && Model?.StyleUse is { Name: not null, Count: > 0 });
        SplitIntoSpansCommand = new RelayCommand(() => Write(() => Model!.SplitIntoSpans()), () => CanEdit && Model?.CanSplitIntoSpans == true);
        CopyStyleCommand = new RelayCommand(() => _canvas.CopyStyle(), () => HasSelection);
        PasteStyleCommand = new RelayCommand(() => _canvas.PasteStyle(), () => CanEdit && HasSelection && _canvas.HasCopiedStyle);
        SaveAsStyleCommand = new RelayCommand(SaveAsStyle, () => CanEdit && Model is { Target: PropertyTargetKind.Widgets, Widgets.Count: 1 });
    }

    public EditorSession? Session => _workspace?.Session;

    /// The panel's model for the selection now; null with no theme open.
    public PropertyModel? Model => _model;

    public PropertyTargetKind? Target => Model?.Target;
    public bool IsLayer => Target == PropertyTargetKind.Layer;
    public bool IsWidgets => Target == PropertyTargetKind.Widgets;
    public bool IsStyle => Target == PropertyTargetKind.Style;
    public bool HasProperties => Model is not null;
    public ShaderPanelViewModel? Shader
    {
        get => _shader;
        private set => Set(ref _shader, value);
    }

    public IReadOnlyList<LayerKindOption> KindChoices { get; } =
    [
        new(LayerKindChoice.Fill, Strings.LayerFill), new(LayerKindChoice.Image, Strings.LayerImage),
        new(LayerKindChoice.Video, Strings.LayerVideo), new(LayerKindChoice.Shader, Strings.LayerShader),
    ];

    public LayerKindChoice? Kind
    {
        get => IsLayer && Enum.TryParse<LayerKindChoice>(Model?.Type, true, out var kind) ? kind : null;
        set
        {
            if (value is not { } kind || value == Kind || !CanEdit) return;
            _changeKind(kind);
            Raise();
        }
    }

    public void SetChoice(string name, string value)
    {
        if (Row(name) is { } row) row.Choice = value;
    }

    internal bool SetOrientation(string orientation)
    {
        EndDrag();
        return _orientation(orientation);
    }

    /// Widgets are selected, so widget-only commands may run.
    public bool HasSelection
    {
        get => _hasSelection;
        private set => Set(ref _hasSelection, value);
    }

    /// The widget's name, or how many widgets are selected.
    public string Heading
    {
        get => _heading;
        private set => Set(ref _heading, value);
    }

    /// Whether the panel may write: a theme that can be saved, not paused.
    public bool CanEdit => _canvas.CanEdit && !_canvas.IsDragging;

    /// The widget keys, in the catalog's order.
    public IReadOnlyList<PropertyRowViewModel> Rows
    {
        get => _rows;
        private set => Set(ref _rows, value);
    }

    /// The Style section's groups: Text, Colour and fill, Outline, Effects.
    public IReadOnlyList<PropertySectionViewModel> Sections
    {
        get => _sections;
        private set => Set(ref _sections, value);
    }

    /// "meter + 2 overrides", for one widget; null for several.
    public string? StyleLabel
    {
        get => _styleLabel;
        private set => Set(ref _styleLabel, value);
    }

    /// The style dropdown: no named style, then the theme's named styles.
    public IReadOnlyList<string> StyleChoices { get; private set; } = [];

    /// The named style the widgets start from, as the dropdown shows it; setting it picks another (PickStyle), keeping
    /// what the widgets set over it.
    public string? StyleChoice
    {
        get => _styleChoice;
        set
        {
            if (value is null || value == _styleChoice || Model is not { } model) return;
            var name = value == Strings.NoNamedStyle ? null : value;
            if (!Write(() => model.PickStyle(name))) Raise();
        }
    }

    /// Plain-language summaries, retained for consumers that don't show the editable RulesList.
    public IReadOnlyList<string> Rules
    {
        get => _rules;
        private set
        {
            if (Set(ref _rules, value)) Raise(nameof(HasRules));
        }
    }

    public bool HasRules => Rules.Count > 0;

    public RelayCommand ClearOverridesCommand { get; }

    public RelayCommand SplitIntoSpansCommand { get; }

    public RelayCommand CopyStyleCommand { get; }

    public RelayCommand PasteStyleCommand { get; }
    public RelayCommand SaveAsStyleCommand { get; }

    void SaveAsStyle()
    {
        if (Session is not { } session || Model is not { Widgets.Count: 1 } model) return;
        var name = _dialogs.AskStyleName(StylesViewModel.FreeName(session.Document),
            candidate => StyleCommands.StyleNameProblem(session.Document, candidate.Trim()));
        if (name is not null) Write(() => new SaveAsStyle(model.Widgets[0].Path, name.Trim()));
    }

    internal IEditorDialogs Dialogs => _dialogs;

    internal EditorWorkspace? Workspace => _workspace;

    /// The row named `name` (as PropertyModel names rows), anywhere in the panel; null when there is none.
    public PropertyRowViewModel? Row(string name) => _byName.GetValueOrDefault(name);

    public void Attach(EditorWorkspace workspace)
    {
        Detach();
        _workspace = workspace;
        workspace.Session.Changed += OnSessionChanged;
        workspace.Values.Updated += OnValuesUpdated;
        PropertyChoices.Forget();
        Refresh(rebuild: true);
    }

    public void Detach()
    {
        if (_workspace is not { } workspace) return;
        // A drag cut short by closing the theme: what it made is saved.
        EndDrag();
        workspace.Session.Changed -= OnSessionChanged;
        workspace.Values.Updated -= OnValuesUpdated;
        _workspace = null;
        Shader?.Dispose();
        Shader = null;
        Refresh(rebuild: true);
    }

    /// Applies a command made by `make`, which may refuse with PropertyValueException; reports the refusal. True when it
    /// went through.
    internal bool Write(Func<EditorCommand> make)
    {
        if (!CanEdit) return false;
        EditorCommand command;
        try
        {
            command = make();
        }
        catch (Exception ex) when (ex is PropertyValueException or InvalidOperationException or ArgumentException)
        {
            _report(ex.Message);
            return false;
        }

        // A merged change is a drag's move (a slider, the colour picker, a gradient's stop, dial, centre or radius): the
        // first one begins the drag's interaction, so however long it rests it is one undo step, and it is saved once,
        // when it is let go (Seal). Any other change ends a drag left open.
        if (command.MergeKey is null) EndDrag();
        else if (Session is { IsInteracting: false, IsPreviewing: false } session) session.BeginInteraction();
        return _run(command);
    }

    /// Ends a run of merged changes (a slider, colour or gradient drag let go): its interaction ends, so it is saved,
    /// and the next change is a step of its own.
    internal void Seal()
    {
        EndDrag();
        Session?.History.Seal();
    }

    void EndDrag()
    {
        if (Session is { IsInteracting: true } session) session.EndInteraction();
    }

    internal void Report(string message) => _report(message);

    /// The values the canvas draws now, for a template's preview, with a sample for each of `keys` the theme doesn't
    /// read yet: a template being typed often names a key no widget has used, which the canvas would show once saved.
    internal SensorSnapshot Snapshot(IReadOnlyList<string> keys)
    {
        var snapshot = _workspace?.Values.Snapshot() ?? SensorSnapshot.Of(DateTimeOffset.UtcNow, new Dictionary<string, SensorEntry>());
        var read = _workspace?.Values.TestValues.Keys ?? [];
        var unread = keys.Where(key => key != TestValues.Now && !read.Contains(key)).Distinct().ToList();
        if (unread.Count == 0) return snapshot;

        var now = snapshot.Now;
        var entries = new Dictionary<string, SensorEntry>(StringComparer.Ordinal);
        foreach (var key in snapshot.Keys)
            if (snapshot.TryGetLast(key, out var value, out var stale))
                entries[key] = new SensorEntry(value, now, stale ? now : null, [.. snapshot.History(key)]);
        foreach (var key in unread) entries[key] = new SensorEntry(Samples.ValueFor(key, now), now, null, []);
        return SensorSnapshot.Of(now, entries);
    }

    internal TimeLocale? Locale => Session?.Locale;

    void OnSessionChanged(object? sender, SessionChanges changes)
    {
        // A canvas drag's moves, and a panel drag beginning or ending, change no value.
        if ((changes & ~(SessionChanges.Preview | SessionChanges.Interaction)) == 0) return;
        Refresh(rebuild: false);
    }

    void OnValuesUpdated(object? sender, EventArgs args)
    {
        RulesList?.RefreshMatching();
        Ring?.Rules.RefreshMatching();
    }

    // Nested panels share the workspace and writers, but own their row index and never follow selection themselves.
    internal PropertiesViewModel CreateTarget(PropertyModel model, Func<EditorCommand, bool>? run = null)
    {
        var panel = new PropertiesViewModel(run ?? _run, _report, _canvas, _dialogs, _orientation, _changeKind)
        {
            _workspace = _workspace, _nested = true,
        };
        panel.RefreshTarget(model);
        return panel;
    }

    internal void RefreshTarget(PropertyModel model) => Refresh(false, model);

    /// Builds the model again, and the rows: in place when the panel's shape is the same, else afresh.
    void Refresh(bool rebuild, PropertyModel? target = null)
    {
        var session = Session;
        var model = target ?? (session is null ? null : PropertyModel.For(session));
        _model = model;
        Raise(nameof(Target));
        Raise(nameof(IsLayer));
        Raise(nameof(IsWidgets));
        Raise(nameof(IsStyle));
        Raise(nameof(HasProperties));
        Raise(nameof(Kind));
        HasSelection = model is { Widgets.Count: > 0 };
        var shape = Shape(model);
        if (!rebuild && shape == _shape)
        {
            foreach (var row in Flatten(model!.Rows.Concat(model.Style.SelectMany(section => section.Rows))))
                if (_byName.TryGetValue(row.Name, out var view)) view.Update(row);
        }
        else
        {
            _shape = shape;
            _byName = [];
            Rows = model?.Rows.Select(Make).ToList() ?? [];
            Sections = model?.Style.Select(section => new PropertySectionViewModel(SectionTitle(section.Title), section.Rows.Select(Make).ToList())).ToList()
                ?? [];
        }

        Heading = model?.Target == PropertyTargetKind.Canvas ? Strings.ThemeSettings
            : model?.Target == PropertyTargetKind.Style ? session!.SelectedStyle!
            : model?.Target == PropertyTargetKind.Layer ? LayersModel.Of(session!).Find(session!.SelectedLayer!)!.Name
            : model?.Widgets.Count switch
        {
            null or 0 => "",
            1 => model.Widgets[0].Name,
            var count => Strings.WidgetsSelected(count.Value),
        };
        StyleChoices = model is null ? [] : [Strings.NoNamedStyle, .. model.StyleNames];
        Raise(nameof(StyleChoices));
        StyleLabel = model?.StyleUse is { } use ? StyleText(use) : null;
        _styleChoice = model?.StyleUse is { } used ? used.Name ?? Strings.NoNamedStyle : null;
        Raise(nameof(StyleChoice));
        Rules = model?.Rules.Select(rule => rule.Text).ToList() ?? [];
        if (!_nested)
        {
            var owner = model is { Target: PropertyTargetKind.Widgets, Widgets.Count: 1 } ? model.Widgets[0].Path : null;
            if (RulesList?.Owner != owner || rebuild)
                RulesList = owner is null ? null : new RulesViewModel(this, owner);
            RulesList?.Refresh();
            if (model?.Target == PropertyTargetKind.Canvas)
            {
                if (Ring is null || rebuild) Ring = new RingViewModel(this, _configFile);
                Ring.Refresh();
            }
            else Ring = null;
            Raise(nameof(RulesList));
            Raise(nameof(Ring));
        }
        if (session?.SelectedLayer is { } layer && model?.Type == "shader")
        {
            if (Shader?.Layer != layer || rebuild)
            {
                Shader?.Dispose();
                Shader = new ShaderPanelViewModel(this, layer);
            }
            Shader.Refresh();
        }
        else
        {
            Shader?.Dispose();
            Shader = null;
        }
        Raise(nameof(CanEdit));
        ClearOverridesCommand.Refresh();
        SplitIntoSpansCommand.Refresh();
        CopyStyleCommand.Refresh();
        PasteStyleCommand.Refresh();
        SaveAsStyleCommand.Refresh();
    }

    PropertyRowViewModel Make(PropertyRow row)
    {
        // Each row adds itself, and its children and items, to the panel's index (Register).
        return new PropertyRowViewModel(this, row);
    }

    internal void Register(PropertyRowViewModel view) => _byName[view.Name] = view;

    /// What decides whether the rows can be kept: the selection, and every row's name and control.
    static string Shape(PropertyModel? model) =>
        model is null
            ? ""
            : model.Target + ":" + string.Join("|", model.Rows.SelectMany(row => row.Paths))
              + string.Join("|", model.Widgets.Select(widget => widget.Path.ToString()))
              + "#" + string.Join("|", Flatten(model.Rows.Concat(model.Style.SelectMany(section => section.Rows)))
                  .Select(row => $"{row.Name}:{PropertyEditors.For(row.Kind)}:{row.IsEditable}"));

    static IEnumerable<PropertyRow> Flatten(IEnumerable<PropertyRow> rows) =>
        rows.SelectMany(row => Flatten(row.Children.Concat(row.Items.SelectMany(item => item.Rows))).Prepend(row));

    /// The model's section titles in the user's language.
    static string SectionTitle(string title) => title switch
    {
        StyleSections.Text => Strings.SectionText,
        StyleSections.Fill => Strings.SectionFill,
        StyleSections.Outline => Strings.SectionOutline,
        StyleSections.Effects => Strings.SectionEffects,
        _ => Strings.SectionMore,
    };

    static string StyleText(StyleUse use) => (use.Name, use.Count) switch
    {
        (null, 0) => Strings.StyleNone,
        (null, var count) => Strings.StyleKeysSetHere(count.ToString(CultureInfo.CurrentCulture)),
        ({ } name, 0) => name,
        ({ } name, var count) => Strings.StyleOverrides(name, count.ToString(CultureInfo.CurrentCulture)),
    };
}

/// One group of the Style section, with its title in the user's language.
public sealed record PropertySectionViewModel(string Title, IReadOnlyList<PropertyRowViewModel> Rows);

public sealed record LayerKindOption(LayerKindChoice Kind, string Label);
