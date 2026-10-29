using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using SkiaSharp;
using static Marquee.Core.Video.MediaFoundation;

namespace Marquee.Core.Video;

/// Decodes a video file with Windows Media Foundation's source reader: MP4 and MOV with H.264, which every Windows
/// with Media Foundation decodes, plus HEVC, VP9 and AV1 where the user installed Microsoft's extension for them.
///
/// It decodes on the graphics card where it can (see HardwareVideo), which costs a fraction of the CPU, and otherwise
/// in software. A clip the graphics card's decoder refuses when it is opened is opened again in software, and so is one
/// it stops decoding partway, as it does when the graphics driver resets (a game crashing it, say): that clip carries
/// on in software from the frame after the last one it showed.
///
/// Every Media Foundation object is released as soon as the decoder is done with it (Release, a final release of its
/// wrapper). Frames arrive 30 times a second, each holding a decoded picture of up to 33 MB in native memory behind a
/// wrapper of a few bytes; the wrappers alone would never make the garbage collector run, so leaving them to it would
/// pile up native memory, and could stall the decoder while its output samples are all still held.
public sealed class MediaFoundationDecoder : IVideoDecoder
{
    readonly string _path;
    readonly FileStream _file;
    HardwareVideo? _hardware;
    IMFSourceReader? _reader;
    bool _disposed;
    long? _lastTime; // the time stamp of the frame Next last returned, since the last rewind
    long? _resumeAfter; // after switching to software: frames up to this time stamp were shown already
    readonly SKBitmap _bitmap;
    readonly SKPixmap _pixels;
    readonly TimeSpan _frameDuration;
    Layout _layout;

    /// Opens the file and reads its first video stream's format, decoding on the graphics card if `hardware` allows and
    /// the PC has one that can. Errors are VideoDecodeException with a message that gives the cause for the user; the
    /// caller names the file.
    public MediaFoundationDecoder(string path, bool hardware = true)
    {
        if (!File.Exists(path)) throw new VideoDecodeException("The file doesn't exist.");
        if (!IsAvailable)
        {
            throw new VideoDecodeException("Windows Media Foundation isn't available on this PC. On Windows N editions, " +
                "install the Media Feature Pack (Settings > System > Optional features); on Windows Server, add the " +
                "Media Foundation feature.");
        }

        try
        {
            path = Path.GetFullPath(path);
            _path = path;
            _file = ClipFile.Open(path);
            if (hardware) _hardware = HardwareVideo.Create();
            try
            {
                _reader = Open(_file, path, _hardware);
            }
            catch (VideoDecodeException) when (_hardware is not null)
            {
                _hardware.Dispose();
                _hardware = null;
                _file.Position = 0;
                _reader = Open(_file, path, null);
            }

            (_layout, _frameDuration) = ReadFormat(_reader);
            Size = _layout.Visible.Size;
            // RGB32's fourth byte is padding, not alpha; Copy sets it to 255 so the pixels really are opaque.
            _bitmap = new SKBitmap(new SKImageInfo(Size.Width, Size.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
            _pixels = _bitmap.PeekPixels();
        }
        catch
        {
            Release(_reader);
            _reader = null;
            _file?.Dispose();
            _hardware?.Dispose();
            throw;
        }
    }

    public SKSizeI Size { get; }

    /// Whether the clip is decoded on the graphics card.
    public bool UsesHardware => _hardware is not null;

    /// Tests only: makes the next read on the graphics card fail, as one does when the graphics driver resets.
    internal bool FailNextHardwareRead { get; set; }

    public DecodedFrame? Next()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            return Read();
        }
        catch (VideoDecodeException) when (_hardware is not null)
        {
            SwitchToSoftware();
            return Read();
        }
    }

    DecodedFrame? Read()
    {
        // Null only when switching to software failed, which that failure already reported.
        var reader = _reader ?? throw new VideoDecodeException("Windows stopped decoding the clip partway.");
        while (true)
        {
            IMFSample? sample = null;
            try
            {
                if (FailNextHardwareRead && _hardware is not null)
                {
                    FailNextHardwareRead = false;
                    throw new VideoDecodeException("The graphics card stopped decoding (test).");
                }

                Check(reader.ReadSample(MF_SOURCE_READER_FIRST_VIDEO_STREAM, 0, out _, out var flags, out var time, out sample));
                CheckReadFlags(flags);
                if ((flags & MF_SOURCE_READERF_ENDOFSTREAM) != 0) return null;
                if ((flags & MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED) != 0) FormatChanged(reader);
                // A stream tick (a gap in the stream) comes without a sample.
                if (sample is null) continue;
                // After switching to software, the seek lands on the key frame at or before where the clip had got to.
                if (_resumeAfter is { } shown)
                {
                    if (time <= shown) continue;
                    _resumeAfter = null;
                }

                Copy(sample);
                _lastTime = time;
                var duration = sample.GetSampleDuration(out var ticks) >= 0 ? TimeSpan.FromTicks(ticks) : _frameDuration;
                return new DecodedFrame(_pixels, TimeSpan.FromTicks(time), duration);
            }
            finally
            {
                Release(sample);
            }
        }
    }

    /// Opens the clip again to decode in software, from where it had got to on the graphics card.
    void SwitchToSoftware()
    {
        Release(_reader); // the reader before its graphics card, as Dispose does
        _reader = null;
        _hardware!.Dispose();
        _hardware = null;

        _file.Position = 0;
        var reader = Open(_file, _path, null);
        _reader = reader;
        FormatChanged(reader);
        if (_lastTime is { } shown)
        {
            Check(reader.SetCurrentPosition(Guid.Empty, new PropVariant { Type = VT_I8, Value = shown }));
            _resumeAfter = shown;
        }
    }

    /// ReadSample can succeed and still report in its flags that the reader has failed, with no sample. Media Foundation
    /// says to make no more calls to the reader after that, so reading on (the stream-tick case) would be wrong; this
    /// ends decoding instead, and every caller stops at a VideoDecodeException (the player keeps its last good frame).
    internal static void CheckReadFlags(uint flags)
    {
        if ((flags & MF_SOURCE_READERF_ERROR) != 0)
        {
            throw new VideoDecodeException(
                "Windows stopped decoding the clip partway: the file may be damaged, or may no longer be readable.");
        }
    }

    public void Rewind()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var reader = _reader ?? throw new VideoDecodeException("Windows stopped decoding the clip partway.");
        // GUID_NULL: the position is in 100 ns units.
        Check(reader.SetCurrentPosition(Guid.Empty, new PropVariant { Type = VT_I8, Value = 0 }));
        (_lastTime, _resumeAfter) = (null, null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Release(_reader);
        _reader = null;
        _file.Dispose(); // after the reader, which reads it until released
        _hardware?.Dispose(); // after the reader too, which decodes on it
        _pixels.Dispose();
        _bitmap.Dispose();
    }

    /// A source reader for the clip in `file`, whose name `path` picks the container parser, decoding on `hardware`'s
    /// graphics card if given.
    static IMFSourceReader Open(FileStream file, string path, HardwareVideo? hardware)
    {
        IMFByteStream? stream = null;
        IMFAttributes? attributes = null;
        IMFMediaType? rgb = null;
        IMFSourceReader? reader = null;
        try
        {
            // Media Foundation reads the file Marquee opened, rather than opening it by name, so it stays editable
            // while it plays (see ClipFile).
            Check(MFCreateMFByteStreamOnStream(new ReadOnlyComStream(file), out stream));
            if (stream is IMFAttributes origin) Check(origin.SetString(MF_BYTESTREAM_ORIGIN_NAME, path));
            Check(MFCreateAttributes(out attributes, 3));
            if (hardware is null)
            {
                // Lets the source reader convert the decoder's YUV output to RGB32.
                Check(attributes!.SetUINT32(MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, 1));
            }
            else
            {
                // Decodes on the graphics card and converts to RGB32 there too: the advanced video processing that
                // works with a device manager, in place of the software conversion above.
                var manager = hardware.ManagerPointer();
                try
                {
                    Check(attributes!.SetUnknown(MF_SOURCE_READER_D3D_MANAGER, manager));
                }
                finally
                {
                    Marshal.Release(manager);
                }

                Check(attributes.SetUINT32(MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1));
                Check(attributes.SetUINT32(MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING, 1));
            }

            Check(MFCreateSourceReaderFromByteStream(stream!, attributes, out reader));
            Check(reader!.SetStreamSelection(MF_SOURCE_READER_ALL_STREAMS, 0));
            Check(reader.SetStreamSelection(MF_SOURCE_READER_FIRST_VIDEO_STREAM, 1));

            Check(MFCreateMediaType(out rgb));
            Check(rgb!.SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video));
            Check(rgb.SetGUID(MF_MT_SUBTYPE, MFVideoFormat_RGB32));
            Check(reader.SetCurrentMediaType(MF_SOURCE_READER_FIRST_VIDEO_STREAM, IntPtr.Zero, rgb));
            return reader;
        }
        catch
        {
            Release(reader);
            throw;
        }
        finally
        {
            Release(rgb);
            Release(attributes);
            Release(stream); // the reader holds its own reference
        }
    }

    /// Where the picture is in the decoder's output buffer. Frame is the size the decoder fills, which can be larger
    /// than the picture: H.264 decoders round it up to whole 16-pixel blocks, so a 1920×1080 clip comes out 1920×1088
    /// with the picture in its top 1080 rows. Stride is the bytes from one row to the next, negative when the rows are
    /// stored bottom-up.
    readonly record struct Layout(SKSizeI Frame, SKRectI Visible, int Stride);

    /// The output's layout, with its picture and its frames refused over VideoLimits, and the length of one frame.
    static (Layout Layout, TimeSpan FrameDuration) ReadFormat(IMFSourceReader reader)
    {
        IMFMediaType? type = null;
        try
        {
            Check(reader.GetCurrentMediaType(MF_SOURCE_READER_FIRST_VIDEO_STREAM, out type));
            Check(type!.GetUINT64(MF_MT_FRAME_SIZE, out var packedSize));
            var (width, height) = Unpack(packedSize);
            // The frame is what gets allocated and copied, so it is held to the limit, not only the picture in it.
            VideoLimits.CheckDecoded(width, height);
            var frame = new SKSizeI((int)width, (int)height);

            var visible = Aperture(type, MF_MT_MINIMUM_DISPLAY_APERTURE) ?? Aperture(type, MF_MT_GEOMETRIC_APERTURE)
                ?? SKRectI.Create(frame);
            if (visible.IsEmpty || !SKRectI.Create(frame).Contains(visible))
                throw new VideoDecodeException("The clip's video stream describes a picture outside its frames.");
            VideoLimits.Check(visible.Width, visible.Height);

            // The attribute is a UINT32 holding a signed stride. Without it, RGB rows are top-down and packed.
            var stride = type.GetUINT32(MF_MT_DEFAULT_STRIDE, out var rawStride) >= 0 ? (int)rawStride : frame.Width * 4;

            var frameDuration = TimeSpan.Zero;
            if (type.GetUINT64(MF_MT_FRAME_RATE, out var packedRate) >= 0)
            {
                var (numerator, denominator) = Unpack(packedRate);
                if (numerator > 0) frameDuration = TimeSpan.FromTicks(TimeSpan.TicksPerSecond * denominator / numerator);
            }

            return (new Layout(frame, visible, stride), frameDuration);
        }
        finally
        {
            Release(type);
        }
    }

    static unsafe SKRectI? Aperture(IMFMediaType type, Guid key)
    {
        MFVideoArea area;
        if (type.GetBlob(key, (IntPtr)(&area), (uint)sizeof(MFVideoArea), IntPtr.Zero) < 0) return null;
        return SKRectI.Create(area.OffsetX, area.OffsetY, area.Width, area.Height);
    }

    /// The decoder announces its real output layout with the first frame, and may change it later. The picture's size
    /// can't change, since the bitmap handed out is that size.
    void FormatChanged(IMFSourceReader reader)
    {
        var (layout, _) = ReadFormat(reader);
        if (layout.Visible.Size != Size)
            throw new VideoDecodeException("The clip changes its frame size partway through, which isn't supported.");
        _layout = layout;
    }

    unsafe void Copy(IMFSample sample)
    {
        IMFMediaBuffer? buffer = null;
        try
        {
            Check(sample.ConvertToContiguousBuffer(out buffer));
            Check(buffer!.Lock(out var data, out _, out var length));
            try
            {
                var (frame, visible, stride) = _layout;
                var pitch = Math.Abs(stride);
                if (pitch < frame.Width * 4 || length < (long)pitch * (frame.Height - 1) + frame.Width * 4)
                    throw new VideoDecodeException("The decoder returned a frame smaller than its format says.");

                var rowBytes = visible.Width * 4;
                var target = (byte*)_bitmap.GetPixels();
                for (var y = 0; y < visible.Height; y++)
                {
                    var frameRow = visible.Top + y;
                    // Bottom-up rows: the frame's top row is the last one in the buffer.
                    var bufferRow = stride < 0 ? frame.Height - 1 - frameRow : frameRow;
                    var source = (byte*)data + (long)pitch * bufferRow + visible.Left * 4;
                    var targetRow = target + (long)_bitmap.RowBytes * y;
                    Buffer.MemoryCopy(source, targetRow, rowBytes, rowBytes);
                    MakeOpaque(new Span<uint>(targetRow, visible.Width));
                }
            }
            finally
            {
                buffer.Unlock();
            }
        }
        finally
        {
            Release(buffer);
        }
    }

    /// Sets each BGRA pixel's alpha byte (the high byte of a little-endian uint) to 255.
    static void MakeOpaque(Span<uint> pixels)
    {
        var alpha = new Vector<uint>(0xFF000000);
        var i = 0;
        for (; i <= pixels.Length - Vector<uint>.Count; i += Vector<uint>.Count)
            (new Vector<uint>(pixels[i..]) | alpha).CopyTo(pixels[i..]);
        for (; i < pixels.Length; i++) pixels[i] |= 0xFF000000;
    }

    static void Check(int hr)
    {
        if (hr >= 0) return;
        throw new VideoDecodeException(hr switch
        {
            MF_E_TOPO_CODEC_NOT_FOUND or MF_E_INVALIDMEDIATYPE =>
                "Windows has no decoder for this clip's video format. HEVC, VP9 and AV1 clips need Microsoft's extension " +
                "for that format from the Microsoft Store; H.264 needs nothing extra.",
            // A parser that runs out of file while still reading its headers fails with MF_E_END_OF_STREAM.
            MF_E_UNSUPPORTED_BYTESTREAM_TYPE or MF_E_INVALID_FILE_FORMAT or MF_E_END_OF_STREAM =>
                "Windows can't read the file as video: it's damaged, it isn't a video, or its container format isn't one " +
                "Windows supports (MP4, MOV and WMV are).",
            MF_E_INVALIDSTREAMNUMBER => "The file has no video stream.",
            _ => string.Create(CultureInfo.InvariantCulture, $"Windows couldn't decode the clip (error 0x{hr:X8})."),
        }, Marshal.GetExceptionForHR(hr));
    }
}
