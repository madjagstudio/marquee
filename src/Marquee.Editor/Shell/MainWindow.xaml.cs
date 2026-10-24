using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Marquee.Core.Themes.Validation;
using Marquee.Editor.Layers;
using Marquee.Editor.Model;
using Marquee.Editor.Properties;
using Marquee.Editor.Resources;
using Marquee.Editor.Values;
using Marquee.Editor.Styles;

namespace Marquee.Editor.Shell;

/// The editor's window: a window bar, Layers/Background/Styles on the left, the central canvas, Properties and
/// TestValues on the right, and a status bar, bound to a MainViewModel. Keys reach the model from here,
/// after the control with focus has had them, so text boxes and menus keep theirs.
public partial class MainWindow : Window
{
    readonly MainViewModel _model;

    public MainWindow(MainViewModel model)
    {
        _model = model;
        DataContext = model;
        InitializeComponent();

        Fill(AddWidgetMenu, [.. model.WidgetChoices(), null, .. model.FileChoices()]);
        LayersPanel.Content = new LayersView { DataContext = model.Layers };
        StylesPanel.Content = new StylesView { DataContext = model.Styles };
        PropertiesPanel.Content = new PropertiesView { DataContext = model.Properties };
        TestValuesPanel.Content = new TestValuesView { DataContext = model.TestValues };
        Fill(ArrangeMenu, model.ArrangeChoices());
        FillProblems();
        FillSave();
        model.PropertyChanged += OnModelChanged;
        model.ShowRequested += (_, _) => BringForward();
        model.BackgroundGalleryRequested += OnBackgroundGallery;

        Loaded += (_, _) =>
        {
            model.Canvas.SetVisible(WindowState != WindowState.Minimized);
            CanvasArea.Focus();
        };
        StateChanged += (_, _) => model.Canvas.SetVisible(WindowState != WindowState.Minimized);
        Activated += (_, _) => model.RefreshScreen();
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        Closing += OnClosing;
        Closed += (_, _) => model.Dispose();
    }

    public MainViewModel Model => _model;

    void OnBackgroundGallery(bool replace)
    {
        using var gallery = _model.CreateBackgroundGallery();
        if (gallery is null) return;
        var window = new Backgrounds.BackgroundGalleryView(gallery) { Owner = this };
        if (window.ShowDialog() == true) _model.ChooseBackground(window.Chosen, window.Still, replace);
    }

    /// Restores the window if it is minimised and brings it to the front.
    public void BringForward()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(MainViewModel.Problems)) FillProblems();
        if (e.PropertyName is null or nameof(MainViewModel.SaveDetail) or nameof(MainViewModel.CanSaveToFolder)) FillSave();
    }

    void OnThemeMenuOpened(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, ThemeMenu)) return;
        ThemeMenu.Items.Clear();
        foreach (var group in _model.ThemeChoices().Where(group => group.Count > 0))
        {
            if (ThemeMenu.Items.Count > 0) ThemeMenu.Items.Add(new Separator());
            foreach (var choice in group) ThemeMenu.Items.Add(Item(choice));
        }

        if (ThemeMenu.Items.Count > 0) ThemeMenu.Items.Add(new Separator());
        var examples = new MenuItem { Header = Text(Strings.NewFromExample), ToolTip = Strings.NewFromExampleTip };
        Fill(examples, _model.ExampleChoices());
        if (examples.Items.Count == 0) examples.Items.Add(new MenuItem { Header = Text(Strings.NoExamples), ToolTip = Strings.NoExamplesTip, IsEnabled = false });
        ThemeMenu.Items.Add(examples);
        ThemeMenu.Items.Add(new Separator());
        foreach (var action in _model.ThemeActions()) ThemeMenu.Items.Add(Item(action));
    }

    /// Each problem, in the parser's or loader's words with its line; one in a widget selects it.
    void FillProblems()
    {
        ProblemsMenu.Items.Clear();
        foreach (var problem in _model.Problems)
        {
            ProblemsMenu.Items.Add(new MenuItem
            {
                Header = ProblemLine(problem),
                ToolTip = Strings.SelectProblemTip,
                Command = _model.SelectProblemCommand,
                CommandParameter = problem,
            });
        }
    }

    /// A problem's line in the list: an error's or a warning's mark, then "layout.yaml line 12: …".
    StackPanel ProblemLine(LayoutProblem problem)
    {
        var error = problem.Severity == IssueSeverity.Error;
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new TextBlock
        {
            Text = error ? "" : "",
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            Foreground = (Brush)FindResource("Warning"),
            Margin = new Thickness(0, 2, 8, 0),
            ToolTip = error ? Strings.ProblemError : Strings.ProblemWarning,
        });
        line.Children.Add(new TextBlock { Text = problem.ToString(), TextWrapping = TextWrapping.Wrap, MaxWidth = 520 });
        return line;
    }

    /// Why the last save failed, and "Save to a folder…" when the theme's folder has gone.
    void FillSave()
    {
        SaveMenu.Items.Clear();
        if (_model.SaveDetail is { } detail)
        {
            SaveMenu.Items.Add(new MenuItem
            {
                Header = new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 },
                StaysOpenOnClick = true,
                ToolTip = Strings.SaveDetailsTip,
            });
        }

        if (_model.CanSaveToFolder) SaveMenu.Items.Add(Item(new MenuChoice(Strings.SaveToFolder, _model.SaveToFolderCommand, Strings.SaveToFolderTip)));
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || IsTyping(e.OriginalSource)) return;
        // Alt arrives as a system key, as do keys pressed with it.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (_model.KeyDown(key, Keyboard.Modifiers)) e.Handled = true;
    }

    void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (!_model.Canvas.IsDragging) return;
        _model.Canvas.ModifiersChanged(Keyboard.Modifiers);
        e.Handled = true;
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_model.TryClose()) e.Cancel = true;
    }

    static bool IsTyping(object source) => source is TextBoxBase or PasswordBox or ComboBox { IsEditable: true };

    static void Fill(MenuItem menu, IEnumerable<MenuChoice?> choices)
    {
        menu.Items.Clear();
        foreach (var choice in choices) menu.Items.Add(choice is null ? new Separator() : Item(choice));
    }

    static MenuItem Item(MenuChoice choice) =>
        new() { Header = Text(choice.Label), ToolTip = choice.ToolTip, Command = choice.Command, CommandParameter = choice.Parameter };

    /// A header as plain text: a theme's name may hold an underscore, which a string header would take for an access key.
    static TextBlock Text(string text) => new() { Text = text };
}
