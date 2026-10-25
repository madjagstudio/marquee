using System.Runtime.InteropServices;
using System.Text;
using Marquee.Core.Tests.TestSupport;
using Marquee.Core.Video;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// MP4 clips decoded with Windows Media Foundation. The clips are H.264, written at test time by ClipWriter.
///
/// Where Media Foundation isn't installed (Windows N without the Media Feature Pack, Windows Server without its Media
/// Foundation feature) there is nothing to decode or write clips with. Each decoding test then checks the one thing that
/// can be checked there, that opening a clip says Media Foundation is missing, rather than passing having tested nothing.
public sealed class MediaFoundationDecoderTests : IDisposable
{
    static readonly SKColor Red = new(200, 40, 40);
    static readonly SKColor Green = new(40, 160, 60);
    static readonly SKColor Blue = new(50, 70, 200);

    /// H.264 is lossy and its color conversions round, so decoded colors are only close to the written ones.
    const int Tolerance = 24;

    readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    string Clip(params SKColor[] colors)
    {
        var path = _folder.File("clip.mp4");
        ClipWriter.Write(path, 128, 72, colors);
        return path;
    }

    /// True where Media Foundation can decode. Where it can't, asserts that opening a clip says so and why.
    bool CanDecode()
    {
        if (MediaFoundation.IsAvailable) return true;
        var path = _folder.File("unavailable.mp4");
        File.WriteAllBytes(path, [0]);
        var error = Assert.Throws<VideoDecodeException>(() => new MediaFoundationDecoder(path));
        Assert.Contains("Media Feature Pack", error.Message);
        return false;
    }

    static List<DecodedFrame> ReadAll(IVideoDecoder decoder)
    {
        var frames = new List<DecodedFrame>();
        while (decoder.Next() is { } frame) frames.Add(frame);
        return frames;
    }

    static SKColor Center(DecodedFrame frame) => frame.Pixels.GetPixelColor(frame.Pixels.Width / 2, frame.Pixels.Height / 2);

    /// 72 rows aren't whole 16-row blocks, so the H.264 decoder fills 80 and the picture is the top 72 of them.
    [Fact]
    public void The_size_is_the_clips_frame_size()
    {
        if (!CanDecode()) return;
        using var decoder = new MediaFoundationDecoder(Clip(Red));

        Assert.Equal(new SKSizeI(128, 72), decoder.Size);
        var frame = decoder.Next()!.Value;
        Assert.Equal(128, frame.Pixels.Width);
        Assert.Equal(72, frame.Pixels.Height);
    }

    [Fact]
    public void Every_written_frame_is_decoded_in_order_with_its_color()
    {
        if (!CanDecode()) return;
        using var decoder = new MediaFoundationDecoder(Clip(Red, Green, Blue, Red, Blue));

        var colors = new List<SKColor>();
        while (decoder.Next() is { } frame) colors.Add(Center(frame));

        Assert.Equal(5, colors.Count);
        Pixels.AssertNear(Red, colors[0], Tolerance);
        Pixels.AssertNear(Green, colors[1], Tolerance);
        Pixels.AssertNear(Blue, colors[2], Tolerance);
        Pixels.AssertNear(Red, colors[3], Tolerance);
        Pixels.AssertNear(Blue, colors[4], Tolerance);
    }

    [Fact]
    public void Frames_are_opaque_32_bit_pixels()
    {
        if (!CanDecode()) return;
        using var decoder = new MediaFoundationDecoder(Clip(Green));

        var pixels = decoder.Next()!.Value.Pixels;
        Assert.Equal(SKColorType.Bgra8888, pixels.ColorType);
        Assert.Equal(SKAlphaType.Opaque, pixels.AlphaType);
        Assert.Equal(255, pixels.GetPixelColor(0, 0).Alpha);
        Assert.Equal(255, pixels.GetPixelColor(127, 71).Alpha);
    }

    [Fact]
    public void Rows_come_out_top_down()
    {
        if (!CanDecode()) return;
        var path = _folder.File("halves.mp4");
        ClipWriter.Write(path, 128, 72, 1, (canvas, _) =>
        {
            canvas.Clear(Blue);
            using var paint = new SKPaint { Color = Red };
            canvas.DrawRect(0, 0, 128, 36, paint);
        });
        using var decoder = new MediaFoundationDecoder(path);

        var pixels = decoder.Next()!.Value.Pixels;
        Pixels.AssertNear(Red, pixels.GetPixelColor(64, 10), Tolerance);
        Pixels.AssertNear(Blue, pixels.GetPixelColor(64, 62), Tolerance);
    }

    [Fact]
    public void Frames_are_timed_from_the_clip_100_ms_apart()
    {
        if (!CanDecode()) return;
        using var decoder = new MediaFoundationDecoder(Clip(Red, Green, Blue, Red, Green));

        var frames = ReadAll(decoder);

        Assert.Equal(5, frames.Count);
        for (var i = 0; i < frames.Count; i++)
        {
            Assert.InRange(frames[i].Start.TotalMilliseconds, i * 100 - 2, i * 100 + 2);
            Assert.InRange(frames[i].Duration.TotalMilliseconds, 98, 102);
        }
    }

    [Fact]
    public void Next_returns_null_at_the_end_and_keeps_doing_so()
    {
        if (!CanDecode()) return;
        using var decoder = new MediaFoundationDecoder(Clip(Red, Green));

        Assert.NotNull(decoder.Next());
        Assert.NotNull(decoder.Next());
        Assert.Null(decoder.Next());
        Assert.Null(decoder.Next());
    }

    /// ReadSample can succeed and still say in its flags that the reader has failed, after which Media Foundation says
    /// to make no more calls to it. No real clip makes it do that on demand, so the flags are checked on their own.
    [Fact]
    public void A_read_whose_flags_report_an_error_stops_decoding()
    {
        var error = Assert.Throws<VideoDecodeException>(
            () => MediaFoundationDecoder.CheckReadFlags(MediaFoundation.MF_SOURCE_READERF_ERROR));

        Assert.Equal("Windows stopped decoding the clip partway: the file may be damaged, or may no longer be readable.",
            error.Message);
        MediaFoundationDecoder.CheckReadFlags(0);
        MediaFoundationDecoder.CheckReadFlags(MediaFoundation.MF_SOURCE_READERF_ENDOFSTREAM);
        MediaFoundationDecoder.CheckReadFlags(MediaFoundation.MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED);
    }

    [Fact]
    public void Rewind_goes_back_to_the_first_frame_from_the_middle_or_the_end()
    {
        if (!CanDecode()) return;
        using var decoder = new MediaFoundationDecoder(Clip(Red, Green, Blue));

        decoder.Next();
        decoder.Next();
        decoder.Rewind();
        var first = decoder.Next()!.Value;
        Pixels.AssertNear(Red, Center(first), Tolerance);
        Assert.Equal(TimeSpan.Zero, first.Start);

        ReadAll(decoder);
        decoder.Rewind();
        var frames = ReadAll(decoder);
        Assert.Equal(3, frames.Count);
        Assert.Equal(TimeSpan.Zero, frames[0].Start);
    }

    [Fact]
    public void A_clip_with_frames_over_3840_by_2160_is_refused()
    {
        if (!CanDecode()) return;
        var path = _folder.File("dci4k.mp4");
        ClipWriter.Write(path, 4096, 2160, Red);

        var error = Assert.Throws<VideoDecodeException>(() => new MediaFoundationDecoder(path));
        Assert.Equal("The clip's frames are 4096×2160, over the 3840×2160 limit.", error.Message);
    }

    [Fact]
    public void A_portrait_4k_clip_is_within_the_limit()
    {
        const int MF_E_SINK_NO_SAMPLES_PROCESSED = unchecked((int)0xC00D4A44);
        if (!CanDecode()) return;
        var path = _folder.File("portrait4k.mp4");
        try
        {
            ClipWriter.Write(path, 2160, 3840, Red);
        }
        catch (COMException ex) when (ex.HResult == MF_E_SINK_NO_SAMPLES_PROCESSED)
        {
            // The encoder sometimes finishes without a sample. Its writer is released, so delete the fixture and
            // write it once more.
            File.Delete(path);
            ClipWriter.Write(path, 2160, 3840, Red);
        }

        using var decoder = new MediaFoundationDecoder(path);
        Assert.Equal(new SKSizeI(2160, 3840), decoder.Size);
    }

    [Fact]
    public void A_missing_file_says_it_doesnt_exist()
    {
        var error = Assert.Throws<VideoDecodeException>(() => new MediaFoundationDecoder(_folder.File("missing.mp4")));
        Assert.Equal("The file doesn't exist.", error.Message);
    }

    const string NotVideo = "Windows can't read the file as video: it's damaged, it isn't a video, or its container format " +
        "isn't one Windows supports (MP4, MOV and WMV are).";

    /// Media Foundation fails on these differently: the longer text is no known container
    /// (MF_E_UNSUPPORTED_BYTESTREAM_TYPE), the shorter one runs out while a parser is still reading it (MF_E_END_OF_STREAM).
    [Theory]
    [InlineData("These are not the frames you're looking for.\n")]
    [InlineData("hello there\n")]
    public void A_text_file_named_mp4_says_its_not_readable_as_video(string text)
    {
        if (!CanDecode()) return;
        var path = _folder.File("notes.mp4");
        File.WriteAllText(path, text);

        var error = Assert.Throws<VideoDecodeException>(() => new MediaFoundationDecoder(path));
        Assert.Equal(NotVideo, error.Message);
    }

    [Fact]
    public void A_clip_cut_off_halfway_says_its_not_readable_as_video()
    {
        if (!CanDecode()) return;
        var bytes = File.ReadAllBytes(Clip(Red, Green, Blue));
        var path = _folder.File("cut.mp4");
        File.WriteAllBytes(path, bytes[..(bytes.Length / 2)]);

        var error = Assert.Throws<VideoDecodeException>(() => new MediaFoundationDecoder(path));
        Assert.Equal(NotVideo, error.Message);
    }

    [Fact]
    public void An_empty_file_says_its_not_readable_as_video()
    {
        if (!CanDecode()) return;
        var path = _folder.File("empty.mp4");
        File.WriteAllBytes(path, []);

        var error = Assert.Throws<VideoDecodeException>(() => new MediaFoundationDecoder(path));
        Assert.Equal(NotVideo, error.Message);
    }

    [Fact]
    public void A_clip_in_a_format_with_no_decoder_says_to_install_the_extension()
    {
        if (!CanDecode()) return;
        // An H.264 clip whose sample entry claims an unknown codec, as an HEVC clip looks where its extension isn't installed.
        var bytes = File.ReadAllBytes(Clip(Red));
        var entry = bytes.AsSpan().IndexOf("avc1"u8);
        Assert.True(entry > 0);
        "zzzz"u8.CopyTo(bytes.AsSpan(entry));
        var path = _folder.File("unknown-codec.mp4");
        File.WriteAllBytes(path, bytes);

        var error = Assert.Throws<VideoDecodeException>(() => new MediaFoundationDecoder(path));
        Assert.Equal("Windows has no decoder for this clip's video format. HEVC, VP9 and AV1 clips need Microsoft's " +
            "extension for that format from the Microsoft Store; H.264 needs nothing extra.", error.Message);
    }

    [Fact]
    public void An_audio_file_says_it_has_no_video()
    {
        if (!CanDecode()) return;
        var path = _folder.File("silence.wav");
        File.WriteAllBytes(path, Silence());

        var error = Assert.Throws<VideoDecodeException>(() => new MediaFoundationDecoder(path));
        Assert.Equal("The file has no video stream.", error.Message);
    }

    /// Half a second of 16-bit mono silence at 8 kHz, as a WAV file.
    static byte[] Silence()
    {
        var samples = new byte[8000];
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII);
        writer.Write("RIFF"u8);
        writer.Write(36 + samples.Length);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(8000);
        writer.Write(16000);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples.Length);
        writer.Write(samples);
        writer.Flush();
        return stream.ToArray();
    }
}
