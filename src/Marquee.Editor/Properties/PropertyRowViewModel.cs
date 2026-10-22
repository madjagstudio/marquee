using System.Globalization;
using System.Text;
using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes.Schema;
using Marquee.Editor.Model;
using Marquee.Editor.Mvvm;
using Marquee.Editor.Resources;

namespace Marquee.Editor.Properties;

/// One row of the properties panel: a catalog key of the selected widgets, its control (Editor), and what the control
/// shows and writes. What it shows:
/// - the value the file sets, or, greyed (IsDefault), what the key is when left out: its default, or what the named
///   style gives;
/// - nothing, with "Mixed", when the selected widgets differ (IsMixed);
/// - read-only, when the key sits in an anchor or alias (IsEditable);
/// - the catalog's help, the default and the unit, as a tooltip.
/// Typing checks the value as it goes (Error) and writes it on Commit (Enter, or leaving the box); a slider writes as
/// it moves, merged into one undo step until it is let go (PropertyRowViewModel.Kinds.cs). Reset removes the key where
/// the key can be left out (CanReset).
public sealed partial class PropertyRowViewModel : ObservableObject
{
    readonly PropertiesViewModel _owner;
    PropertyRow _row;
    string _text = "";
    bool _dirty;
    string? _error;

    public PropertyRowViewModel(PropertiesViewModel owner, PropertyRow row)
    {
        _owner = owner;
        _row = row;
        Editor = row.Key.Name == "source" && row.Kind is SchemaKind.Expression or SchemaKind.SensorKey ? PropertyEditor.ValuePicker
            : row.Kind == SchemaKind.List && row.Key.Of is null ? PropertyEditor.ReadOnly : PropertyEditors.For(row.Kind);
        Children = row.Children.Select(child => new PropertyRowViewModel(owner, child)).ToList();
        Items = row.Items.Select(item => new PropertyItemViewModel(this, item)).ToList();
        ResetCommand = new RelayCommand(Reset, () => CanReset);
        ImportCommand = new RelayCommand(Import, () => CanImport);
        AddItemCommand = new RelayCommand(AddItem, () => _owner.CanEdit && _row.CanAdd);
        IsExpanded = row.IsSet;
        owner.Register(this);
        Load();
    }

    public PropertyRow Row => _row;

    /// Where the row writes, from the widget: `size`, `style.stroke.width`, `series[1].source`.
    public string Name => _row.Name;

    /// The key's name, as layout.yaml writes it.
    public string Label => _row.Key.Name;

    public PropertyEditor Editor { get; }

    /// The catalog's help, then the default, what the named style gives, and why the row is read-only.
    public string Help
    {
        get
        {
            var help = new StringBuilder(_row.Help);
            if (_row.Default is { } fallback) help.Append('\n').Append(Strings.DefaultIs(fallback));
            if (!_row.IsSet && _row.Inherited is { } inherited) help.Append('\n').Append(Strings.FromNamedStyle(Show(inherited)));
            if (!_row.IsEditable) help.Append('\n').Append(Strings.KeyReadOnly);
            return help.ToString();
        }
    }

    /// The unit after a number: px, °, ms, %, ×; empty for none, and for a fraction.
    public string Unit => _row.Unit switch
    {
        SchemaUnit.Px => "px",
        SchemaUnit.Degrees => "°",
        SchemaUnit.Ms => "ms",
        SchemaUnit.Percent => "%",
        SchemaUnit.Multiplier => "×",
        _ => "",
    };

    public bool IsSet => _row.IsSet;

    public bool IsMixed => _row.IsMixed;

    /// Not set on any selected widget: the control shows the default, greyed.
    public bool IsDefault => !_row.IsSet && !_row.IsMixed;

    public bool IsEditable => _row.IsEditable && _owner.CanEdit;

    /// Reset is offered: the key can be left out, and a selected widget sets it.
    public bool CanReset => IsEditable && _row.CanReset && (_row.IsSet || _row.IsMixed);

    /// What the box says while the widgets differ and nothing is typed: "Mixed".
    public string Placeholder => IsMixed && Text.Length == 0 ? Strings.Mixed : "";

    /// The text in the row's box. Set from the box as it is typed: checked at once (Error), written by Commit.
    public string Text
    {
        get => _text;
        set
        {
            if (!Set(ref _text, value)) return;
            _dirty = true;
            Error = Check(value);
            Raise(nameof(Placeholder));
            TextChanged();
        }
    }

    /// Why the text typed can't be written; null when it can.
    public string? Error
    {
        get => _error;
        private set
        {
            if (Set(ref _error, value)) Raise(nameof(HasError));
        }
    }

    public bool HasError => Error is not null;

    public IReadOnlyList<PropertyRowViewModel> Children { get; }

    public IReadOnlyList<PropertyItemViewModel> Items { get; }

    /// A block's or list's sub-section is open: at first when the file sets it.
    public bool IsExpanded { get; set; }

    public RelayCommand ResetCommand { get; }

    public RelayCommand AddItemCommand { get; }

    /// Writes the text typed, if it changed: checked first, and left in the box with its Error when it is refused. An
    /// empty box resets the key where it can be left out, and otherwise goes back to the value. True when nothing was
    /// refused.
    public bool Commit()
    {
        if (!_dirty) return true;
        var text = Text.Trim();
        if (text == Shown() && (_row.IsSet || _row.IsMixed))
        {
            // Typed back to what the file has: nothing to write.
            _dirty = false;
            return true;
        }

        if (text.Length == 0)
        {
            if (CanReset) Reset();
            else Revert();
            return true;
        }

        return Write(new YamlText(text));
    }

    /// Puts the file's value back in the box, dropping what was typed.
    public void Revert()
    {
        _dirty = false;
        Load();
    }

    /// Writes `value` to the row on every selected widget, checked first; `merge` joins it to the change before (a
    /// slider's run). True when it was written.
    internal bool Write(YamlValue value, bool merge = false)
    {
        if (_owner.Model is not { } model) return false;
        if (model.Validate(_row, value) is { } reason)
        {
            Error = reason;
            return false;
        }

        Error = null;
        _dirty = false;
        var written = Name == "canvas.orientation" && value is YamlText orientation
            ? _owner.SetOrientation(orientation.Value)
            : _owner.Write(() => model.Set(_row, value, merge));
        if (!written) Load();
        return written;
    }

    /// Takes the row's new state after a change, keeping what is being typed in it.
    internal void Update(PropertyRow row)
    {
        _row = row;
        if (!_dirty) Load();
        else Raise(null);
        UpdateKinds();
        ResetCommand.Refresh();
        AddItemCommand.Refresh();
        ImportCommand.Refresh();
    }

    void Reset()
    {
        if (_owner.Model is not { } model) return;
        _dirty = false;
        _owner.Write(() => model.Reset(_row));
    }

    void AddItem()
    {
        if (_owner.Model is { } model) _owner.Write(() => model.AddItem(_row));
    }

    /// A row of one of this list's items, in the same panel.
    internal PropertyRowViewModel Child(PropertyRow row) => new(_owner, row);

    internal void RemoveItem(int index)
    {
        if (_owner.Model is { } model) _owner.Write(() => model.RemoveItem(_row, index));
    }

    internal void MoveItem(int from, int to)
    {
        if (_owner.Model is { } model && to >= 0 && to < Items.Count) _owner.Write(() => model.MoveItem(_row, from, to));
    }

    /// Fills the controls from the row.
    void Load()
    {
        _text = Shown();
        _error = null;
        LoadKinds();
        Raise(null);
    }

    /// The value as the box shows it: the file's, else the default or the named style's (greyed), or nothing for mixed
    /// values.
    string Shown()
    {
        if (_row.IsMixed) return "";
        if (_row.Value is { } value) return Show(value);
        if (_row.Inherited is { } inherited) return Show(inherited);
        return _row.Default ?? "";
    }

    string? Check(string text) =>
        text.Trim().Length == 0 || _owner.Model is not { } model ? null : model.Validate(_row, new YamlText(text.Trim()));

    static string Show(YamlValue value) => value is YamlText text ? text.Value : YamlWriter.Flow(value);

    /// A number as layout.yaml writes it: invariant, no more decimals than the step needs.
    static string NumberText(double value, double step)
    {
        var decimals = step >= 1 ? 0 : (int)Math.Ceiling(-Math.Log10(step) - 1e-9);
        return Math.Round(value, Math.Clamp(decimals, 0, 6)).ToString(CultureInfo.InvariantCulture);
    }
}

/// One item of a list row (a series, a slice, a span): its rows, removed or moved as a whole.
public sealed class PropertyItemViewModel
{
    readonly PropertyRowViewModel _list;

    public PropertyItemViewModel(PropertyRowViewModel list, PropertyItem item)
    {
        _list = list;
        Index = item.Index;
        Title = Strings.ListItem(list.Label, (item.Index + 1).ToString(CultureInfo.CurrentCulture));
        var owner = list;
        Rows = item.Rows.Select(row => owner.Child(row)).ToList();
        RemoveCommand = new RelayCommand(() => _list.RemoveItem(Index), () => _list.IsEditable && _list.Row.CanRemove);
        MoveUpCommand = new RelayCommand(() => _list.MoveItem(Index, Index - 1), () => _list.IsEditable && Index > 0);
        MoveDownCommand = new RelayCommand(() => _list.MoveItem(Index, Index + 1), () => _list.IsEditable && Index < list.Row.Items.Count - 1);
    }

    public int Index { get; }

    /// "series 1", "spans 2": the list's name and the item's place, counted from one.
    public string Title { get; }

    public IReadOnlyList<PropertyRowViewModel> Rows { get; }

    public bool IsExpanded { get; set; } = true;

    public RelayCommand RemoveCommand { get; }

    public RelayCommand MoveUpCommand { get; }

    public RelayCommand MoveDownCommand { get; }
}
