using Marquee.Core.Themes.Editing;

namespace Marquee.Editor.Model.Tests.TestSupport;

/// A session on a temp theme with its autosave and opened-version backup, on a fake clock and a manual UI thread, and
/// the editor's data under a temp root: what the editor wires up when it opens a theme.
sealed class Saving : IDisposable
{
    public const string Layout =
        "canvas: { orientation: landscape }\nbackground:\n  - fill: \"#000000\"\n" +
        "widgets:\n  - { type: shape, shape: rect, at: [10, 10], size: [40, 40], style: { fill: \"#FF0000\" } }\n";

    public static readonly YamlPath X = YamlPath.Parse("widgets[0].at[0]");

    readonly bool _ownsTheme;
    readonly bool _ownsData;

    public Saving(TempTheme? theme = null, TempFolder? data = null, FakeClock? clock = null)
    {
        _ownsTheme = theme is null;
        _ownsData = data is null;
        Theme = theme ?? TempTheme.With(Layout);
        Data = data ?? new TempFolder();
        Clock = clock ?? new FakeClock();
        Folders = new EditorFolders(Data.Combine("editor"));
        Session = Theme.Open(new EditorSessionOptions { Time = Clock });
        File = LayoutFile.For(Session);
        Backup = OpenedBackup.For(Session, Folders);
        Autosaver = new Autosaver(Session, File, Backup, Ui, Clock);
        // A flush waits as long as the save takes: no test hangs on how busy the machine is. Tests of the bound pass one.
        Autosaver.FlushWait = Timeout.InfiniteTimeSpan;
    }

    public TempTheme Theme { get; }

    public TempFolder Data { get; }

    public FakeClock Clock { get; }

    public ManualContext Ui { get; } = new();

    public EditorFolders Folders { get; }

    public EditorSession Session { get; }

    public LayoutFile File { get; }

    public OpenedBackup Backup { get; }

    public Autosaver Autosaver { get; }

    /// Moves widget 0 to `x`.
    public void Move(int x) => Session.Apply(new SetValue(X, x.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    /// The layout with widget 0 moved to `x`.
    public static string Moved(int x) => Layout.Replace("at: [10, 10]", $"at: [{x}, 10]");

    public void Advance(double seconds) => Clock.Advance(TimeSpan.FromSeconds(seconds));

    public string OnDisk => System.IO.File.ReadAllText(Theme.LayoutPath);

    public void Dispose()
    {
        Autosaver.Dispose();
        Session.Dispose();
        if (_ownsTheme) Theme.Dispose();
        if (_ownsData) Data.Dispose();
    }
}
