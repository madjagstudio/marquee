using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Tests.TestSupport;

/// A theme folder under %TEMP%, deleted on dispose.
sealed class TempTheme : IDisposable
{
    public TempTheme(string layout)
    {
        Folder = Path.Combine(Path.GetTempPath(), "marquee-theme-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Folder);
        Write(layout);
    }

    public string Folder { get; }

    public string LayoutPath => Path.Combine(Folder, ThemeLoader.LayoutFileName);

    public void Write(string layout) => File.WriteAllText(LayoutPath, layout);

    public void AddImage(string relativePath, SKColor color, int width = 64, int height = 64)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(color);
        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        AddFile(relativePath, png.ToArray());
    }

    public void AddFile(string relativePath, byte[] contents)
    {
        var path = Path.Combine(Folder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, contents);
    }

    /// Loads without measuring shader cost (ShaderCostTests covers that), so results don't depend on the machine.
    public LoadedTheme Load() => ThemeLoader.Load(Folder, null, TestProbes.Free);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
            // A watcher test may still hold a handle; %TEMP% is cleaned eventually.
        }
    }
}
