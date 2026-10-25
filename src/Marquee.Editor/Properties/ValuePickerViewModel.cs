using System.Globalization;
using System.Windows;
using Marquee.Core.Expressions;
using Marquee.Editor.Model.Values;
using Marquee.Editor.Mvvm;
using Marquee.Editor.Resources;
using Expression = Marquee.Core.Expressions.Expression;

namespace Marquee.Editor.Properties;

public enum ValuePickerMode { Sensor, Number, Advanced }

public sealed class ValuePickerKey(string key) : ObservableObject
{
    string _valueText = "";
    bool _hasReading;
    public string Key { get; } = key;
    public string Name => SensorNames.For(Key).Name;
    public string CategoryName => SensorNames.CategoryName(SensorNames.For(Key).Category);
    public string ValueText { get => _valueText; internal set => Set(ref _valueText, value); }
    public bool HasReading { get => _hasReading; internal set => Set(ref _hasReading, value); }
}
public sealed record ValuePickerModeChoice(ValuePickerMode Mode, string Label);

/// Shared expression editor. Drafts are checked as they change; only a pick or Commit asks the owner to write.
public sealed class ValuePickerViewModel : ObservableObject
{
    // Each picker starts with its sections closed. Pickers are recreated on selection changes, so a section opened in
    // one stays open while that widget is selected and doesn't open in the next.
    readonly Dictionary<string, SectionExpansion> _expansion = [];
    readonly ValueSources _sources;
    internal SectionExpansion ExpansionFor(string section)
    {
        if (!_expansion.TryGetValue(section, out var state)) _expansion[section] = state = new();
        return state;
    }
    public SectionExpansion RawExpansion => ExpansionFor("raw");
    string _saved = "";
    ValuePickerMode _mode;
    string? _selectedKey;
    string _search = "";
    string _fromLow = "0", _fromHigh = "100", _toLow = "0", _toHigh = "1", _number = "0", _advancedText = "";
    bool _ranged;
    bool _advancedEdited;
    bool _constantsOnly;
    IReadOnlyList<ValuePickerKey> _keys = [];

    public ValuePickerViewModel(ValueSources sources, string? text, bool showRange, string? hint = null)
    {
        _sources = sources;
        ShowRange = showRange;
        Hint = hint;
        Read(text, isDraft: false);
        // Rows can be replaced on selection changes; the sources must not retain old editors.
        WeakEventManager<ValueSources, EventArgs>.AddHandler(sources, nameof(ValueSources.Updated), OnValuesUpdated);
    }

    public IReadOnlyList<ValuePickerModeChoice> Modes => ConstantsOnly
        ? [new(ValuePickerMode.Number, Strings.PickerNumber), new(ValuePickerMode.Advanced, Strings.PickerAdvanced)]
        : [new(ValuePickerMode.Sensor, Strings.PickerSensor), new(ValuePickerMode.Number, Strings.PickerNumber), new(ValuePickerMode.Advanced, Strings.PickerAdvanced)];
    public bool ConstantsOnly
    {
        get => _constantsOnly;
        internal set
        {
            if (_constantsOnly == value) return;
            _constantsOnly = value;
            if (value && _mode == ValuePickerMode.Sensor) _mode = ValuePickerMode.Advanced;
            Raise(null);
        }
    }

    public ValuePickerMode Mode
    {
        get => _mode;
        set
        {
            if (ConstantsOnly && value == ValuePickerMode.Sensor) return;
            if (_mode == value) return;
            if (value == ValuePickerMode.Advanced && Text is { } text)
            {
                _advancedText = text;
                _advancedEdited = false;
            }
            _mode = value;
            Raise(null);
        }
    }

    public bool IsSensor => Mode == ValuePickerMode.Sensor;
    public bool IsNumber => Mode == ValuePickerMode.Number;
    public bool IsAdvanced => Mode == ValuePickerMode.Advanced;
    public bool ShowRange { get; }
    public bool SensorsOnly { get; init; }
    public bool ShowModes => !SensorsOnly;
    public string? Hint { get; internal set; }
    public string Search { get => _search; set { if (Set(ref _search, value)) Raise(nameof(Keys)); } }

    public IReadOnlyList<ValuePickerKey> Keys => _keys.Where(row => (!SensorsOnly || row.Key != "now") && (
        row.Name.Contains(Search, StringComparison.OrdinalIgnoreCase) || row.CategoryName.Contains(Search, StringComparison.OrdinalIgnoreCase)
        || row.Key.Contains(Search, StringComparison.OrdinalIgnoreCase))).ToList();

    public string? SelectedKey
    {
        get => _selectedKey;
        set
        {
            // List refresh/filtering can temporarily clear selection. That is not a user pick.
            if (value is null || value == _selectedKey) return;
            _selectedKey = value;
            _ranged = ShowRange;
            DefaultRange(value);
            RefreshKeys();
            Raise(null);
            Commit();
        }
    }

    public string FromLow { get => _fromLow; set { if (Set(ref _fromLow, value)) RangeChanged(); } }
    public string FromHigh { get => _fromHigh; set { if (Set(ref _fromHigh, value)) RangeChanged(); } }
    public string ToLow { get => _toLow; set { if (Set(ref _toLow, value)) RangeChanged(); } }
    public string ToHigh { get => _toHigh; set { if (Set(ref _toHigh, value)) RangeChanged(); } }
    public string Number { get => _number; set { if (Set(ref _number, value)) Changed(); } }
    public string AdvancedText { get => _advancedText; set { if (Set(ref _advancedText, value)) { _advancedEdited = true; Changed(); } } }
    public string AdvancedSummary => SensorNames.InText(AdvancedText);
    public string? Text => Validate(out var text) is null ? text : null;
    public string? Problem => IsAdvanced && _advancedEdited ? Validate(out _) : null;
    public bool HasProblem => Problem is not null;
    public event EventHandler? Committed;

    public bool Commit()
    {
        if (Text is null) return false;
        Committed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// Refresh the saved value without discarding a draft on an unrelated document change.
    internal void Update(string text)
    {
        if (text == _saved) return;
        if (text == Text) _saved = text;
        else Read(text, isDraft: false);
    }

    internal void Read(string? text, bool isDraft = true)
    {
        text ??= "";
        _saved = text;
        _advancedText = text;
        _advancedEdited = isDraft;
        _selectedKey = null;
        _ranged = false;
        switch (ValuePicker.Read(text))
        {
            case PickedValue.Unset:
                _mode = ValuePickerMode.Sensor;
                break;
            case PickedValue.Sensor sensor:
                _mode = ValuePickerMode.Sensor;
                _selectedKey = sensor.Key;
                DefaultRange(sensor.Key);
                break;
            case PickedValue.Ranged range:
                _mode = ValuePickerMode.Sensor;
                _selectedKey = range.Key;
                _ranged = true;
                (_fromLow, _fromHigh, _toLow, _toHigh) = (Format(range.FromLow), Format(range.FromHigh), Format(range.ToLow), Format(range.ToHigh));
                break;
            case PickedValue.Number number:
                _mode = ValuePickerMode.Number;
                _number = Format(number.Value);
                break;
            default:
                _mode = ValuePickerMode.Advanced;
                break;
        }
        if (ConstantsOnly && _mode == ValuePickerMode.Sensor) _mode = ValuePickerMode.Advanced;
        RefreshKeys();
        Raise(null);
    }

    string? Validate(out string? text)
    {
        text = null;
        switch (Mode)
        {
            case ValuePickerMode.Sensor:
                if (SelectedKey is null) return null;
                if (_ranged)
                {
                    if (!TryNumber(FromLow, out var low) || !TryNumber(FromHigh, out var high) ||
                        !TryNumber(ToLow, out var a) || !TryNumber(ToHigh, out var b)) return Strings.PickerInvalidNumber;
                    if (high <= low || b < a || !double.IsFinite(high - low) || !double.IsFinite(b - a)) return Strings.PickerInvalidRange;
                    text = ValuePicker.Write(new PickedValue.Ranged(SelectedKey, low, high, a, b));
                }
                else text = ValuePicker.Write(new PickedValue.Sensor(SelectedKey));
                break;
            case ValuePickerMode.Number:
                if (!TryNumber(Number, out var number)) return Strings.PickerInvalidNumber;
                text = ValuePicker.Write(new PickedValue.Number(number));
                break;
            case ValuePickerMode.Advanced:
                if (string.IsNullOrWhiteSpace(AdvancedText)) return null;
                text = AdvancedText;
                break;
        }
        try
        {
            if (Expression.Parse(text ?? "").Keys.Count > 0 && ConstantsOnly) return Strings.ShaderConstantRequired;
        }
        catch (ExpressionException ex) { return ex.Message; }
        return null;
    }

    void DefaultRange(string key)
    {
        var (low, high) = ValuePicker.DefaultRange(key);
        (_fromLow, _fromHigh, _toLow, _toHigh) = (Format(low), Format(high), "0", "1");
    }

    void RangeChanged() { _ranged = true; Changed(); }
    void Changed() { Raise(nameof(Text)); Raise(nameof(AdvancedSummary)); Raise(nameof(Problem)); Raise(nameof(HasProblem)); }
    void OnValuesUpdated(object? sender, EventArgs e) => RefreshKeys();

    void RefreshKeys()
    {
        var names = SensorNames.Grouped(ValuePicker.Keys(_sources, _selectedKey is { } key ? [key] : []))
            .SelectMany(group => group).Select(name => name.Key).ToList();
        var changed = !names.SequenceEqual(_keys.Select(row => row.Key));
        if (changed)
        {
            var existing = _keys.ToDictionary(row => row.Key);
            _keys = names.Select(name => existing.GetValueOrDefault(name) ?? new ValuePickerKey(name)).ToList();
        }
        var readings = _sources.Rows(names).ToDictionary(row => row.Key);
        foreach (var row in _keys)
        {
            var reading = readings[row.Key];
            row.HasReading = reading.Source != ValueSourceKind.Sample && reading.Value is not null && !reading.Stale;
            row.ValueText = row.HasReading ? SensorNames.Format(row.Key, reading.Value!.Value) : Strings.PickerNoReading;
        }
        if (changed) Raise(nameof(Keys));
    }
    static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    static bool TryNumber(string text, out double number) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number);
}
