using System.Text;
using Marquee.Core.Themes;

namespace Marquee.Cli.Tests.TestSupport;

/// A theme folder under %TEMP%, deleted on dispose.
sealed class TempTheme : IDisposable
{
    public TempTheme(string layout)
    {
        Folder = Path.Combine(Path.GetTempPath(), "marquee-theme-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Folder);
        AddFile(ThemeLoader.LayoutFileName, layout);
    }

    public string Folder { get; }

    public string Name => Path.GetFileName(Folder);

    public void AddFile(string relativePath, string contents)
    {
        var path = Path.Combine(Folder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents, new UTF8Encoding(false));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // %TEMP% is cleaned eventually.
        }
    }
}
