using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Marquee.Core.Sensors;
using Marquee.Editor.Backgrounds;
using Marquee.Editor.Resources;

namespace Marquee.Editor.Properties;

/// Picture and icon choices share the same validated property rows as the raw panel.
public sealed class VisualChoices : WrapPanel
{
    PropertyRowViewModel? _row;
    public static bool Supports(PropertyRowViewModel row) => row.Label is "align" or "case" or "weight" or "italic" or "underline" or "strike" or "blend"
        || row.Label == "kind" && row.Choices.Contains("columns");
    public VisualChoices()
    {
        Loaded += (_, _) => Attach(); Unloaded += (_, _) => Detach(); DataContextChanged += (_, _) => { if (IsLoaded) Attach(); };
    }
    void Detach() { if (_row is not null) _row.PropertyChanged -= Changed; _row = null; }
    void Attach()
    {
        Detach(); Children.Clear();
        if (DataContext is not PropertyRowViewModel row) return;
        _row = row; row.PropertyChanged += Changed;
        var values = row.Label switch
        {
            "align" => new[] { "left", "center", "right" }, "case" => ["none", "upper", "lower"],
            "weight" => ["bold"], "italic" or "underline" or "strike" => ["true"], _ => row.Choices.ToArray(),
        };
        foreach (var value in values)
        {
            var button = new ToggleButton { Tag = value, ToolTip = Tip(row.Label, value), Style = (Style)FindResource("VisualOption") };
            if (row.Label is "blend" or "kind") button.Content = new Image { Source = ChoicePictures.Draw(row.Label, value), Width = 76, Height = 48 };
            else
            {
                var icon = new TextBlock { Text = Icon(row.Label, value), FontSize = 17, VerticalAlignment = VerticalAlignment.Center };
                if (row.Label == "align") icon.FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
                if (row.Label == "weight") icon.FontWeight = FontWeights.Bold;
                if (row.Label == "italic") icon.FontStyle = FontStyles.Italic;
                if (row.Label == "underline") icon.TextDecorations = TextDecorations.Underline;
                if (row.Label == "strike") icon.TextDecorations = TextDecorations.Strikethrough;
                button.Content = icon;
            }
            button.Click += (_, _) =>
            {
                if (!row.IsEditable) return;
                row.Choice = row.Label == "weight" ? (IsBold(row.Text) ? "normal" : "bold")
                    : row.Label is "italic" or "underline" or "strike" ? (row.Text == "true" ? "false" : "true") : value;
                Sync();
            };
            Children.Add(button);
        }
        Sync();
    }
    static bool IsBold(string text) => text == "bold" || double.TryParse(text, out var weight) && weight >= 600;
    void Changed(object? sender, PropertyChangedEventArgs e) => Sync();
    void Sync()
    {
        if (_row is not { } row) return;
        foreach (var button in Children.OfType<ToggleButton>())
        {
            button.IsEnabled = row.IsEditable;
            button.IsChecked = row.IsMixed ? null : row.Label == "weight" ? IsBold(row.Text) : Equals(button.Tag, row.Text);
        }
    }
    static string Icon(string key, string value) => key switch
    {
        "align" => value == "left" ? "\uE8E4" : value == "center" ? "\uE8E3" : "\uE8E2",
        "case" => value == "none" ? Strings.CaseAsWrittenIcon : value == "upper" ? Strings.CaseUpperIcon : Strings.CaseLowerIcon,
        "weight" => Strings.BoldIcon, "italic" => Strings.ItalicIcon, "underline" => Strings.UnderlineIcon, _ => Strings.StrikeIcon,
    };
    static string Tip(string key, string value) => key switch
    {
        "align" => value == "left" ? Strings.TextAlignLeftTip : value == "center" ? Strings.TextAlignCenterTip : Strings.TextAlignRightTip,
        "case" => value == "none" ? Strings.CaseAsWrittenTip : value == "upper" ? Strings.CaseUpperTip : Strings.CaseLowerTip,
        "weight" => Strings.BoldTip, "italic" => Strings.ItalicTip, "underline" => Strings.UnderlineTip, "strike" => Strings.StrikeTip,
        "kind" => value == "line" ? Strings.GraphLineTip : value == "area" ? Strings.GraphAreaTip : Strings.GraphColumnsTip,
        _ => value switch { "normal" => Strings.BlendNormalTip, "multiply" => Strings.BlendMultiplyTip, "screen" => Strings.BlendScreenTip,
            "overlay" => Strings.BlendOverlayTip, "darken" => Strings.BlendDarkenTip, "lighten" => Strings.BlendLightenTip, _ => Strings.BlendAddTip },
    };
}

/// Render thumbnails through the same renderer as the theme. They own no live assets after drawing.
static class ChoicePictures
{
    public static ImageSource Draw(string kind, string value)
    {
        var widgets = kind == "gauge" ?
            "  - { type: gauge, at: [4, 4], size: [88, 88], source: cpu.load, min: 0, max: 100, thickness: 8, " +
            (value == "ring" ? "start: -90, sweep: 360, " : "start: 135, sweep: 270, ") +
            (value == "dial" ? "needle: { width: 4 }, ticks: { major: 5 }, " : "") + "style: { color: \"#36D1DC\", track: \"#454552\" } }\n"
            : kind == "kind" ? "  - { type: graph, at: [4, 4], size: [88, 56], source: cpu.load, min: 0, max: 100, points: 8, kind: " + value + ", style: { color: \"#36D1DC\", stroke: { width: 2 } } }\n"
            : BlendWidgets(value);
        var yaml = "canvas: { orientation: landscape }\nwidgets:\n" + widgets;
        using var tile = new LiveThemeTile("", Path.GetTempPath(), yaml, width: 96, height: 64,
            viewBox: SkiaSharp.SKRect.Create(0, 0, 96, kind == "gauge" ? 96 : 64));
        var store = new SensorStore();
        foreach (var valueInHistory in new[] { 22, 65, 40, 82, 55, 90, 35, 68 }) store.Set("cpu.load", valueInHistory);
        tile.Render(store.Snapshot(), TimeSpan.Zero);
        var bitmap = tile.Bitmap!.Clone(); bitmap.Freeze(); return bitmap;
    }
    static string BlendWidgets(string value)
    {
        // The left pair is unchanged; the right pair shows the mode's result over light and dark strips.
        return "  - { type: shape, shape: rect, at: [0, 0], size: [96, 32], style: { fill: \"#DCA05A\" } }\n" +
            "  - { type: shape, shape: rect, at: [0, 32], size: [96, 32], style: { fill: \"#30304B\" } }\n" +
            "  - { type: shape, shape: ellipse, at: [5, 8], size: [36, 48], style: { fill: \"#36A4BA\" } }\n" +
            "  - { type: shape, shape: ellipse, at: [55, 8], size: [36, 48], style: { fill: \"#36A4BA\", blend: " + value + " } }\n";
    }
}
