using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Marquee.Core.Expressions;
using Marquee.Core.Themes.Editing;
using Marquee.Editor.Controls;
using Marquee.Editor.Model;
using Marquee.Editor.Resources;

namespace Marquee.Editor.Properties;

/// A selected group's clock formats (GroupClocks): the time and date formats of the clocks inside it, such as the Clock
/// and date module's, so it can be made 12-hour without selecting the text inside. Hidden for anything else.
public sealed class ClockFormatsView : StackPanel
{
    readonly ComboBox _time = Choice("GroupTimeFormat", Strings.ClockTimeFormat, Strings.ClockTimeFormatTip);
    readonly ComboBox _date = Choice("GroupDateFormat", Strings.ClockDateFormat, Strings.ClockDateFormatTip);
    PropertiesViewModel? _owner;
    bool _syncing;

    public ClockFormatsView()
    {
        Children.Add(Row(Strings.ClockTimeFormat, _time));
        Children.Add(Row(Strings.ClockDateFormat, _date));
        _time.SelectionChanged += (_, _) => Choose(_time, TokenKind.Time);
        _date.SelectionChanged += (_, _) => Choose(_date, TokenKind.Date);
        Loaded += (_, _) => Attach(); Unloaded += (_, _) => Detach(); DataContextChanged += (_, _) => { if (IsLoaded) Attach(); };
        Visibility = System.Windows.Visibility.Collapsed;
    }

    void Detach() { if (_owner is not null) _owner.PropertyChanged -= Changed; _owner = null; }

    void Attach()
    {
        Detach();
        if (DataContext is not PropertiesViewModel owner) return;
        _owner = owner; owner.PropertyChanged += Changed;
        Sync();
    }

    void Changed(object? sender, PropertyChangedEventArgs e) => Sync();

    /// The selected group and its clocks; none unless one group is selected.
    (YamlPath? Group, IReadOnlyList<YamlPath> Clocks) Clocks() =>
        _owner is { Model: { Target: PropertyTargetKind.Widgets, Type: "group", Widgets: [var group] }, Session: { } session }
            ? (group.Path, GroupClocks.In(session.Document, group.Path)) : (null, []);

    void Sync()
    {
        var (_, clocks) = Clocks();
        Visibility = clocks.Count > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        if (clocks.Count == 0 || _owner is not { Session: { } session } owner) return;
        var document = session.Document;
        _syncing = true;
        try
        {
            Show(_time, FormatChoice.Times(), document, clocks, TokenKind.Time);
            Show(_date, FormatChoice.Dates(), document, clocks, TokenKind.Date);
        }
        finally { _syncing = false; }
        IsEnabled = owner.CanEdit && clocks.All(clock => document.IsEditable(clock.Child("text")));
    }

    /// The row shows when a clock has that part, with the clocks' shared format chosen; a format the list hasn't
    /// stays as "Current format", and clocks that differ choose none.
    void Show(ComboBox box, List<FormatChoice> choices, LayoutDocument document, IReadOnlyList<YamlPath> clocks, TokenKind kind)
    {
        var row = (FrameworkElement)box.Parent;
        row.Visibility = GroupClocks.Has(document, clocks, kind) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        var format = GroupClocks.Format(document, clocks, kind);
        if (format is not null && choices.All(choice => choice.Pattern != format)) choices.Insert(0, new FormatChoice(format, Strings.ClockCurrentFormat));
        var now = (_owner?.Workspace?.Values.Snapshot().Now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        foreach (var choice in choices) choice.Preview = Formatting.Time(now, choice.Pattern, _owner?.Locale ?? TimeLocale.Pc);
        box.ItemsSource = choices;
        box.SelectedValue = format;
    }

    void Choose(ComboBox box, TokenKind kind)
    {
        if (_syncing || box.SelectedItem is not FormatChoice choice || Clocks() is not ({ } group, { Count: > 0 }) || _owner is not { } owner) return;
        owner.Write(() => new EditDocument(Strings.ClockFormatChange,
            document => GroupClocks.Change(document, GroupClocks.In(document, group), kind, choice.Pattern)));
        Sync();
    }

    static ComboBox Choice(string name, string label, string tip)
    {
        var box = new ComboBox { Name = name, DisplayMemberPath = nameof(FormatChoice.Label), SelectedValuePath = nameof(FormatChoice.Pattern), ToolTip = tip };
        AutomationProperties.SetName(box, label);
        return box;
    }

    static DockPanel Row(string label, ComboBox box)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        var text = new TextBlock { Text = label, Width = 108, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(text, Dock.Left);
        row.Children.Add(text);
        row.Children.Add(box);
        return row;
    }
}
