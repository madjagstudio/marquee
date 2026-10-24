using Marquee.Editor.Model;
using Marquee.Editor.Resources;
using Marquee.Editor.Tests.TestSupport;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Marquee.Editor.Properties;
using Marquee.Editor.Shell;
using Marquee.Editor.Styles;

namespace Marquee.Editor.Tests;

public class StylesPanelTests
{
    const string Layout = "canvas: { orientation: landscape }\n" +
        "styles:\n  base: { color: '#123456' }\n  child: { extends: base, size: 24 }\n" +
        "widgets:\n  - { type: text, text: Hello, at: [0, 0], size: [100, 40], style: base }\n" +
        "  - { type: text, text: World, at: [0, 40], size: [100, 40], style: { color: '#abcdef' } }\n";

    [Fact]
    public void Selecting_a_style_shows_heading_extends_and_editable_sections() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine", Layout));
        editor.Model.Styles.Select("child");
        var properties = editor.Model.Properties;
        Assert.Equal(PropertyTargetKind.Style, properties.Target);
        Assert.Equal("Style: child", properties.Heading);
        Assert.Equal("base", properties.Row("extends")!.Choice);
        Assert.NotEmpty(properties.Sections);
        Assert.False(properties.HasSelection);
        var font = properties.Row("style.size")!;
        font.Text = "36";
        Assert.True(font.Commit());
        Assert.Equal("36", Scalar(editor, "styles.child.size"));
        editor.Session.Undo();
        Assert.Equal("24", Scalar(editor, "styles.child.size"));
        editor.Model.Styles.Select("base");
        var extends = properties.Row("extends")!;
        extends.Choice = "child";
        Assert.NotNull(extends.Error);
        Assert.False(editor.Session.Document.Exists(OpenEditor.P("styles.base.extends")));
    });

    [Fact]
    public void New_uses_a_free_name_selects_it_and_starts_rename() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine", Layout));
        editor.Model.Styles.NewCommand.Execute(null);
        Assert.Equal("style", editor.Session.SelectedStyle);
        Assert.True(editor.Model.Styles.Selected!.IsRenaming);
        editor.Model.Styles.NewCommand.Execute(null);
        Assert.Equal("style-2", editor.Session.SelectedStyle);
        Assert.True(editor.Model.Styles.Selected!.IsRenaming);
        Assert.Equal(2, editor.Session.History.Count);
    });

    [Fact]
    public void Rename_validates_live_and_updates_references_in_one_undo_step() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine", Layout));
        var styles = editor.Model.Styles;
        styles.Select("base");
        styles.RenameCommand.Execute(null);
        var item = styles.Selected!;
        item.EditName = "child";
        Assert.NotNull(item.Problem);
        Assert.False(styles.CommitRename(item));
        Assert.True(item.IsRenaming);
        item.EditName = "renamed";
        Assert.Null(item.Problem);
        Assert.True(styles.CommitRename(item));
        Assert.Equal("renamed", editor.Session.SelectedStyle);
        Assert.Equal("renamed", Scalar(editor, "widgets[0].style"));
        Assert.Equal("renamed", Scalar(editor, "styles.child.extends"));
        Assert.Equal(1, editor.Session.History.Count);
        editor.Session.Undo();
        Assert.Equal(Layout, editor.Session.Document.Text);
    });

    [Fact]
    public void Delete_reports_copy_count_and_undo_restores_style_and_users() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine", Layout));
        var styles = editor.Model.Styles;
        styles.Select("base");
        Assert.Equal(Strings.StyleUsedBy(2), styles.Selected!.UsedBy);
        styles.DeleteCommand.Execute(null);
        Assert.Equal(Strings.StyleCopiedInto("base", 2), editor.Model.Message);
        Assert.DoesNotContain(styles.Items, item => item.Name == "base");
        Assert.Equal("#123456", Scalar(editor, "widgets[0].style.color"));
        editor.Session.Undo();
        Assert.Equal(Layout, editor.Session.Document.Text);
        Assert.Equal(Strings.StyleUsedBy(2), styles.Items.Single(item => item.Name == "base").UsedBy);
    });

    [Fact]
    public void Save_as_style_asks_a_validated_name_and_widget_names_it() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine", Layout));
        editor.Session.Select([OpenEditor.P("widgets[1]")]);
        environment.Dialogs.StyleNameAnswer = "saved";
        editor.Model.Properties.SaveAsStyleCommand.Execute(null);
        Assert.Equal([null, null], environment.Dialogs.StyleNameChecks);
        Assert.Equal("saved", Scalar(editor, "widgets[1].style"));
        Assert.Equal("#abcdef", Scalar(editor, "styles.saved.color"));
        Assert.Contains(editor.Model.Styles.Items, item => item.Name == "saved" && item.UsedBy == Strings.StyleUsedBy(1));
        Assert.Equal(1, editor.Session.History.Count);
        editor.Session.Undo();
        Assert.Equal(Layout, editor.Session.Document.Text);
    });

    [Fact]
    public void Save_cancel_and_duplicate_name_leave_document_unchanged() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine", Layout));
        editor.Session.Select([OpenEditor.P("widgets[1]")]);
        editor.Model.Properties.SaveAsStyleCommand.Execute(null);
        Assert.Equal(Layout, editor.Session.Document.Text);
        environment.Dialogs.StyleNameAnswer = "base";
        editor.Model.Properties.SaveAsStyleCommand.Execute(null);
        Assert.NotNull(environment.Dialogs.StyleNameChecks[^1]);
        Assert.Equal(Layout, editor.Session.Document.Text);
        editor.Model.Styles.Select("base");
        Assert.False(editor.Model.Properties.SaveAsStyleCommand.CanExecute(null));
    });

    [Fact]
    public void The_window_shows_style_sections_and_loop_reason_and_routes_rename_keys() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine", Layout));
        var window = new MainWindow(editor.Model)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000, Top = -20000, Width = 1400, Height = 1100,
            ShowActivated = false, ShowInTaskbar = false,
        };
        try
        {
            window.Show();
            editor.Model.IsLayersOpen = true;
            editor.Model.Styles.Select("base");
            Sta.Pump();
            var panel = Assert.IsType<StylesView>(((ContentControl)window.FindName("StylesPanel")).Content);
            Assert.True(panel.IsVisible);
            var properties = Assert.IsType<PropertiesView>(((ContentControl)window.FindName("PropertiesPanel")).Content);
            Assert.Contains(Descendants(properties).OfType<Expander>(), section => section.IsVisible && Equals(section.Header, Strings.SectionText));
            var styling = (ItemsControl)properties.FindName("MoreStylingSections");
            Descendants(styling).OfType<Expander>().Single(section =>
                section.DataContext is PropertySectionViewModel group && group.Rows.Any(row => row.Name == "extends")).IsExpanded = true;
            Sta.Pump();
            var extends = Descendants(styling).OfType<ComboBox>().Single(box => box.DataContext is PropertyRowViewModel { Name: "extends" });
            extends.SelectedItem = "child";
            Sta.Pump();
            var reason = editor.Model.Properties.Row("extends")!.Error;
            Assert.NotNull(reason);
            Assert.Contains(Descendants(properties).OfType<TextBlock>(), text => text.IsVisible && text.Text == reason);

            SendKey(panel, Key.F2);
            Sta.Pump();
            var rename = Descendants(panel).OfType<TextBox>().Single(box => box.IsVisible);
            rename.Text = "child";
            Sta.Pump();
            Assert.NotNull(editor.Model.Styles.Selected!.Problem);
            SendKey(rename, Key.Enter);
            Assert.Equal("base", editor.Session.SelectedStyle);
            rename.Text = "renamed";
            SendKey(rename, Key.Enter);
            Sta.Pump();
            Assert.Equal("renamed", editor.Session.SelectedStyle);
            SendKey(panel, Key.Delete);
            Assert.Equal(Strings.StyleCopiedInto("renamed", 2), editor.Model.Message);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Read_only_styles_can_be_selected_but_not_changed() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        environment.Dialogs.CopyAnswer = Marquee.Editor.Session.CopyChoice.ViewOnly;
        using var editor = new OpenEditor(environment, environment.AddTheme("example", Layout, installed: true));
        editor.Model.Styles.Select("base");
        Assert.Equal("Style: base", editor.Model.Properties.Heading);
        Assert.False(editor.Model.Styles.NewCommand.CanExecute(null));
        Assert.False(editor.Model.Styles.RenameCommand.CanExecute(null));
        Assert.False(editor.Model.Styles.DeleteCommand.CanExecute(null));
        editor.Session.Select([OpenEditor.P("widgets[0]")]);
        Assert.False(editor.Model.Properties.SaveAsStyleCommand.CanExecute(null));
        Assert.Equal(Layout, editor.Session.Document.Text);
    });

    static void SendKey(UIElement target, Key key) => target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
        PresentationSource.FromVisual(target), 0, key) { RoutedEvent = Keyboard.KeyDownEvent });

    static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    static string? Scalar(OpenEditor editor, string path) =>
        editor.Session.Document.TryGetScalar(OpenEditor.P(path), out var value) ? value : null;
}
