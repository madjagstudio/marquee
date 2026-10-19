using Marquee.Core.Themes;

namespace Marquee.Host.Tests.TestSupport;

/// A theme folder under %TEMP%, deleted on dispose.
sealed class TempTheme : IDisposable
{
    /// A plain landscape theme: one fill, no assets, no shaders, so it loads fast on any machine.
    public static string Fill(string color, int fps = 30) =>
        $"canvas: {{ orientation: landscape, fps: {fps} }}\nbackground:\n  - fill: \"{color}\"\n";

    /// Fails to load at line 4.
    public const string Broken = "canvas: { orientation: landscape }\nbackground:\n  - fill: \"#00FF00\"\n    blend: sideways\n";

    public TempTheme(string layout)
    {
        Folder = Path.Combine(Path.GetTempPath(), "marquee-theme-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Folder);
        Write(layout);
    }

    public string Folder { get; }

    public string Name => Path.GetFileName(Folder);

    public string LayoutPath => Path.Combine(Folder, ThemeLoader.LayoutFileName);

    public void Write(string layout) => File.WriteAllText(LayoutPath, layout);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A watcher may still hold a handle; %TEMP% is cleaned eventually.
        }
    }
}
