using System.Globalization;
using System.Windows.Media;
using Marquee.Core.Expressions;
using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes.Schema;
using Marquee.Editor.Controls;
using Marquee.Editor.Gradients;
using Marquee.Editor.Model;
using Marquee.Editor.Mvvm;
using Marquee.Editor.Resources;

namespace Marquee.Editor.Properties;

/// What each kind of control needs beyond the text box: the slider's range and its merged drag, the checkbox, the
/// dropdowns' choices, the colour swatch, the paint's gradient switch and editor, the two boxes of a point or size,
/// a template's preview, and the file pickers' choices and "Import…".
public sealed partial class PropertyRowViewModel
{
    string _x = "";
    string _y = "";
    double _number;
    string? _preview;
    GradientEditorViewModel? _gradient;
    public ValuePickerViewModel? Picker { get; private set; }

    // --- Number ---

    public double SliderMin { get; private set; }

    public double SliderMax { get; private set; }

    public double Step => _row.Step * (IsPercentage ? 100 : 1);

    public double? Minimum => _row.Minimum * (IsPercentage ? 100 : 1);

    /// The slider's value: the number shown, or the slider's low end when the text isn't one.
    public double Number
    {
        get => _number;
        private set => Set(ref _number, value);
    }

    /// The slider moved to `value`: it is rounded to the step and written, merged with the slider's earlier moves into
    /// one undo step. `final` is true for a move that ends the run (a click on the track, a key): the step is then
    /// closed, as EndSlide does when a drag is let go.
    public void SlideTo(double value, bool final)
    {
        var stepped = Math.Clamp(Math.Round(value / Step) * Step, Math.Max(SliderMin, Minimum ?? SliderMin), SliderMax);
        var text = NumberText(stepped, Step);
        if (text != Text || !_row.IsSet)
        {
            _text = text;
            Raise(nameof(Text));
            Write(StoredValue(text), merge: true);
        }

        if (final) EndSlide();
    }

    /// The slider was let go: its run of moves is one undo step, and the next change starts another.
    public void EndSlide() => _owner.Seal();

    // --- Boolean ---

    /// The checkbox: null while the widgets differ. Checking or clearing it writes the key.
    public bool? Checked
    {
        get => IsMixed ? null : Text == "true";
        set
        {
            if (value is not { } on || value == Checked) return;
            Write(new YamlText(on ? "true" : "false"));
        }
    }

    // --- Choice, weight ---

    /// A dropdown's values: an enum's, the theme's named styles, or a weight's names then 100 to 900.
    public IReadOnlyList<string> Choices => Editor == PropertyEditor.Weight
        ? [.. _row.Choices, .. Enumerable.Range(1, 9).Select(n => (n * 100).ToString(CultureInfo.InvariantCulture))]
        : _row.Choices;

    /// The dropdown's choice; choosing another writes it.
    public string? Choice
    {
        get => IsMixed ? null : Text;
        set
        {
            if (value is null || value == Choice) return;
            if (!Write(new YamlText(value))) Raise(nameof(Choice));
        }
    }

    // --- Color, paint ---

    /// The colour the swatch shows: the hex in the box, or transparent when it isn't a colour.
    public Color Swatch => ColorText.TryParse(Text, out var color) ? color : Colors.Transparent;

    /// A drag in the colour picker: written as it goes, merged into one undo step.
    public void PreviewColor(string hex) => Write(new YamlText(hex), merge: true);

    /// The colour picker's choice: written, merged with any drag before it, and the step closed. It joins the recent
    /// colours.
    public void CommitColor(string hex)
    {
        if (Write(new YamlText(hex), merge: true)) RecentColors.Shared.Add(hex);
        EndSlide();
    }

    /// The paint is a gradient: the widget's own, or its named style's when it sets none.
    public bool IsGradient => Editor == PropertyEditor.Paint && GradientModel.Read(_row) is not null;

    /// The paint's swatch: the gradient as WPF draws it, or the colour.
    public Brush SwatchBrush
    {
        get
        {
            if (GradientModel.Read(_row) is { } gradient) return GradientEditorViewModel.BrushOf(gradient);
            var brush = new SolidColorBrush(Swatch);
            brush.Freeze();
            return brush;
        }
    }

    /// The gradient switch can be used: one widget's paint, which can be written.
    public bool CanUseGradient => Editor == PropertyEditor.Paint && _row.Paths.Count == 1 && IsEditable;

    /// The gradient switch: on turns the colour into a gradient of that colour at both ends, which the gradient editor
    /// then recolours; off turns a gradient back into its first colour.
    public bool UseGradient
    {
        get => IsGradient;
        set
        {
            if (value == IsGradient || !CanUseGradient) return;
            if (value)
            {
                var color = ColorText.TryParse(Text, out var parsed) ? ColorText.Format(parsed) : "#FFFFFF";
                if (_owner.Write(() => GradientModel.FromColor(color).Write(_row.Paths[0]))) IsGradientOpen = true;
            }
            else if (GradientModel.Read(_row) is { } gradient)
            {
                Write(new YamlText(gradient.Colors[0]));
            }

            Raise(nameof(UseGradient));
        }
    }

    /// The gradient editor is open under the row.
    public bool IsGradientOpen
    {
        get => _owner.ExpansionFor("gradient:" + Name).IsExpanded && Gradient is not null;
        set
        {
            _owner.ExpansionFor("gradient:" + Name).IsExpanded = value;
            Raise(nameof(IsGradientOpen));
            Raise(nameof(Gradient));
        }
    }

    /// The gradient editor for the paint; null when it isn't a gradient, or several widgets are selected.
    public GradientEditorViewModel? Gradient
    {
        get
        {
            if (!CanUseGradient || GradientModel.Read(_row) is not { } gradient) return _gradient = null;
            if (_gradient is null)
            {
                var box = _owner.Model?.Widgets.FirstOrDefault()?.LocalBounds.Size ?? new SkiaSharp.SKSize(100, 100);
                _gradient = new GradientEditorViewModel(_row.Paths[0], gradient, box, command => _owner.Write(() => command), _owner.Seal);
            }

            return _gradient;
        }
    }

    // --- Point, size ---

    /// The first of two numbers: x, or the width.
    public string X
    {
        get => _x;
        set
        {
            if (Set(ref _x, value)) PairChanged();
        }
    }

    /// The second: y, or the height.
    public string Y
    {
        get => _y;
        set
        {
            if (Set(ref _y, value)) PairChanged();
        }
    }

    public string XLabel => _row.Kind == SchemaKind.Size ? Strings.Width : "X";

    public string YLabel => _row.Kind == SchemaKind.Size ? Strings.Height : "Y";

    public bool HasSizeSliders => _row.Kind == SchemaKind.Size;
    public double SizeSliderMin => Math.Max(_row.SliderMin ?? 8, Math.Max(_row.Minimum ?? 0, _row.Min ?? 1));
    public double WidthSliderMax => Math.Min(_row.SliderMax ?? 1920, _owner.Session?.CanvasSize.Width ?? 1920);
    public double HeightSliderMax => Math.Min(_row.SliderMax ?? 1920, _owner.Session?.CanvasSize.Height ?? 480);
    public double WidthNumber => PairNumber(X, WidthSliderMax);
    public double HeightNumber => PairNumber(Y, HeightSliderMax);

    double PairNumber(string text, double max) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        ? Math.Clamp(value, SizeSliderMin, max) : SizeSliderMin;

    public void SlideSizeTo(double value, bool width, bool final)
    {
        var text = NumberText(Math.Clamp(Math.Round(value), SizeSliderMin, width ? WidthSliderMax : HeightSliderMax), 1);
        var x = width ? text : X;
        var y = width ? Y : text;
        Write(new YamlList([new YamlText(x), new YamlText(y)]), merge: true);
        if (final) EndSlide();
    }

    /// Writes the two boxes as `[x, y]`.
    public bool CommitPair()
    {
        if (!_dirty) return true;
        var value = new YamlList([new YamlText(X.Trim()), new YamlText(Y.Trim())]);
        if (Shown() == YamlWriter.Flow(value) && _row.IsSet)
        {
            _dirty = false;
            return true;
        }

        return Write(value);
    }

    // --- Font, key, file ---

    /// A font picker's, key picker's or file picker's list: the theme's font files, Inter and the system's fonts; the
    /// keys the theme reads and the samples know; the theme's files of the key's type.
    public IReadOnlyList<string> Suggestions => Editor switch
    {
        PropertyEditor.Font => PropertyChoices.Fonts(_owner.Session?.Folder),
        PropertyEditor.Key => PropertyChoices.Keys(_owner.Workspace?.Values.TestValues.Keys ?? []),
        PropertyEditor.File => PropertyChoices.Files(_owner.Session?.Folder, _row.Key, ImportKind),
        _ => [],
    };

    /// The kind of file "Import…" brings in for this key; null when it takes none the editor imports.
    public AssetKind? ImportKind => _owner.Session is { } session && _row.Paths.Count == 1
        ? AssetImport.KindFor(session.Document, _row.Paths[0]) ?? (Editor == PropertyEditor.Font ? AssetKind.Font : null)
        : null;

    public bool CanImport => IsEditable && ImportKind is not null;

    public RelayCommand ImportCommand { get; }

    /// "Import…": asks for a file, copies it into the theme (AssetImport) and sets the key to it, as one undo step.
    void Import()
    {
        if (_owner.Session is not { } session || ImportKind is not { } kind || _owner.Dialogs.PickImportFile(kind) is not { } file) return;
        if (!_owner.CanEdit) return;
        try
        {
            var imported = new AssetImport(session.Folder).SetFile(session, _row.Paths[0], file);
            PropertyChoices.Forget();
            Raise(nameof(Suggestions));
            if (imported.Warning is { } warning) _owner.Report(warning);
        }
        catch (AssetImportException ex)
        {
            _owner.Dialogs.ShowError(Strings.ImportRefused(Path.GetFileName(file), ex.Reason));
        }
        catch (Exception ex) when (ex is LayoutEditException or Core.Themes.ThemeException or ArgumentException or IOException
                                       or UnauthorizedAccessException)
        {
            _owner.Dialogs.ShowError(Strings.ImportRefused(Path.GetFileName(file), ex.Message));
        }
    }

    // --- Template, format ---

    /// What the template or format shows with the canvas's values now, `time` and the rest of the helpers included;
    /// null when the text doesn't parse (Error says why).
    public string? Preview
    {
        get => _preview;
        private set => Set(ref _preview, value);
    }

    void LoadKinds()
    {
        LoadTextPicker();
        if (Editor == PropertyEditor.ValuePicker && _owner.Workspace is { } workspace)
        {
            if (Picker is null)
            {
                Picker = new ValuePickerViewModel(workspace.Values, Text, showRange: false);
                Picker.Committed += (_, _) =>
                {
                    if (!IsEditable || Picker.Text is not { } text) return;
                    Text = text;
                    Commit();
                };
            }
            else Picker.Update(Text);
        }
        if (Editor == PropertyEditor.Number) LoadNumber();
        if (Editor == PropertyEditor.Pair && _row.Value is YamlList { Items: [YamlText x, YamlText y] })
            (_x, _y) = (x.Value, y.Value);
        else if (Editor == PropertyEditor.Pair)
            (_x, _y) = Pair(_row.IsMixed ? null : _row.Default);
        UpdatePreview();
    }

    void UpdateKinds()
    {
        if (_gradient is not null && GradientModel.Read(_row) is { } gradient) _gradient.Update(gradient);
        Raise(nameof(Gradient));
        Raise(nameof(IsGradientOpen));
        Raise(nameof(UseGradient));
        Raise(nameof(IsGradient));
    }

    void TextChanged()
    {
        LoadTextPicker();
        if (Editor == PropertyEditor.Number && double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            Number = Math.Clamp(number, SliderMin, SliderMax);
        if (Editor is PropertyEditor.Color or PropertyEditor.Paint)
        {
            Raise(nameof(Swatch));
            Raise(nameof(SwatchBrush));
        }
        UpdatePreview();
    }

    void PairChanged()
    {
        _dirty = true;
        var value = new YamlList([new YamlText(X.Trim()), new YamlText(Y.Trim())]);
        Error = _owner.Model?.Validate(_row, value);
        Raise(nameof(WidthNumber));
        Raise(nameof(HeightNumber));
    }

    /// Authored slider ranges stay fixed even when a typed or inherited value lies outside them.
    void LoadNumber()
    {
        double.TryParse(_text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value);
        var scale = IsPercentage ? 100 : 1;
        SliderMin = Math.Max(_row.SliderMin ?? throw new InvalidOperationException($"No slider range for {Name}."),
            Math.Max(_row.Min ?? double.NegativeInfinity, _row.Minimum ?? double.NegativeInfinity)) * scale;
        SliderMax = Math.Min(_row.SliderMax ?? throw new InvalidOperationException($"No slider range for {Name}."),
            _row.Max ?? double.PositiveInfinity) * scale;
        _number = Math.Clamp(value, SliderMin, SliderMax);
    }

    void UpdatePreview()
    {
        if (Editor != PropertyEditor.Template || _owner.Model is not { Widgets: [var widget, ..] } || Text.Length == 0 || Error is not null)
        {
            Preview = null;
            return;
        }

        try
        {
            var template = _row.Kind == SchemaKind.Format ? null : Template.Parse(Text, withValue: widget.Type == "value");
            var sensors = _owner.Snapshot([.. template?.Keys ?? [], .. widget.Spec.Source?.Keys ?? []]);
            var value = widget.Spec.Source?.Evaluate(sensors);
            Preview = template is null
                ? Formatting.Apply(Text, value ?? default, _owner.Locale)
                : template.Render(sensors, _owner.Locale, value).Text;
        }
        catch (Exception ex) when (ex is ExpressionException or FormatException or ArgumentException)
        {
            Preview = null;
        }
    }

    static (string, string) Pair(string? written) =>
        written is { } text && YamlValue.Parse(text) is YamlList { Items: [YamlText x, YamlText y] } ? (x.Value, y.Value) : ("", "");
}
