using System.Runtime.CompilerServices;
using Marquee.Core.Themes;
using Marquee.Editor.Model;
using Marquee.Editor.Model.Values;
using Marquee.Editor.Session;
using Marquee.Host;

namespace Marquee.Editor.Tests.TestSupport;

/// The shell's world in a temp folder: the user's themes, the installed examples, config.yaml (not written until a
/// test writes it) and the editor's own folder; no tray, no sensors, and dialogs and a clipboard the test controls.
sealed class TestEnvironment : IDisposable
{
    /// A black canvas with a red box and a blue one, and a group holding a green one.
    public const string Layout =
        "canvas: { orientation: landscape }\n" +
        "background:\n  - fill: \"#000000\"\n" +
        "widgets:\n" +
        "  - { type: shape, shape: rect, at: [100, 100], size: [200, 100], style: { fill: \"#FF0000\" } }\n" +
        "  - { type: shape, shape: rect, at: [500, 100], size: [100, 100], style: { fill: \"#0000FF\" } }\n" +
        "  - type: group\n" +
        "    at: [1000, 100]\n" +
        "    size: [300, 300]\n" +
        "    children:\n" +
        "      - { type: shape, shape: rect, at: [10, 10], size: [50, 50], style: { fill: \"#00FF00\" } }\n";

    readonly TempFolder _root = new();

    /// `time` is the editor's clock, for autosave and recovery: the real one unless a test moves it itself.
    public TestEnvironment(TimeProvider? time = null)
    {
        Directory.CreateDirectory(UserThemes);
        Directory.CreateDirectory(InstalledThemes);
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigFile)!);
        Environment = new EditorEnvironment
        {
            UserThemes = UserThemes,
            InstalledThemes = InstalledThemes,
            ConfigFile = ConfigFile,
            Folders = new EditorFolders(_root.Combine("editor")),
            Locator = new ThemeLocator([UserThemes, InstalledThemes], _root.Combine("cwd")),
            ValueSources = () => new ValueSources(new NoTray(), new NoReadings()),
            Dialogs = Dialogs,
            Clipboard = Clipboard,
            IsElevated = () => false,
            Time = time ?? TimeProvider.System,
        };
    }

    public EditorEnvironment Environment { get; }

    public FakeDialogs Dialogs { get; } = new();

    public FakeClipboard Clipboard { get; } = new();

    public string Root => _root.Path;

    public string UserThemes => _root.Combine("themes");

    public string InstalledThemes => _root.Combine("installed");

    public string ConfigFile => _root.Combine("config", "config.yaml");

    /// Writes a theme named `name` into the user's themes, or the installed examples, and returns its folder.
    public string AddTheme(string name, string layout = Layout, bool installed = false)
    {
        var folder = Path.Combine(installed ? InstalledThemes : UserThemes, name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, ThemeLoader.LayoutFileName), layout);
        return folder;
    }

    /// Copies the repository's example theme `name`, every file, into the user's themes, or the installed examples, and
    /// returns its folder.
    public string AddExample(string name, bool installed = false)
    {
        var source = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFile())!, "..", "..", "..", "themes", name));
        var folder = Path.Combine(installed ? InstalledThemes : UserThemes, name);
        foreach (var path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(folder, Path.GetRelativePath(source, path));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(path, target);
        }

        return folder;
    }

    /// A small PNG at `path`, which the theme loader decodes.
    public static void WritePng(string path, int size = 4)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new SkiaSharp.SKBitmap(size, size);
        bitmap.Erase(SkiaSharp.SKColors.Orange);
        using var data = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    static string ThisFile([CallerFilePath] string file = "") => file;

    public void Dispose() => _root.Dispose();
}
