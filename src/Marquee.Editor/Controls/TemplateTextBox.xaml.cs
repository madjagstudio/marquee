using System.Windows;
using Visibility = System.Windows.Visibility;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Marquee.Core.Expressions;
using Marquee.Core.Sensors;
using Marquee.Editor.Model;
using Marquee.Editor.Model.Values;
using Marquee.Editor.Mvvm;
using Marquee.Editor.Properties;

namespace Marquee.Editor.Controls;

/// Text stays editable; each placeholder is one WPF embedded element, so selection and deletion are atomic.
public partial class TemplateTextBox : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(TemplateTextBox),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextPropertyChanged));
    public static readonly DependencyProperty SourcesProperty = DependencyProperty.Register(nameof(Sources), typeof(ValueSources), typeof(TemplateTextBox),
        new PropertyMetadata(null, OnSourcesChanged));
    public static readonly DependencyProperty LocaleProperty = DependencyProperty.Register(nameof(Locale), typeof(TimeLocale), typeof(TemplateTextBox),
        new PropertyMetadata(null, (d, _) => ((TemplateTextBox)d).RefreshLive()));
    public static readonly DependencyProperty ShowClockFormatsProperty = DependencyProperty.Register(nameof(ShowClockFormats), typeof(bool), typeof(TemplateTextBox),
        new PropertyMetadata(false, (d, _) => ((TemplateTextBox)d).RefreshDocument()));
    public static readonly DependencyProperty ValueSourceProperty = DependencyProperty.Register(nameof(ValueSource), typeof(string), typeof(TemplateTextBox),
        new PropertyMetadata(null, (d, _) => ((TemplateTextBox)d).RefreshLive()));

    bool _updating;
    bool _writing;
    TokenKind _insertKind;
    TextPointer? _insertPosition;
    readonly List<FormatChoice> _times = [];
    readonly List<FormatChoice> _dates = [];

    public TemplateTextBox()
    {
        InitializeComponent();
        DataObject.AddPastingHandler(Input, OnPaste);
        // Clipboard text is literal, never executable template syntax or foreign rich-text UI.
        Input.AllowDrop = false;
        _times.AddRange(Enum.GetValues<TimeFormat>().Select(format => new FormatChoice(TextTemplate.Pattern(format), format switch
        {
            TimeFormat.Hour12 => "12-hour", TimeFormat.Hour12Seconds => "12-hour with seconds",
            TimeFormat.Hour24 => "24-hour", _ => "24-hour with seconds",
        })));
        _dates.AddRange(Enum.GetValues<DateFormat>().Select(format => new FormatChoice(TextTemplate.Pattern(format), format switch
        {
            DateFormat.Long => "Long date", DateFormat.Short => "Short date", DateFormat.MonthDay => "Month and day", _ => "Weekday",
        })));
        TimeChoice.ItemsSource = _times;
        DateChoice.ItemsSource = _dates;
        RefreshDocument();
    }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public ValueSources? Sources { get => (ValueSources?)GetValue(SourcesProperty); set => SetValue(SourcesProperty, value); }
    public TimeLocale? Locale { get => (TimeLocale?)GetValue(LocaleProperty); set => SetValue(LocaleProperty, value); }
    public bool ShowClockFormats { get => (bool)GetValue(ShowClockFormatsProperty); set => SetValue(ShowClockFormatsProperty, value); }
    public string? ValueSource { get => (string?)GetValue(ValueSourceProperty); set => SetValue(ValueSourceProperty, value); }

    static void OnTextPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (TemplateTextBox)d;
        if (!box._writing) box.RefreshDocument();
    }

    static void OnSourcesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (TemplateTextBox)d;
        if (e.OldValue is ValueSources old) WeakEventManager<ValueSources, EventArgs>.RemoveHandler(old, nameof(ValueSources.Updated), box.OnValuesUpdated);
        if (e.NewValue is ValueSources sources) WeakEventManager<ValueSources, EventArgs>.AddHandler(sources, nameof(ValueSources.Updated), box.OnValuesUpdated);
        box.RefreshLive();
    }

    void OnValuesUpdated(object? sender, EventArgs e) => RefreshLive();

    void RefreshDocument()
    {
        if (Input is null) return;
        _updating = true;
        try
        {
            IReadOnlyList<TemplatePart> parts;
            try { parts = TextTemplate.Parse(Text); }
            catch (ExpressionException) { parts = [new TemplatePart.Literal(Text)]; }
            var paragraph = new Paragraph { Margin = new Thickness(0) };
            foreach (var part in parts)
                paragraph.Inlines.Add(part is TemplatePart.Literal literal ? new Run(literal.Text) : Chip((TemplatePart.Token)part));
            Input.Document = new FlowDocument(paragraph) { PagePadding = new Thickness(0) };
            Input.CaretPosition = paragraph.ContentEnd;
            RefreshClock(parts);
        }
        finally { _updating = false; }
        RefreshLive();
    }

    void RefreshClock(IReadOnlyList<TemplatePart> parts)
    {
        var clock = ShowClockFormats && parts.OfType<TemplatePart.Token>().Any()
            && parts.All(p => p is TemplatePart.Token { Key: "now", Kind: TokenKind.Time or TokenKind.Date }
                || p is TemplatePart.Literal l && string.IsNullOrWhiteSpace(l.Text));
        ClockPanel.Visibility = clock ? Visibility.Visible : Visibility.Collapsed;
        TextPanel.Visibility = clock ? Visibility.Collapsed : Visibility.Visible;
        TimePanel.Visibility = parts.OfType<TemplatePart.Token>().Any(t => t.Kind == TokenKind.Time) ? Visibility.Visible : Visibility.Collapsed;
        DatePanel.Visibility = parts.OfType<TemplatePart.Token>().Any(t => t.Kind == TokenKind.Date) ? Visibility.Visible : Visibility.Collapsed;
        SelectClockFormat(TimeChoice, _times, parts.OfType<TemplatePart.Token>().FirstOrDefault(t => t.Kind == TokenKind.Time), "t");
        SelectClockFormat(DateChoice, _dates, parts.OfType<TemplatePart.Token>().FirstOrDefault(t => t.Kind == TokenKind.Date), "d");
    }

    static void SelectClockFormat(ComboBox box, List<FormatChoice> choices, TemplatePart.Token? token, string defaultPattern)
    {
        var pattern = token is null ? null : token.Format ?? defaultPattern;
        box.ItemsSource = pattern is not null && choices.All(c => c.Pattern != pattern)
            ? new[] { new FormatChoice(pattern, "Current format") }.Concat(choices).ToList() : choices;
        box.SelectedValue = pattern;
    }

    InlineUIContainer Chip(TemplatePart.Token token, TextPointer? position = null)
    {
        var button = new Button { Tag = token, Padding = new Thickness(5, 2, 5, 2), Margin = new Thickness(2, 0, 2, 0),
            ToolTip = "Change this value's format or remove it.", Content = Label(token) };
        var chip = position is null ? new InlineUIContainer(button) : new InlineUIContainer(button, position);
        chip.BaselineAlignment = BaselineAlignment.Center;
        button.Click += (_, _) =>
        {
            var menu = new ContextMenu();
            var choices = token.Kind switch
            {
                TokenKind.Time => _times, TokenKind.Date => _dates,
                _ => new List<FormatChoice> { new("0", "0 decimals"), new("0.0", "1 decimal"), new("0.00", "2 decimals") },
            };
            // Helper-based readings retain their helper; changing them to numeric format would be invalid.
            if (token.Kind != TokenKind.Reading || !TextTemplate.Write([token]).Contains('|'))
                foreach (var choice in choices)
                {
                    var item = new MenuItem { Header = choice.Label, ToolTip = "Use this format for this value." };
                    item.Click += (_, _) => ReplaceChip(chip, TextTemplate.ChangeTokenFormat(token, choice.Pattern));
                    menu.Items.Add(item);
                }
            var remove = new MenuItem { Header = "Remove value", ToolTip = "Remove this whole value from the text." };
            remove.Click += (_, _) => { chip.SiblingInlines.Remove(chip); Publish(); };
            menu.Items.Add(remove);
            button.ContextMenu = menu;
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        };
        return chip;
    }

    void ReplaceChip(InlineUIContainer chip, IReadOnlyList<TemplatePart> parts)
    {
        _updating = true;
        foreach (var part in parts)
            chip.SiblingInlines.InsertBefore(chip, part is TemplatePart.Literal literal
                ? new Run(literal.Text) : Chip((TemplatePart.Token)part));
        chip.SiblingInlines.Remove(chip);
        _updating = false;
        Publish();
    }

    string Label(TemplatePart.Token token)
    {
        // In value-widget spans {0} reads the owning widget's source. This substitution is display-only.
        if (token.Key == "0" && ValueSource is { } source) token = token with { Key = source };
        var name = token.Kind switch { TokenKind.Time => "Time", TokenKind.Date => "Date",
            _ => ValuePicker.Read(token.Key) is PickedValue.Number ? "Number" : SensorNames.InText(token.Key) };
        if (token.Kind == TokenKind.Reading && Sources is { } sources)
        {
            var keys = Marquee.Core.Expressions.Template.Parse(TextTemplate.Write([token])).Keys;
            var rows = sources.Rows(keys);
            if (rows.Any(row => row.Source == ValueSourceKind.Sample || row.Stale || row.Value is null)) return name + " — no reading here";
            var entries = rows.ToDictionary(row => row.Key, row => new SensorEntry(row.Value!.Value, DateTimeOffset.UtcNow, null, []));
            var rendered = Marquee.Core.Expressions.Template.Parse(TextTemplate.Write([token])).Render(SensorSnapshot.Of(DateTimeOffset.UtcNow, entries), Locale).Text;
            return name + " " + rendered + (!TextTemplate.Write([token]).Contains('|')
                && SensorNames.For(token.Key).Unit is { } unit ? " " + unit : "");
        }
        if (token.Kind == TokenKind.Reading) return name + " — no reading here";
        return name + " " + Marquee.Core.Expressions.Template.Parse(TextTemplate.Write([token])).Render(Sources?.Snapshot()
            ?? SensorSnapshot.Of(DateTimeOffset.UtcNow, new Dictionary<string, SensorEntry>()), Locale).Text;
    }

    void RefreshLive()
    {
        if (Input is null) return;
        foreach (var chip in Input.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<InlineUIContainer>())
            if (chip.Child is Button { Tag: TemplatePart.Token token } button) button.Content = Label(token);
        var now = Sources?.Snapshot().Now ?? DateTimeOffset.UtcNow;
        foreach (var choice in _times.Concat(_dates).Concat(TimeChoice.Items.Cast<FormatChoice>()).Concat(DateChoice.Items.Cast<FormatChoice>()).Distinct())
            choice.Preview = Formatting.Time(now.ToUnixTimeSeconds(), choice.Pattern, Locale ?? TimeLocale.Pc);
    }

    IEnumerable<TemplatePart> ReadInlines(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Run run) yield return new TemplatePart.Literal(run.Text);
            else if (inline is LineBreak) yield return new TemplatePart.Literal("\n");
            else if (inline is InlineUIContainer { Child: FrameworkElement { Tag: TemplatePart.Token token } }) yield return token;
            else if (inline is Span span)
                foreach (var part in ReadInlines(span.Inlines)) yield return part;
        }
    }

    void Publish()
    {
        if (_updating) return;
        var text = string.Join("\n", Input.Document.Blocks.OfType<Paragraph>().Select(p => TextTemplate.Write(ReadInlines(p.Inlines))));
        _writing = true;
        try
        {
            SetCurrentValue(TextProperty, text);
            GetBindingExpression(TextProperty)?.UpdateSource();
        }
        finally { _writing = false; }
    }

    void Commit() { if (DataContext is PropertyRowViewModel row) row.Commit(); }
    void OnTextChanged(object sender, TextChangedEventArgs e) => Publish();
    void OnInputLostFocus(object sender, KeyboardFocusChangedEventArgs e) => Commit();

    void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Back or Key.Delete && Input.Selection.IsEmpty)
        {
            var direction = e.Key == Key.Back ? LogicalDirection.Backward : LogicalDirection.Forward;
            var cursor = Input.CaretPosition;
            // Skip inline boundary markers, but never a literal character or paragraph boundary.
            while (cursor.GetPointerContext(direction) is TextPointerContext.ElementStart or TextPointerContext.ElementEnd)
            {
                if (cursor.GetAdjacentElement(direction) is InlineUIContainer chip)
                {
                    chip.SiblingInlines.Remove(chip);
                    Publish();
                    Commit();
                    e.Handled = true;
                    return;
                }
                if (cursor.GetAdjacentElement(direction) is not Run) break;
                var next = cursor.GetNextContextPosition(direction);
                if (next is null) break;
                cursor = next;
            }
        }
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { Commit(); e.Handled = true; }
        if (e.Key == Key.Escape && DataContext is PropertyRowViewModel row) { row.Revert(); e.Handled = true; }
    }

    void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetData(DataFormats.UnicodeText) is string text) Input.Selection.Text = text;
        e.CancelCommand();
    }

    public void InsertReading(string key) => Insert(new TemplatePart.Token(key, "0", TokenKind.Reading));
    public void InsertTime(TimeFormat format) => Insert(new TemplatePart.Token("now", TextTemplate.Pattern(format), TokenKind.Time));
    public void InsertDate(DateFormat format) => Insert(new TemplatePart.Token("now", TextTemplate.Pattern(format), TokenKind.Date));

    void Insert(TemplatePart.Token token)
    {
        _updating = true;
        if (_insertPosition is { } saved) Input.CaretPosition = saved;
        Input.Selection.Text = "";
        var at = Input.CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
        var chip = Chip(token, at);
        Input.CaretPosition = chip.ElementEnd;
        _insertPosition = null;
        _updating = false;
        Publish();
        Commit();
        InsertPopup.IsOpen = false;
        _updating = true;
        try { RefreshClock(TextTemplate.Parse(Text)); }
        finally { _updating = false; }
        if (TextPanel.Visibility == Visibility.Visible) Input.Focus();
        else if (TimePanel.Visibility == Visibility.Visible) TimeChoice.Focus();
        else DateChoice.Focus();
    }

    void OnInsertClick(object sender, RoutedEventArgs e)
    {
        _insertPosition = Input.CaretPosition;
        PickPanel.Visibility = Visibility.Visible;
        FormatPanel.Visibility = Visibility.Collapsed;
        if (Sources is { } sources)
        {
            var picker = new ValuePickerViewModel(sources, null, false) { SensorsOnly = true };
            picker.Committed += (_, _) => { if (picker.SelectedKey is { } key) InsertReading(key); };
            ReadingPicker.DataContext = picker;
        }
        InsertPopup.IsOpen = true;
    }

    void OnPopupOpened(object? sender, EventArgs e) => RefreshLive();
    void OnPopupClosed(object? sender, EventArgs e) => _insertPosition = null;
    void OnPickTime(object sender, RoutedEventArgs e) => ShowFormats(TokenKind.Time);
    void OnPickDate(object sender, RoutedEventArgs e) => ShowFormats(TokenKind.Date);
    void ShowFormats(TokenKind kind)
    {
        _insertKind = kind;
        PickPanel.Visibility = Visibility.Collapsed;
        FormatPanel.Visibility = Visibility.Visible;
        FormatHeading.Text = kind == TokenKind.Time ? "Time format" : "Date format";
        FormatChoices.SelectedItem = null;
        FormatChoices.ItemsSource = kind == TokenKind.Time ? _times : _dates;
    }
    void OnFormatBack(object sender, RoutedEventArgs e) { PickPanel.Visibility = Visibility.Visible; FormatPanel.Visibility = Visibility.Collapsed; }
    void OnInsertFormat(object sender, SelectionChangedEventArgs e)
    {
        if (FormatChoices.SelectedItem is FormatChoice choice) Insert(new TemplatePart.Token("now", choice.Pattern, _insertKind));
    }
    void OnClockFormatChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || sender is not ComboBox { SelectedItem: FormatChoice choice }) return;
        SetCurrentValue(TextProperty, TextTemplate.ChangeFormat(Text, ReferenceEquals(sender, TimeChoice) ? TokenKind.Time : TokenKind.Date, choice.Pattern));
        GetBindingExpression(TextProperty)?.UpdateSource();
        Commit();
    }
}

public sealed class FormatChoice(string pattern, string name) : ObservableObject
{
    string _preview = "";
    public string Pattern { get; } = pattern;
    public string Label => _preview.Length == 0 ? name : name + " — " + _preview;
    public string Preview { set { if (Set(ref _preview, value)) Raise(nameof(Label)); } }
}
