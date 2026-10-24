using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Marquee.Core.Expressions;
using Marquee.Core.Themes;
using Marquee.Editor.Controls;
using Marquee.Editor.Model;
using Marquee.Editor.Model.Values;
using Marquee.Editor.Resources;
using Expression = Marquee.Core.Expressions.Expression;

namespace Marquee.Editor.Properties;

public static class ShaderCostPresentation
{
    public static string State(CostHint cost) => cost.OverBudget ? "TooHeavy" : Ratio(cost) < .5 ? "Light" : "Heavy";
    public static double Ratio(CostHint cost) => cost.Budget.TotalSeconds <= 0 ? 1 : cost.PerFrame.TotalSeconds / cost.Budget.TotalSeconds;
}

public partial class ShaderControlsView : UserControl
{
    ShaderPanelViewModel? _model;
    ValueSources? _sources;
    readonly List<Action> _refresh = [];
    bool _updating;
    string? _shape;
    public ShaderControlsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }
    void Detach()
    {
        if (_model is null) return;
        _model.PropertyChanged -= Changed;
        if (_sources is not null) _sources.Updated -= ValuesChanged;
        _sources = null;
        _model = null; _shape = null;
    }
    void Attach()
    {
        Detach();
        _model = DataContext as ShaderPanelViewModel;
        if (_model?.Owner.Workspace is not { } workspace) return;
        _model.PropertyChanged += Changed;
        _sources = workspace.Values;
        _sources.Updated += ValuesChanged;
        Refresh();
    }
    void Changed(object? sender, PropertyChangedEventArgs e) => Refresh();
    void ValuesChanged(object? sender, EventArgs e) => Refresh();
    void Refresh()
    {
        if (_updating || _model is not { Info: { } info } model || model.Owner.Session is null) return;
        _updating = true;
        try
        {
            var shape = string.Join("|", info.Controls) + info.Reacts;
            if (shape != _shape) { _shape = shape; Build(model, info); }
            IsEnabled = model.IsEditable;
            foreach (var refresh in _refresh) refresh();
        }
        finally { _updating = false; }
    }
    void Build(ShaderPanelViewModel model, ShaderInfo info)
    {
        Rows.Children.Clear(); _refresh.Clear();
        foreach (var control in info.Controls)
        {
            if (control.Kind == ControlKind.Colour)
            {
                var swatch = new Button { Name = "ColourSwatch", Height = 34, Width = 80, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = Strings.ShaderColourTip };
                swatch.Click += (_, _) =>
                {
                    var picker = new ColorPicker { Color = ReadColour(model, control.Uniform), ShowAlpha = false, Width = 280 };
                    var popup = PopupFor(swatch, picker);
                    picker.ColorCommitted += (_, e) =>
                    {
                        if (!ColorText.TryParse(e.Hex, out var color)) return;
                        model.Set(control.Uniform, new[] { color.R, color.G, color.B }.Select(c => (c / 255d).ToString("R", CultureInfo.InvariantCulture)).ToArray());
                        popup.IsOpen = false;
                    };
                    popup.IsOpen = true;
                };
                _refresh.Add(() => swatch.Background = (Brush)new BrushConverter().ConvertFromString(ReadColour(model, control.Uniform))!);
                Rows.Children.Add(swatch);
            }
            else
            {
                var speed = control.Uniform == "speed";
                AddSlider(speed ? "SpeedSlider" : "StrengthSlider", control.Min, control.Max,
                    speed ? Strings.ShaderSlowIcon : Strings.ShaderFaintIcon, speed ? Strings.ShaderFastIcon : Strings.ShaderFullIcon,
                    speed ? Strings.ShaderSpeedTip : Strings.ShaderStrengthTip,
                    () => ReadNumber(model, control.Uniform), (value, final) => model.Set(control.Uniform, [value.ToString("0.###", CultureInfo.InvariantCulture)], merge: !final), speed ? Strings.ShaderSpeedUnit : Strings.PercentUnit, speed ? 1 : 100);
            }
        }
        if (info.Reacts is { } reacts)
        {
            Rows.Children.Add(new TextBlock { Text = Strings.ShaderReacts, Margin = new Thickness(0, 12, 0, 4) });
            var chip = new Button { ToolTip = Strings.ShaderReactTip, HorizontalAlignment = HorizontalAlignment.Left };
            chip.Click += (_, _) =>
            {
                var picker = new ValuePickerViewModel(model.Values, Read(model, reacts.Uniform), true)
                    { SensorsOnly = true, Mode = ValuePickerMode.Sensor };
                var stack = new StackPanel { Width = 300 };
                var nothing = new Button { Content = Strings.ShaderNothing, ToolTip = Strings.ShaderNothingTip };
                stack.Children.Add(nothing); stack.Children.Add(new ValuePickerControl { DataContext = picker });
                var popup = PopupFor(chip, stack);
                nothing.Click += (_, _) => { model.Set(reacts.Uniform, ["0"]); popup.IsOpen = false; };
                picker.Committed += (_, _) => { if (picker.Text is { } text) model.Set(reacts.Uniform, [text]); popup.IsOpen = false; };
                popup.IsOpen = true;
            };
            Rows.Children.Add(chip);
            var meter = new ProgressBar { Minimum = 0, Maximum = 1, Height = 8, Margin = new Thickness(0, 6, 0, 2) };
            Rows.Children.Add(meter);
            var ends = new DockPanel();
            var busy = new TextBlock { Text = Strings.ShaderBusy, HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(busy, Dock.Right); ends.Children.Add(busy); ends.Children.Add(new TextBlock { Text = Strings.ShaderCalm }); Rows.Children.Add(ends);
            Rows.Children.Add(new TextBlock { Text = reacts.Sentence == "Warmer and brighter as it gets busy" ? Strings.ShaderReactionSentence : reacts.Sentence, TextWrapping = TextWrapping.Wrap });
            _refresh.Add(() =>
            {
                Expression expression;
                try { expression = Expression.Parse(Read(model, reacts.Uniform)); }
                catch (ExpressionException)
                {
                    chip.Content = Strings.ShaderMissingValue;
                    meter.Value = 0;
                    meter.IsIndeterminate = true;
                    return;
                }
                var snapshot = model.Values.Snapshot();
                var key = expression.Keys.FirstOrDefault();
                chip.Content = key is null ? Strings.ShaderNothing : (key == "cpu.load" ? Strings.ShaderCpuLoad : SensorNames.For(key).Name) + "  " +
                    (snapshot.TryGet(key, out var value) ? value.ToString() : Strings.ShaderMissingValue);
                var number = expression.Evaluate(snapshot)?.Number;
                meter.IsIndeterminate = number is null;
                meter.Value = Math.Clamp(number ?? 0, 0, 1);
            });
        }
        var costBar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 10, Margin = new Thickness(0, 12, 0, 4) };
        var verdict = new TextBlock { TextWrapping = TextWrapping.Wrap };
        Rows.Children.Add(costBar); Rows.Children.Add(verdict);
        _refresh.Add(() =>
        {
            if (model.Cost is not { } cost) return;
            var state = ShaderCostPresentation.State(cost);
            costBar.Value = Math.Clamp(ShaderCostPresentation.Ratio(cost), 0, 1);
            costBar.Foreground = (Brush)FindResource(state == "Light" ? "Success" : state == "Heavy" ? "Warning" : "Red");
            verdict.Text = state == "Light" ? Strings.ShaderLight : state == "Heavy" ? Strings.ShaderHeavy : Strings.ShaderTooHeavy;
        });
        Rows.Children.Add(new TextBlock { Text = Strings.ShaderQuality });
        AddSlider("QualitySlider", .1, 1, Strings.ShaderFaintIcon, Strings.ShaderFullIcon, Strings.ShaderQualityTip,
            () => model.Owner.Row("scale")?.Number ?? 1, (value, final) => model.Owner.Row("scale")?.SlideTo(value, final), Strings.PercentUnit, 100);
    }
    void AddSlider(string name, double min, double max, string left, string right, string tip, Func<double> read, Action<double, bool> write, string unit, double displayScale = 1)
    {
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var value = new TextBlock { Width = 48, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(value, Dock.Right); row.Children.Add(value);
        var end = new TextBlock { Text = right, Width = 22, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(end, Dock.Right); row.Children.Add(end);
        row.Children.Add(new TextBlock { Text = left, Width = 22, VerticalAlignment = VerticalAlignment.Center });
        var slider = new Slider { Name = name, Minimum = min, Maximum = max, ToolTip = tip };
        var dragging = false;
        slider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => { _model?.Owner.Seal(); dragging = true; }));
        slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => { dragging = false; _model?.Owner.Seal(); }));
        slider.ValueChanged += (_, e) => { if (!_updating) write(e.NewValue, !dragging); };
        row.Children.Add(slider); Rows.Children.Add(row);
        _refresh.Add(() => { var number = read(); slider.Value = number; value.Text = (number * displayScale).ToString("0.##", CultureInfo.CurrentCulture) + unit; });
    }
    Popup PopupFor(UIElement target, UIElement child) => new()
    {
        PlacementTarget = target, Placement = PlacementMode.Bottom, StaysOpen = false,
        Child = new Border { Background = (Brush)FindResource("Panel"), Padding = new Thickness(8), Child = child },
    };
    static string Read(ShaderPanelViewModel model, string name) => model.Uniforms.FirstOrDefault(row => row.Name == name)?.Components[0].Text ?? "0";
    static double ReadNumber(ShaderPanelViewModel model, string name) => double.TryParse(Read(model, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
    static string ReadColour(ShaderPanelViewModel model, string name)
    {
        var row = model.Uniforms.FirstOrDefault(row => row.Name == name);
        var components = row?.Components.Select(c => double.TryParse(c.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? (byte)Math.Clamp(Math.Round(value * 255), 0, 255) : (byte)0).ToArray();
        return components is [var r, var g, var b] ? $"#{r:X2}{g:X2}{b:X2}" : "#000000";
    }
}
