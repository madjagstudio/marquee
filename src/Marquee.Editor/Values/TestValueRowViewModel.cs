using System.Globalization;
using Marquee.Core.Expressions;
using Marquee.Editor.Model.Values;
using Marquee.Editor.Mvvm;
using Marquee.Editor.Resources;

namespace Marquee.Editor.Values;

/// One key of the test values panel: where its value comes from and what it is, and its pin control.
/// - A number pin: the slider over the key's range (from the gauges and bars reading it, else 0 to 100) or the box,
///   with a pattern (steady, ramp, spikes) when a graph draws the key.
/// - Stale, Missing and Unpin.
/// - `now`: a date and a time, pinned together.
public sealed class TestValueRowViewModel : ObservableObject
{
    readonly TestValuesViewModel _panel;
    ValueRow _row;
    DateTimeOffset? _pinnedNow;
    string _numberText = "";
    double _number;
    bool _isOpen;
    DateTime? _date;
    string _time = "";
    string? _error;

    public TestValueRowViewModel(TestValuesViewModel panel, ValueRow row, DateTimeOffset? pinnedNow)
    {
        _panel = panel;
        _row = row;
        _pinnedNow = pinnedNow;
        StaleCommand = new RelayCommand(() => Pin(TestPin.Stale), () => !IsNow);
        MissingCommand = new RelayCommand(() => Pin(TestPin.Missing), () => !IsNow);
        UnpinCommand = new RelayCommand(Unpin, () => IsPinned);
        PinNowCommand = new RelayCommand(PinNow, () => IsNow);
        Load();
    }

    public string Key => _row.Key;
    public string Name => SensorNames.For(Key).Name;
    public string? Unit => SensorNames.For(Key).Unit;
    public string CategoryName => SensorNames.CategoryName(SensorNames.For(Key).Category);

    public bool IsNow => _row.Key == TestValues.Now;

    public ValueSourceKind Source => _row.Source;

    /// The badge: Pinned, Tray, Local or Sample.
    public string SourceText => _row.Source switch
    {
        ValueSourceKind.Pinned => Strings.SourcePinned,
        ValueSourceKind.Tray => Strings.SourceTray,
        ValueSourceKind.Local => Strings.SourceLocal,
        _ => Strings.SourceSample,
    };

    /// The value as the canvas gets it: a number or text, "—" for missing, marked when stale; `now` as a local date and
    /// time, and a size in bytes as "3.3 GB".
    public string ValueText
    {
        get
        {
            if (_row.Value is not { } value) return Strings.ValueMissing;
            string text;
            if (IsNow && value.Number is { } seconds)
                text = DateTimeOffset.FromUnixTimeSeconds((long)seconds).ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
            else if (SensorNames.IsBytes(Key) && value.Number is { } bytes)
                text = Formatting.Bytes(bytes);
            else
                text = Formatting.Format(value);
            return _row.Stale ? Strings.ValueStale(text) : text;
        }
    }

    public bool IsPinned => IsNow ? _pinnedNow is not null : _row.Pin is not null;

    public bool IsGraph => _row.IsGraph;

    /// The pin control is open under the row.
    public bool IsOpen
    {
        get => _isOpen;
        set => Set(ref _isOpen, value);
    }

    public double Min => _row.Range.Min;

    public double Max => _row.Range.Max;

    /// The slider: dragging it pins the key to the number.
    public double Number
    {
        get => _number;
        set
        {
            if (!Set(ref _number, value)) return;
            _numberText = Show(value);
            Raise(nameof(NumberText));
            Pin(TestPin.Of(value, Pattern));
        }
    }

    /// The box beside the slider; CommitNumber pins it.
    public string NumberText
    {
        get => _numberText;
        set
        {
            if (Set(ref _numberText, value)) Error = null;
        }
    }

    /// Why the number typed can't be pinned.
    public string? Error
    {
        get => _error;
        private set => Set(ref _error, value);
    }

    public IReadOnlyList<HistoryPattern> Patterns { get; } = Enum.GetValues<HistoryPattern>();

    /// A graph key's history pattern, kept with the number pin.
    public HistoryPattern Pattern
    {
        get => _row.Pin?.Pattern ?? HistoryPattern.Steady;
        set
        {
            if (value != Pattern) Pin(TestPin.Of(_row.Pin?.Kind == PinKind.Number ? _row.Pin.Number : Number, value));
        }
    }

    /// `now`'s pin: the date.
    public DateTime? Date
    {
        get => _date;
        set => Set(ref _date, value);
    }

    /// `now`'s pin: the time of day, as the user writes times.
    public string Time
    {
        get => _time;
        set => Set(ref _time, value);
    }

    public RelayCommand StaleCommand { get; }

    public RelayCommand MissingCommand { get; }

    public RelayCommand UnpinCommand { get; }

    public RelayCommand PinNowCommand { get; }

    /// Pins the number in the box; false, with Error, when it isn't one.
    public bool CommitNumber()
    {
        if (!TryNumber(NumberText, out var value))
        {
            Error = Strings.NotANumber(NumberText);
            return false;
        }

        _number = value;
        Raise(nameof(Number));
        Pin(TestPin.Of(value, Pattern));
        return true;
    }

    /// Pins `now` to the date and time chosen; false, with Error, when the time isn't one.
    public void PinNow()
    {
        if (_panel.TestValues is not { } values) return;
        var date = (Date ?? DateTime.Today).Date;
        if (!TimeSpan.TryParse(Time.Trim(), CultureInfo.CurrentCulture, out var time) || time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
        {
            Error = Strings.NotATime(Time);
            return;
        }

        Error = null;
        var local = DateTime.SpecifyKind(date + time, DateTimeKind.Local);
        values.PinNow(new DateTimeOffset(local));
    }

    internal void Update(ValueRow row, DateTimeOffset? pinnedNow)
    {
        _row = row;
        _pinnedNow = pinnedNow;
        Load();
    }

    void Pin(TestPin pin)
    {
        try
        {
            _panel.TestValues?.Pin(Key, pin);
        }
        catch (ArgumentException ex)
        {
            Error = ex.Message;
        }
    }

    void Unpin() => _panel.TestValues?.Unpin(Key);

    /// The controls from the row: a pinned number, or the value underneath, or the range's middle.
    void Load()
    {
        if (!_isOpen || _row.Pin is { Kind: PinKind.Number })
        {
            var number = _row.Pin is { Kind: PinKind.Number } pin ? pin.Number
                : _row.Value?.Number is { } value && !IsNow ? value
                : (Min + Max) / 2;
            _number = number;
            _numberText = Show(number);
        }

        if (IsNow && (_date is null || _pinnedNow is not null))
        {
            var at = (_pinnedNow ?? DateTimeOffset.Now).ToLocalTime();
            _date = at.Date;
            _time = at.TimeOfDay.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        }

        Raise(null);
        StaleCommand.Refresh();
        MissingCommand.Refresh();
        UnpinCommand.Refresh();
    }

    static bool TryNumber(string text, out double value) =>
        (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value)
         || double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        && double.IsFinite(value);

    static string Show(double value) => Math.Round(value, 2).ToString(CultureInfo.CurrentCulture);
}
