using System.Diagnostics;
using Marquee.Core.Tests.TestSupport;
using Marquee.Core.Video;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// These tests measure the whole process, so nothing else may run beside them.
[CollectionDefinition("Measures process memory", DisableParallelization = true)]
public class MeasuresProcessMemoryCollection;

/// A decoder releases each frame's Media Foundation objects when it moves on, not when the garbage collector next runs.
/// The clip is 640×360, so each decoded frame holds about 1 MB of native memory behind a small managed wrapper: frames
/// left for the collector would pile up by about 1.4 GB over 1,500 frames, while no managed allocation asks it to run.
/// The limits are far below that and far above the noise.
[Collection("Measures process memory")]
public sealed class MediaFoundationDecoderLeakTests : IDisposable
{
    const int Reads = 50;
    const long MemoryLimit = 300L * 1024 * 1024;
    const int HandleLimit = 200;

    readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    string Clip()
    {
        var path = _folder.File("leak.mp4");
        ClipWriter.Write(path, 640, 360, 30, (canvas, i) => canvas.Clear(new SKColor((byte)(i * 8), 80, 160)));
        return path;
    }

    static int ReadAll(IVideoDecoder decoder)
    {
        var count = 0;
        while (decoder.Next() is not null) count++;
        return count;
    }

    static (long Memory, int Handles) Measure()
    {
        using var process = Process.GetCurrentProcess();
        return (process.PrivateMemorySize64, process.HandleCount);
    }

    static void AssertBounded((long Memory, int Handles) before, (long Memory, int Handles) after)
    {
        Assert.True(after.Memory - before.Memory < MemoryLimit,
            $"Private memory grew by {(after.Memory - before.Memory) / (1024 * 1024)} MB");
        Assert.True(after.Handles - before.Handles < HandleLimit, $"Handle count grew by {after.Handles - before.Handles}");
    }

    [Fact]
    public void Rewinding_and_reading_a_clip_many_times_doesnt_leak()
    {
        if (!MediaFoundation.IsAvailable) return; // MediaFoundationDecoderTests checks the error there.
        using var decoder = new MediaFoundationDecoder(Clip());
        for (var i = 0; i < 3; i++)
        {
            decoder.Rewind();
            ReadAll(decoder);
        }

        var before = Measure();
        for (var i = 0; i < Reads; i++)
        {
            decoder.Rewind();
            Assert.Equal(30, ReadAll(decoder));
        }

        AssertBounded(before, Measure());
    }

    [Fact]
    public void Opening_reading_and_disposing_a_clip_many_times_doesnt_leak()
    {
        if (!MediaFoundation.IsAvailable) return;
        var path = Clip();
        for (var i = 0; i < 3; i++)
        {
            using var decoder = new MediaFoundationDecoder(path);
            ReadAll(decoder);
        }

        var before = Measure();
        for (var i = 0; i < Reads; i++)
        {
            using var decoder = new MediaFoundationDecoder(path);
            Assert.Equal(30, ReadAll(decoder));
        }

        AssertBounded(before, Measure());
    }
}
