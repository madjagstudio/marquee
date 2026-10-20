using System.Runtime.CompilerServices;
using Marquee.Core.Themes;

namespace Marquee.Editor.Model.Tests.TestSupport;

/// A theme folder under %TEMP%, deleted on dispose: a copy of one of the example themes, or a layout written by the
/// test. The editor's tests never open the examples in place, nor anything under %LOCALAPPDATA%.
sealed class TempTheme : IDisposable
{
    public static readonly string[] Examples = ["example-landscape", "example-portrait", "example-agents", "example-showcase"];

    TempTheme()
    {
        Folder = Path.Combine(Path.GetTempPath(), "marquee-editor-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Folder);
    }

    public string Folder { get; }

    public string LayoutPath => Path.Combine(Folder, ThemeLoader.LayoutFileName);

    /// A theme whose layout.yaml is `layout`, written as UTF-8 without a byte order mark unless it starts with one.
    public static TempTheme With(string layout)
    {
        var theme = new TempTheme();
        File.WriteAllText(theme.LayoutPath, layout);
        return theme;
    }

    /// A copy of the example theme `name` from the repository's themes folder, with its files.
    public static TempTheme Example(string name)
    {
        var theme = new TempTheme();
        var source = ExampleFolder(name);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(theme.Folder, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        return theme;
    }

    public static string ExampleFolder(string name, [CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", "..", "..", "themes", name));

    public string Text => File.ReadAllText(LayoutPath);

    public EditorSession Open(EditorSessionOptions? options = null) => EditorSession.Open(Folder, options);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
            // A clip a failing test didn't dispose may still be open; %TEMP% is cleaned eventually.
        }
    }
}
