using Marquee.Editor.Model.Values;
using Marquee.Editor.Mvvm;

namespace Marquee.Editor.Values;

/// The test values panel: every key the theme reads, then `now`, each with where its value comes from
/// (pinned, the tray, the editor's own readings or a sample) and the value; and a pin for each, to a number on a slider
/// over the key's range, stale or missing, with a pattern for a key a graph draws; `now` pins to a date and time.
/// "Unpin all" ends every pin, and the panel says when the tray isn't running. Pins are never saved.
///
/// The rows follow the value sources (ValueSources.Updated, about once a second, and after each pin) and the theme's
/// keys; a row keeps its place and its open pin control while its value moves on.
///
/// Made and used on the UI thread.
public sealed class TestValuesViewModel : ObservableObject
{
    ValueSources? _values;
    IReadOnlyList<TestValueRowViewModel> _rows = [];
    bool _trayNotRunning;
    bool _anyPinned;

    public TestValuesViewModel()
    {
        UnpinAllCommand = new RelayCommand(() => _values?.TestValues.UnpinAll(), () => AnyPinned);
    }

    public IReadOnlyList<TestValueRowViewModel> Rows
    {
        get => _rows;
        private set => Set(ref _rows, value);
    }

    /// No tray answered, so values come from the editor's own readings and samples.
    public bool TrayNotRunning
    {
        get => _trayNotRunning;
        private set => Set(ref _trayNotRunning, value);
    }

    public bool AnyPinned
    {
        get => _anyPinned;
        private set
        {
            if (Set(ref _anyPinned, value)) UnpinAllCommand.Refresh();
        }
    }

    public RelayCommand UnpinAllCommand { get; }

    internal TestValues? TestValues => _values?.TestValues;

    /// The row for `key`; null when the theme doesn't read it.
    public TestValueRowViewModel? Row(string key) => Rows.FirstOrDefault(row => row.Key == key);

    public void Attach(ValueSources values)
    {
        Detach();
        _values = values;
        values.Updated += OnUpdated;
        values.TestValues.Changed += OnUpdated;
        Refresh();
    }

    public void Detach()
    {
        if (_values is not { } values) return;
        values.Updated -= OnUpdated;
        values.TestValues.Changed -= OnUpdated;
        _values = null;
        Rows = [];
    }

    /// Reads the rows again: in place when the theme's keys are the same, else afresh.
    public void Refresh()
    {
        if (_values is not { } values) return;
        var byKey = values.Rows().ToDictionary(row => row.Key);
        var rows = SensorNames.Grouped(byKey.Keys).SelectMany(group => group).Select(name => byKey[name.Key]).ToList();
        if (rows.Select(row => row.Key).SequenceEqual(Rows.Select(row => row.Key)))
        {
            for (var i = 0; i < rows.Count; i++) Rows[i].Update(rows[i], values.TestValues.PinnedNow);
        }
        else
        {
            Rows = rows.Select(row => new TestValueRowViewModel(this, row, values.TestValues.PinnedNow)).ToList();
        }

        TrayNotRunning = values.TrayNotRunning;
        AnyPinned = values.TestValues.Pins.Count > 0 || values.TestValues.PinnedNow is not null;
    }

    void OnUpdated(object? sender, EventArgs e) => Refresh();
}
