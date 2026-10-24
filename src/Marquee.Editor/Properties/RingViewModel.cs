using Marquee.Core.Config;
using Marquee.Core.Logging;
using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model;
using Marquee.Editor.Mvvm;
using Marquee.Host;
using SkiaSharp;

namespace Marquee.Editor.Properties;

/// Ring fields and rules use the same property writers as widgets. The view owns animation timing.
public sealed class RingViewModel : ObservableObject
{
    readonly PropertiesViewModel _panel;
    RingPreview? _preview;
    IReadOnlyList<PropertyRowViewModel> _rows = [];

    public RingViewModel(PropertiesViewModel panel, string? configFile)
    {
        _panel = panel;
        Properties = panel.CreateTarget(PropertyModel.ForRing(panel.Session!.Document));
        Rules = new RulesViewModel(panel, YamlPath.Root.Child("ring"));
        IsOverridden = configFile is not null && ConfigFile.Load(configFile, NullLog.Instance).Ring != RingMode.Theme;
        Refresh();
    }

    public PropertiesViewModel Properties { get; }
    public SectionExpansion Expansion => _panel.ExpansionFor("ring");
    public RulesViewModel Rules { get; }
    public bool IsOverridden { get; }
    public IReadOnlyList<PropertyRowViewModel> Rows => _rows;

    internal void Refresh()
    {
        _preview = _panel.Session!.IsPaused ? null : new RingPreview(_panel.Session.Document);
        Properties.RefreshTarget(PropertyModel.ForRing(_panel.Session.Document));
        var rows = Properties.Rows.Where(row => row.Name != "when").ToList();
        if (!_rows.SequenceEqual(rows)) { _rows = rows; Raise(nameof(Rows)); }
        Rules.Refresh();
    }

    public SKColor[] Colors(TimeSpan elapsed) => _preview?.Colors(_panel.Workspace!.Values.Snapshot(), elapsed) ?? new SKColor[60];
}
