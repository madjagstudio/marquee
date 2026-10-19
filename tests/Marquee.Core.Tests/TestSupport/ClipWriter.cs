using System.Runtime.InteropServices;
using Marquee.Core.Video;
using SkiaSharp;
using static Marquee.Core.Video.MediaFoundation;

namespace Marquee.Core.Tests.TestSupport;

/// Writes small H.264 MP4 clips with Media Foundation's sink writer, so video tests decode real clips without a binary
/// committed to the repo. Encoding lives here, not in Core, because Marquee itself only decodes.
static class ClipWriter
{
    public const int Fps = 10;

    /// 100 ns units, Media Foundation's time unit.
    public const long FrameTicks = TimeSpan.TicksPerSecond / Fps;

    /// One solid-color frame per color, at 10 fps.
    public static void Write(string path, int width, int height, params SKColor[] colors) =>
        Write(path, width, height, colors.Length, (canvas, i) => canvas.Clear(colors[i]));

    /// frameCount frames at 10 fps, each painted by paint(canvas, index). Width and height must be even (H.264 4:2:0).
    public static void Write(string path, int width, int height, int frameCount, Action<SKCanvas, int> paint)
    {
        if (!IsAvailable) throw new InvalidOperationException("Media Foundation isn't available to write a test clip.");
        IMFSinkWriter? writer = null;
        try
        {
            Check(MFCreateSinkWriterFromURL(path, IntPtr.Zero, IntPtr.Zero, out writer));
            var stream = AddStream(writer!, width, height);
            Check(writer!.BeginWriting());
            using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
            using var canvas = new SKCanvas(bitmap);
            for (var i = 0; i < frameCount; i++)
            {
                paint(canvas, i);
                canvas.Flush();
                WriteFrame(writer, stream, bitmap, i);
            }

            // Finalize drains the encoder, which holds back a few frames, and writes the MP4's index.
            Check(writer.FinalizeWriting());
        }
        finally
        {
            Release(writer);
        }
    }

    static uint AddStream(IMFSinkWriter writer, int width, int height)
    {
        IMFMediaType? output = null;
        IMFMediaType? input = null;
        try
        {
            Check(MFCreateMediaType(out output));
            SetVideoFormat(output!, MFVideoFormat_H264, width, height);
            // Generous for 128×72, so the lossy codec stays close to the written colors.
            Check(output!.SetUINT32(MF_MT_AVG_BITRATE, 2_000_000));
            Check(writer.AddStream(output, out var stream));

            Check(MFCreateMediaType(out input));
            SetVideoFormat(input!, MFVideoFormat_RGB32, width, height);
            // A positive stride says the rows are top-down, as Skia lays them out.
            Check(input!.SetUINT32(MF_MT_DEFAULT_STRIDE, (uint)(width * 4)));
            Check(writer.SetInputMediaType(stream, input, IntPtr.Zero));
            return stream;
        }
        finally
        {
            Release(output);
            Release(input);
        }
    }

    static void SetVideoFormat(IMFMediaType type, Guid subtype, int width, int height)
    {
        Check(type.SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video));
        Check(type.SetGUID(MF_MT_SUBTYPE, subtype));
        Check(type.SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive));
        Check(type.SetUINT64(MF_MT_FRAME_SIZE, Pack((uint)width, (uint)height)));
        Check(type.SetUINT64(MF_MT_FRAME_RATE, Pack(Fps, 1)));
        Check(type.SetUINT64(MF_MT_PIXEL_ASPECT_RATIO, Pack(1, 1)));
    }

    static void WriteFrame(IMFSinkWriter writer, uint stream, SKBitmap bitmap, int index)
    {
        var bytes = bitmap.Bytes;
        IMFMediaBuffer? buffer = null;
        IMFSample? sample = null;
        try
        {
            Check(MFCreateMemoryBuffer((uint)bytes.Length, out buffer));
            Check(buffer!.Lock(out var data, out _, out _));
            Marshal.Copy(bytes, 0, data, bytes.Length);
            Check(buffer.Unlock());
            Check(buffer.SetCurrentLength((uint)bytes.Length));

            Check(MFCreateSample(out sample));
            Check(sample!.AddBuffer(buffer));
            Check(sample.SetSampleTime(index * FrameTicks));
            Check(sample.SetSampleDuration(FrameTicks));
            Check(writer.WriteSample(stream, sample));
        }
        finally
        {
            Release(sample);
            Release(buffer);
        }
    }

    static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);

    [DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int MFCreateSinkWriterFromURL(string url, IntPtr byteStream, IntPtr attributes, out IMFSinkWriter? writer);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int MFCreateSample(out IMFSample? sample);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int MFCreateMemoryBuffer(uint maxLength, out IMFMediaBuffer? buffer);

    /// mfreadwrite.h, up to Finalize (renamed here: C# reserves Finalize for destructors). Vtable order matters, not names.
    [ComImport, Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSinkWriter
    {
        [PreserveSig] int AddStream(IMFMediaType targetMediaType, out uint streamIndex);
        [PreserveSig] int SetInputMediaType(uint streamIndex, IMFMediaType inputMediaType, IntPtr encodingParameters);
        [PreserveSig] int BeginWriting();
        [PreserveSig] int WriteSample(uint streamIndex, IMFSample sample);
        [PreserveSig] int SendStreamTick(uint streamIndex, long timestamp);
        [PreserveSig] int PlaceMarker(uint streamIndex, IntPtr context);
        [PreserveSig] int NotifyEndOfSegment(uint streamIndex);
        [PreserveSig] int Flush(uint streamIndex);
        [PreserveSig] int FinalizeWriting();
    }
}
