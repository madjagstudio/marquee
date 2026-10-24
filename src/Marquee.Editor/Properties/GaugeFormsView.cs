using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model;
using Marquee.Editor.Resources;

namespace Marquee.Editor.Properties;

public sealed class GaugeFormsView : WrapPanel
{
    PropertiesViewModel? _owner;
    public GaugeFormsView()
    {
        Loaded += (_, _) => Attach(); Unloaded += (_, _) => Detach(); DataContextChanged += (_, _) => { if (IsLoaded) Attach(); };
    }
    void Detach() { if (_owner is not null) _owner.PropertyChanged -= Changed; _owner = null; }
    void Attach()
    {
        Detach(); Children.Clear();
        if (DataContext is not PropertiesViewModel owner) return;
        _owner = owner; owner.PropertyChanged += Changed;
        foreach (var form in new[] { "dial", "arc", "ring" })
        {
            var button = new ToggleButton { Tag = form, Style = (Style)FindResource("VisualOption"),
                ToolTip = form == "dial" ? Strings.GaugeDialTip : form == "arc" ? Strings.GaugeArcTip : Strings.GaugeRingTip,
                Content = new Image { Source = ChoicePictures.Draw("gauge", form), Width = 76, Height = 64 } };
            button.Click += (_, _) => Choose(form); Children.Add(button);
        }
        Sync();
    }
    void Changed(object? sender, PropertyChangedEventArgs e) => Sync();
    void Sync()
    {
        var owner = _owner;
        Visibility = owner?.Model is { Type: "gauge", Target: PropertyTargetKind.Widgets } ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        var editable = owner?.CanEdit == true && new[] { "start", "sweep", "needle", "ticks", "segments", "thickness" }.All(key => owner.Row(key)?.IsEditable == true);
        foreach (var button in Children.OfType<ToggleButton>())
        {
            button.IsEnabled = editable;
            button.IsChecked = Equals(button.Tag, owner?.Row("needle")?.IsSet == true ? "dial" : owner?.Row("sweep")?.Text == "360" ? "ring" : "arc");
        }
    }
    void Choose(string form)
    {
        if (_owner is not { CanEdit: true, Model: { Type: "gauge" } model } owner || Children.OfType<ToggleButton>().Any(b => !b.IsEnabled)) return;
        owner.Write(() => new EditDocument(Strings.GaugeFormChange, document =>
        {
            foreach (var widget in model.Widgets)
            {
                foreach (var key in new[] { "needle", "ticks", "segments" })
                    if (document.Exists(widget.Path.Child(key))) document = document.RemoveKey(widget.Path.Child(key));
                document = Set(document, widget.Path, "start", form == "ring" ? "-90" : "135");
                document = Set(document, widget.Path, "sweep", form == "ring" ? "360" : "270");
                if (owner.Row("thickness")?.Number == 0) document = Set(document, widget.Path, "thickness", "12");
                if (form == "dial")
                {
                    document = Set(document, widget.Path, "needle", "{ width: 4 }");
                    document = Set(document, widget.Path, "ticks", "{ major: 5 }");
                }
            }
            return document;
        }));
        Sync();
    }
    static LayoutDocument Set(LayoutDocument document, YamlPath widget, string key, string value) => document.Exists(widget.Child(key))
        ? document.ReplaceValue(widget.Child(key), value) : document.AddKey(widget, key, value);
}
