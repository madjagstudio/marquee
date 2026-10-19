using Marquee.Core.Tests.TestSupport;
using Marquee.Core.Video;
using SkiaSharp;

namespace Marquee.Core.Tests;

public sealed class VideoDecodersTests : IDisposable
{
    readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    string Gif(string name)
    {
        var path = _folder.File(name);
        File.WriteAllBytes(path, GifWriter.Write(4, 4, [SKColors.Red], GifFrame.Solid(SKRectI.Create(4, 4), 0, 10)));
        return path;
    }

    [Theory]
    [InlineData("clip.gif")]
    [InlineData("clip.webp")]
    [InlineData("CLIP.GIF")]
    public void GIF_and_WebP_clips_are_played_by_Skia(string name)
    {
        // A GIF named .webp still opens: Skia goes by the file's contents, and the extension only picks the decoder.
        using var decoder = VideoDecoders.Open(Gif(name));

        Assert.IsType<SkiaAnimatedDecoder>(decoder);
    }

    [Theory]
    [InlineData("clip.mp4")]
    [InlineData("clip.m4v")]
    [InlineData("clip.mov")]
    [InlineData("clip.wmv")]
    public void Other_clips_are_played_by_Media_Foundation(string name)
    {
        // A GIF is no video to Media Foundation, so its error (or, without Media Foundation, the one saying it's
        // missing) shows which decoder had the file.
        var error = Assert.Throws<VideoDecodeException>(() => VideoDecoders.Open(Gif(name)));

        Assert.Contains("Windows", error.Message);
    }

    [Theory]
    [InlineData("clip.gif")]
    [InlineData("clip.mp4")]
    public void A_playing_clip_can_be_saved_over_and_a_closed_one_is_let_go(string name)
    {
        var path = _folder.File(name);
        if (name.EndsWith(".gif", StringComparison.Ordinal))
            Gif(name);
        else if (MediaFoundation.IsAvailable)
            ClipWriter.Write(path, 128, 72, SKColors.Red, SKColors.Blue);
        else
        {
            // Nothing can write or play an MP4 here; check the one thing that can be: opening one says why not.
            File.WriteAllBytes(path, [0]);
            Assert.Contains("Media Feature Pack", Assert.Throws<VideoDecodeException>(() => VideoDecoders.Open(path)).Message);
            return;
        }

        var decoder = VideoDecoders.Open(path);
        Assert.NotNull(decoder.Next());

        // An editor can write over the clip, or delete it, while it plays (see ClipFile)...
        using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { }

        // ...and once the decoder is closed, nothing holds the file at all.
        decoder.Dispose();
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    }
}
