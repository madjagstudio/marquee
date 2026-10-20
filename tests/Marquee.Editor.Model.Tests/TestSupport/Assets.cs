using System.Runtime.CompilerServices;
using Marquee.Core.Themes;
using Marquee.Core.Video;
using SkiaSharp;

namespace Marquee.Editor.Model.Tests.TestSupport;

/// Files to import, made by the test or copied from the repository, and the import checks with a fixed video probe.
static class Assets
{
    /// A width×height PNG of one colour at `path`.
    public static string Png(string path, int width = 8, int height = 8)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Orange);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    /// A copy of the font Marquee bundles, Inter, at `path`.
    public static string Font(string path) => CopyOf(RepositoryFile("src", "Marquee.Core", "Fonts", "Inter-Regular.ttf"), path);

    /// A copy of example-showcase's clip, a 10 fps H.264 MP4, at `path`.
    public static string Clip(string path) => CopyOf(RepositoryFile("themes", "example-showcase", "video", "radar.mp4"), path);

    /// The import checks with a video probe that finds every clip quick, so a busy machine can't change a result.
    public static AssetChecks Checks { get; } = new(Quick);

    /// The checks with a probe that finds every clip takes a second a frame to decode.
    public static AssetChecks SlowChecks { get; } = new(Slow);

    static DecodeCost Quick(IVideoDecoder decoder, FrameScaler scaler) => new(TimeSpan.Zero, TimeSpan.FromMilliseconds(100));

    static DecodeCost Slow(IVideoDecoder decoder, FrameScaler scaler) => new(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(100));

    static string CopyOf(string source, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(source, path);
        return path;
    }

    static string RepositoryFile(string first, string second, string third, string fourth, [CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", "..", "..", first, second, third, fourth));
}
