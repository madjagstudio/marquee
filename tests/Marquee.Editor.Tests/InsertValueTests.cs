using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Marquee.Editor.Controls;
using Marquee.Editor.Model;
using Marquee.Editor.Properties;
using Marquee.Editor.Tests.TestSupport;

namespace Marquee.Editor.Tests;

public class InsertValueTests
{
    [Fact]
    public void Cutting_and_pasting_a_chip_preserves_the_template() => Sta.Run(() =>
    {
        const string template = "CPU {cpu.temp:0} end";
        var box = new TemplateTextBox { Text = template };
        WithWindow(box, () =>
        {
            var input = (RichTextBox)box.FindName("Input");
            var chip = Assert.Single(((Paragraph)input.Document.Blocks.FirstBlock).Inlines.OfType<InlineUIContainer>());
            input.Selection.Select(chip.ElementStart, chip.ElementEnd);
            input.Copy();
            Assert.Equal("{cpu.temp:0}", Clipboard.GetText());
            Assert.Equal(template, box.Text);
            input.Cut();
            Assert.Equal("{cpu.temp:0}", Clipboard.GetText());
            Assert.Equal("CPU  end", box.Text);
            input.Paste();
            Assert.Equal(template, box.Text);
            Assert.Single(((Paragraph)input.Document.Blocks.FirstBlock).Inlines.OfType<InlineUIContainer>());
        });
    });

    [Theory]
    [InlineData("CPU {cpu.temp:0}°", "CPU {cpu.temp:0}°", 1)]
    [InlineData("CPU temperature", "CPU temperature", 0)]
    [InlineData("CPU {not a token}", "CPU {{not a token}}", 0)]
    public void Pasting_external_text_parses_tokens_and_keeps_literal_text(string text, string expected, int chips) => Sta.Run(() =>
    {
        var box = new TemplateTextBox();
        WithWindow(box, () =>
        {
            var input = (RichTextBox)box.FindName("Input");
            Clipboard.SetText(text);
            input.Paste();
            Assert.Equal(expected, box.Text);
            Assert.Equal(chips, ((Paragraph)input.Document.Blocks.FirstBlock).Inlines.OfType<InlineUIContainer>().Count());
        });
    });

    [Fact]
    public void Inserting_a_reading_keeps_the_literal_and_writes_default_decimals() => Sta.Run(() =>
    {
        var box = new TemplateTextBox { Text = "CPU " };
        box.InsertReading("cpu.temp");
        Assert.Equal("CPU {cpu.temp:0}", box.Text);
    });

    [Fact]
    public void Backspace_after_a_chip_removes_the_whole_token() => Sta.Run(() =>
    {
        var box = new TemplateTextBox { Text = "CPU {cpu.temp:0}" };
        WithWindow(box, () =>
        {
            var input = (RichTextBox)box.FindName("Input");
            var chip = Assert.Single(((Paragraph)input.Document.Blocks.FirstBlock).Inlines.OfType<InlineUIContainer>());
            input.CaretPosition = chip.ElementEnd;
            input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(box), 0, Key.Back)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert.Equal("CPU ", box.Text);
        });
    });

    [Theory]
    [InlineData("CPU {cpu.temp:0.0}", "CPU {gpu.temp:0.0}")]
    [InlineData("Temperature ", "Temperature {gpu.temp:0}")]
    public void What_it_shows_changes_or_appends_the_reading_in_text(string original, string expected) => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = Open(environment, original);
        var row = editor.Model.Properties.Row("text")!;
        row.TextPicker!.SelectedKey = "gpu.temp";
        Assert.Equal(expected, row.Text);
        Assert.True(editor.Session.Document.TryGetScalar(OpenEditor.P("widgets[0].text"), out var saved));
        Assert.Equal(expected, saved);
        Assert.True(editor.Session.Undo());
        Assert.Equal(original, row.Text);
        Assert.DoesNotContain(editor.Model.Properties.Basics, item => item.Name == "source");
    });

    [Fact]
    public void Multiple_readings_show_chips_instead_of_a_single_picker() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = Open(environment, "{cpu.temp:0} {gpu.temp:0}");
        Assert.False(editor.Model.Properties.Row("text")!.ShowTextPicker);
    });

    [Theory]
    [InlineData("{cpu.load + 2:0}", "{gpu.temp:0}")]
    [InlineData("{42:0.0}", "{gpu.temp:0.0}")]
    [InlineData("{0}", "{gpu.temp}")]
    public void Non_sensor_tokens_start_with_an_unset_sensor_pick(string original, string expected) => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = Open(environment, original);
        var row = editor.Model.Properties.Row("text")!;
        Assert.True(row.ShowTextPicker);
        Assert.Equal(ValuePickerMode.Sensor, row.TextPicker!.Mode);
        Assert.Null(row.TextPicker.SelectedKey);
        row.TextPicker.SelectedKey = "gpu.temp";
        Assert.Equal(expected, row.Text);
        Assert.True(editor.Session.Undo());
        Assert.Equal(original, row.Text);
        Assert.Equal(ValuePickerMode.Sensor, row.TextPicker.Mode);
        Assert.Null(row.TextPicker.SelectedKey);
    });

    [Theory]
    [InlineData("gpu.vram_used", "GPU memory used")]
    [InlineData("ram.used", "Memory used")]
    public void Helper_chips_do_not_append_a_second_unit(string key, string name) => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = Open(environment, "{" + key + "|bytes}");
        var reading = editor.Model.TestValues.Row(key)!;
        reading.NumberText = "9126805504";
        Assert.True(reading.CommitNumber());
        var box = new TemplateTextBox { Text = "{" + key + "|bytes}", Sources = editor.Workspace.Values };
        var input = (RichTextBox)box.FindName("Input");
        var chip = (Button)((Paragraph)input.Document.Blocks.FirstBlock).Inlines.OfType<InlineUIContainer>().Single().Child;
        Assert.Equal(name + " 8.5 GB", chip.Content);
    });

    [Theory]
    [InlineData("{0}", "Number 0")]
    [InlineData("{42:0.0}", "Number 42.0")]
    public void Numeric_chips_use_a_plain_fallback_label(string text, string expected) => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = Open(environment, text);
        var box = new TemplateTextBox { Text = text, Sources = editor.Workspace.Values };
        var input = (RichTextBox)box.FindName("Input");
        var chip = (Button)((Paragraph)input.Document.Blocks.FirstBlock).Inlines.OfType<InlineUIContainer>().Single().Child;
        Assert.Equal(expected, chip.Content);
    });

    [Fact]
    public void Insert_popup_inserts_a_date_after_choosing_its_format() => Sta.Run(() =>
    {
        var box = new TemplateTextBox { Text = "Today " };
        WithWindow(box, () =>
        {
            ((Button)box.FindName("InsertButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((StackPanel)box.FindName("PickPanel")).Children.OfType<Button>().Single(b => Equals(b.Content, "Date"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Today ", box.Text);
            var choices = (ListBox)box.FindName("FormatChoices");
            Assert.Equal(4, choices.Items.Count);
            Assert.All(choices.Items.Cast<FormatChoice>(), c => Assert.Contains(" — ", c.Label));
            choices.SelectedIndex = 3;
            Assert.Equal("Today {now|date:dddd}", box.Text);
            Assert.False(((System.Windows.Controls.Primitives.Popup)box.FindName("InsertPopup")).IsOpen);
        });
    });

    [Fact]
    public void Insert_popup_reading_picker_inserts_at_the_saved_cursor() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = Open(environment, "CPU after");
        var box = new TemplateTextBox { Text = "CPU after", Sources = editor.Workspace.Values };
        WithWindow(box, () =>
        {
            var input = (RichTextBox)box.FindName("Input");
            var run = Assert.IsType<Run>(((Paragraph)input.Document.Blocks.FirstBlock).Inlines.FirstInline);
            input.CaretPosition = run.ContentStart.GetPositionAtOffset(4)!;
            ((Button)box.FindName("InsertButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var picker = (FrameworkElement)box.FindName("ReadingPicker");
            Assert.Equal("CPU after", box.Text);
            var list = Descendants(picker).OfType<ListBox>().Single();
            list.SelectedItem = list.Items.Cast<ValuePickerKey>().Single(k => k.Key == "cpu.temp");
            Assert.Equal("CPU {cpu.temp:0}after", box.Text);
            Assert.False(((System.Windows.Controls.Primitives.Popup)box.FindName("InsertPopup")).IsOpen);
        });
    });

    [Fact]
    public void Insert_popup_asks_for_a_time_format_before_inserting_at_the_cursor() => Sta.Run(() =>
    {
        var box = new TemplateTextBox { Text = "Before after" };
        WithWindow(box, () =>
        {
            var input = (RichTextBox)box.FindName("Input");
            var run = Assert.IsType<Run>(((Paragraph)input.Document.Blocks.FirstBlock).Inlines.FirstInline);
            input.CaretPosition = run.ContentStart.GetPositionAtOffset(7)!;
            ((Button)box.FindName("InsertButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var pick = (StackPanel)box.FindName("PickPanel");
            Assert.Equal(new[] { "Time", "Date" }, pick.Children.OfType<Button>().Select(b => b.Content));
            pick.Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Before after", box.Text);
            var choices = (ListBox)box.FindName("FormatChoices");
            Assert.Equal(4, choices.Items.Count);
            Assert.All(choices.Items.Cast<FormatChoice>(), c => Assert.Contains(" — ", c.Label));
            choices.SelectedIndex = 2;
            Assert.Equal("Before {now|time:HH:mm}after", box.Text);
            input.Selection.Text = "X";
            Assert.Equal("Before {now|time:HH:mm}Xafter", box.Text);
            var chip = Assert.Single(((Paragraph)input.Document.Blocks.FirstBlock).Inlines.OfType<InlineUIContainer>());
            ((Button)chip.Child).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var menu = ((Button)chip.Child).ContextMenu!;
            ((MenuItem)menu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.IsOpen = false;
            Assert.Equal("Before {now|time:h:mm tt}Xafter", box.Text);
        });
    });

    [Fact]
    public void A_reading_chip_shows_live_values_and_its_decimal_menu_changes_only_that_token() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = Open(environment, "CPU {cpu.temp:0} {{degrees}}");
        var row = editor.Model.Properties.Row("text")!;
        var box = new TemplateTextBox { Text = row.Text, Sources = editor.Workspace.Values };
        WithWindow(box, () =>
        {
            var input = (RichTextBox)box.FindName("Input");
            Button Chip() => (Button)((Paragraph)input.Document.Blocks.FirstBlock).Inlines.OfType<InlineUIContainer>().Single().Child;
            Assert.Contains("no reading here", (string)Chip().Content);
            var reading = editor.Model.TestValues.Row("cpu.temp")!;
            reading.NumberText = "52";
            Assert.True(reading.CommitNumber());
            Assert.True(Sta.PumpUntil(() => Equals(Chip().Content, "CPU temperature 52 °C")));
            Assert.Equal("CPU temperature 52 °C", Chip().Content);
            Chip().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var menu = Chip().ContextMenu!;
            Assert.Equal(new[] { "0 decimals", "1 decimal", "2 decimals", "Remove value" }, menu.Items.Cast<MenuItem>().Select(i => i.Header));
            ((MenuItem)menu.Items[2]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.IsOpen = false;
            Assert.Equal("CPU {cpu.temp:0.00} {{degrees}}", box.Text);
            Assert.Equal("CPU temperature 52.00 °C", Chip().Content);
        });
    });

    [Fact]
    public void Selecting_across_a_chip_and_pasting_braces_replaces_it_with_literal_text() => Sta.Run(() =>
    {
        var box = new TemplateTextBox { Text = "CPU {cpu.temp:0} end" };
        WithWindow(box, () =>
        {
            var input = (RichTextBox)box.FindName("Input");
            var chip = ((Paragraph)input.Document.Blocks.FirstBlock).Inlines.OfType<InlineUIContainer>().Single();
            input.Selection.Select(chip.ElementStart, chip.ElementEnd);
            input.Selection.Text = "{literal}";
            Assert.Equal("CPU {{literal}} end", box.Text);
        });
    });

    [Fact]
    public void Clock_basics_show_format_choices_and_keep_the_date_when_time_changes() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = Open(environment, "{now|time:h:mm tt} {now|date:dddd}");
        var panel = new PropertiesView { DataContext = editor.Model.Properties };
        WithWindow(panel, () =>
        {
            var box = Descendants(panel).OfType<TemplateTextBox>().First(b => b.IsVisible);
            Assert.False(((RichTextBox)box.FindName("Input")).IsVisible);
            var time = (ComboBox)box.FindName("TimeChoice");
            var date = (ComboBox)box.FindName("DateChoice");
            Assert.True(time.IsVisible);
            Assert.True(date.IsVisible);
            Assert.True(((Button)box.FindName("InsertButton")).IsVisible);
            time.SelectedValue = TextTemplate.Pattern(TimeFormat.Hour24);
            Sta.Pump();
            Assert.Equal("{now|time:HH:mm} {now|date:dddd}", editor.Model.Properties.Row("text")!.Text);
        });
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Editing_a_combined_clock_keeps_its_date_in_basics_and_in_chips(bool clockBasics) => Sta.Run(() =>
    {
        var box = new TemplateTextBox { Text = "{now|time:F}", ShowClockFormats = clockBasics };
        WithWindow(box, () =>
        {
            if (clockBasics) ((ComboBox)box.FindName("TimeChoice")).SelectedValue = "HH:mm";
            else
            {
                var input = (RichTextBox)box.FindName("Input");
                var chip = (Button)((Paragraph)input.Document.Blocks.FirstBlock).Inlines.OfType<InlineUIContainer>().Single().Child;
                chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var menu = chip.ContextMenu!;
                ((MenuItem)menu.Items[2]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                menu.IsOpen = false;
            }
            Assert.Equal("{now|date:D} {now|time:HH:mm}", box.Text);
        });
    });

    [Theory]
    [InlineData("{now|time}", "t")]
    [InlineData("{now|time:HH 'hours'}", "HH 'hours'")]
    public void An_existing_clock_format_is_shown_without_rewriting_it(string text, string pattern) => Sta.Run(() =>
    {
        var box = new TemplateTextBox { Text = text, ShowClockFormats = true };
        WithWindow(box, () =>
        {
            var choice = Assert.IsType<FormatChoice>(((ComboBox)box.FindName("TimeChoice")).SelectedItem);
            Assert.Equal(pattern, choice.Pattern);
            Assert.Contains(" — ", choice.Label);
            Assert.Equal(text, box.Text);
        });
    });

    [Fact]
    public void A_value_widgets_span_chip_names_and_reads_its_own_source() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("value-span",
            "canvas: { orientation: landscape }\nwidgets:\n  - type: value\n    at: [0, 0]\n    size: [300, 60]\n    source: cpu.temp\n    spans: [{ text: '{0:0}' }]\n"));
        editor.Session.Select([OpenEditor.P("widgets[0]")]);
        var reading = editor.Model.TestValues.Row("cpu.temp")!;
        reading.NumberText = "52";
        Assert.True(reading.CommitNumber());
        editor.Model.Properties.MoreStyling.Single(section => section.Title == "Text").Expansion.IsExpanded = true;
        editor.Model.Properties.Row("spans")!.Items[0].Expansion.IsExpanded = true;
        var panel = new PropertiesView { DataContext = editor.Model.Properties };
        WithWindow(panel, () =>
        {
            var box = Descendants(panel).OfType<TemplateTextBox>().First(b => b.Text == "{0:0}" && b.IsVisible);
            var input = (RichTextBox)box.FindName("Input");
            var chip = (Button)((Paragraph)input.Document.Blocks.FirstBlock).Inlines.OfType<InlineUIContainer>().Single().Child;
            Assert.Equal("CPU temperature 52 °C", chip.Content);
            Assert.Equal("{0:0}", box.Text);
        });
    });

    static OpenEditor Open(TestEnvironment environment, string text)
    {
        var editor = new OpenEditor(environment, environment.AddTheme("insert",
            "canvas: { orientation: landscape }\nwidgets:\n  - type: text\n    at: [0, 0]\n    size: [300, 60]\n    text: '" + text.Replace("'", "''") + "'\n"));
        editor.Session.Select([OpenEditor.P("widgets[0]")]);
        return editor;
    }

    static void WithWindow(FrameworkElement content, Action test)
    {
        var window = new Window { Content = content, Width = 500, Height = 700, Left = -20000, Top = -20000,
            ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        try { window.Show(); Sta.Pump(); test(); }
        finally { window.Close(); }
    }

    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
