using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Marquee.Core.Logging;
using SkiaSharp;

namespace Marquee.Core.Devices.UniversalScreen88;

/// Encodes frames as JPEG with Windows' own encoder (WIC, windowscodecs.dll), which gives the same quality as Skia's in
/// well under half the time: about 2.5 ms against 6.4 for a 480×1920 frame at quality 90, which at 30 fps is 8% of a
/// core against 19%. Alpha is dropped as Skia drops it: premultiplied colours as they are, so a see-through pixel comes
/// out as if over black.
///
/// Not thread-safe: one per sending thread. After a failure it logs once, and FrameEncoder uses Skia from then on.
sealed class WicJpeg(ILog log) : IDisposable
{
    static readonly Guid ContainerJpeg = new("19e4a5aa-5662-4fc5-a0c0-1758028e1057");
    static readonly Guid Format24bppBgr = new("6fddc324-4e03-4bfe-b185-3d77768dc90c");
    static readonly Guid Format32bppBgr = new("6fddc324-4e03-4bfe-b185-3d77768dc90e");
    static readonly Guid Format32bppRgb = new("d98c6b95-3efe-47d6-bb25-eb1748ab0cf1");
    const int NoCache = 2; // WICBitmapEncoderNoCache
    const ushort VT_R4 = 4;
    const uint PROPBAG2_TYPE_DATA = 1;
    const int STREAM_SEEK_SET = 0, STREAM_SEEK_CUR = 1;

    IWICImagingFactory? _factory;
    IStream? _stream;
    bool _failed;

    /// The frame as a JPEG at `quality` (1–100), or null to encode it with Skia.
    public byte[]? Encode(SKImage image, int quality)
    {
        if (_failed) return null;
        try
        {
            _factory ??= (IWICImagingFactory)new WicImagingFactory();
            _stream ??= CreateStream();
            return Encode(_factory, _stream, image, quality);
        }
        catch (Exception ex)
        {
            _failed = true; // first, so a log that throws can't bring the encoder back
            Release();
            log.Warn($"Encoding frames with Windows' JPEG encoder failed ({ex.Message}), so they are encoded with Skia from now on.");
            return null;
        }
    }

    static byte[] Encode(IWICImagingFactory factory, IStream stream, SKImage image, int quality)
    {
        var (pixels, copy) = Pixels(image);
        using var _ = copy;
        using var __ = pixels;
        var source = pixels.Info.ColorType == SKColorType.Bgra8888 ? Format32bppBgr : Format32bppRgb; // the 4th byte is ignored

        stream.Seek(0, STREAM_SEEK_SET, IntPtr.Zero); // reused: start each frame on an empty stream
        stream.SetSize(0);

        IWICBitmapEncoder? encoder = null;
        IWICBitmapFrameEncode? frame = null;
        var options = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        try
        {
            Check(factory.CreateEncoder(ContainerJpeg, IntPtr.Zero, out encoder));
            Check(encoder.Initialize(stream, NoCache));
            Check(encoder.CreateNewFrame(out frame, out options));
            SetQuality(options, quality / 100f);
            Check(frame.Initialize(options));
            Check(frame.SetSize((uint)pixels.Width, (uint)pixels.Height));
            var format = Format24bppBgr;
            Check(frame.SetPixelFormat(ref format));
            if (format != Format24bppBgr) throw new InvalidOperationException($"the encoder offered pixel format {format}");
            Check(factory.CreateBitmapFromMemory((uint)pixels.Width, (uint)pixels.Height, source, (uint)pixels.RowBytes,
                (uint)(pixels.RowBytes * pixels.Height), pixels.GetPixels(), out bitmap));
            Check(frame.WriteSource(bitmap, IntPtr.Zero)); // converts to the frame's format as it goes
            Check(frame.Commit());
            Check(encoder.Commit());
        }
        finally
        {
            if (bitmap != IntPtr.Zero) Marshal.Release(bitmap);
            if (options != IntPtr.Zero) Marshal.Release(options);
            if (frame is not null) Marshal.ReleaseComObject(frame);
            if (encoder is not null) Marshal.ReleaseComObject(encoder);
        }

        return Bytes(stream);
    }

    /// The image's pixels as 32-bit BGRA or RGBA: its own when it has them (a raster frame), otherwise a copy in a new
    /// bitmap, which the caller disposes after the pixmap.
    static (SKPixmap Pixels, SKBitmap? Copy) Pixels(SKImage image)
    {
        if (image.PeekPixels() is { } direct)
        {
            if (direct.ColorType is SKColorType.Bgra8888 or SKColorType.Rgba8888) return (direct, null);
            direct.Dispose();
        }
        var copy = new SKBitmap(new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (!image.ReadPixels(copy.Info, copy.GetPixels(), copy.RowBytes, 0, 0))
        {
            copy.Dispose();
            throw new InvalidOperationException("the frame's pixels could not be read");
        }
        return (copy.PeekPixels(), copy);
    }

    /// The encoder's options are an IPropertyBag2 that won't answer QueryInterface for its own IID, so .NET can't wrap
    /// it: its Write is called through the vtable (IUnknown's three methods, then Read, then Write).
    static unsafe void SetQuality(IntPtr options, float quality)
    {
        var name = Marshal.StringToCoTaskMemUni("ImageQuality");
        try
        {
            var property = new PROPBAG2 { dwType = PROPBAG2_TYPE_DATA, vt = VT_R4, pstrName = name };
            var value = new VARIANT { vt = VT_R4, r4 = quality };
            var write = (delegate* unmanaged[Stdcall]<IntPtr, uint, PROPBAG2*, VARIANT*, int>)(*(IntPtr**)options)[4];
            Check(write(options, 1, &property, &value));
        }
        finally
        {
            Marshal.FreeCoTaskMem(name);
        }
    }

    static unsafe byte[] Bytes(IStream stream)
    {
        long length;
        stream.Seek(0, STREAM_SEEK_CUR, (IntPtr)(&length));
        Check(GetHGlobalFromStream(stream, out var memory));
        var start = GlobalLock(memory);
        if (start == IntPtr.Zero) throw new InvalidOperationException("the encoded frame could not be read");
        try
        {
            var bytes = new byte[length];
            Marshal.Copy(start, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            GlobalUnlock(memory);
        }
    }

    static IStream CreateStream()
    {
        Check(CreateStreamOnHGlobal(IntPtr.Zero, true, out var stream));
        return stream;
    }

    static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    void Release()
    {
        if (_stream is not null) Marshal.ReleaseComObject(_stream);
        if (_factory is not null) Marshal.ReleaseComObject(_factory);
        _stream = null;
        _factory = null;
    }

    public void Dispose()
    {
        _failed = true;
        Release();
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    static extern int CreateStreamOnHGlobal(IntPtr memory, [MarshalAs(UnmanagedType.Bool)] bool deleteOnRelease, out IStream stream);

    [DllImport("ole32.dll", ExactSpelling = true)]
    static extern int GetHGlobalFromStream(IStream stream, out IntPtr memory);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GlobalUnlock(IntPtr memory);
}

// The interfaces follow wincodec.h's and ocidl.h's vtable order exactly, up to the last method Marquee calls. Methods it
// never calls hold their slot with no parameters.

/// CLSID_WICImagingFactory.
[ComImport, Guid("cacaf262-9370-4615-a13b-9f5539da4c0a")]
class WicImagingFactory
{
}

[ComImport, Guid("ec5ec8a9-c395-4314-9c77-54d7a935ff70"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IWICImagingFactory
{
    [PreserveSig] int CreateDecoderFromFilename();
    [PreserveSig] int CreateDecoderFromStream();
    [PreserveSig] int CreateDecoderFromFileHandle();
    [PreserveSig] int CreateComponentInfo();
    [PreserveSig] int CreateDecoder();
    [PreserveSig] int CreateEncoder(in Guid containerFormat, IntPtr vendor, out IWICBitmapEncoder encoder);
    [PreserveSig] int CreatePalette();
    [PreserveSig] int CreateFormatConverter();
    [PreserveSig] int CreateBitmapScaler();
    [PreserveSig] int CreateBitmapClipper();
    [PreserveSig] int CreateBitmapFlipRotator();
    [PreserveSig] int CreateStream();
    [PreserveSig] int CreateColorContext();
    [PreserveSig] int CreateColorTransformer();
    [PreserveSig] int CreateBitmap();
    [PreserveSig] int CreateBitmapFromSource();
    [PreserveSig] int CreateBitmapFromSourceRect();
    [PreserveSig] int CreateBitmapFromMemory(uint width, uint height, in Guid pixelFormat, uint stride, uint bufferSize, IntPtr buffer, out IntPtr bitmap);
}

[ComImport, Guid("00000103-a8f2-4877-ba0a-fd2b6645fb94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IWICBitmapEncoder
{
    [PreserveSig] int Initialize(IStream stream, int cacheOption);
    [PreserveSig] int GetContainerFormat();
    [PreserveSig] int GetEncoderInfo();
    [PreserveSig] int SetColorContexts();
    [PreserveSig] int SetPalette();
    [PreserveSig] int SetThumbnail();
    [PreserveSig] int SetPreview();
    [PreserveSig] int CreateNewFrame(out IWICBitmapFrameEncode frame, out IntPtr options);
    [PreserveSig] int Commit();
}

[ComImport, Guid("00000105-a8f2-4877-ba0a-fd2b6645fb94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IWICBitmapFrameEncode
{
    [PreserveSig] int Initialize(IntPtr options);
    [PreserveSig] int SetSize(uint width, uint height);
    [PreserveSig] int SetResolution();
    [PreserveSig] int SetPixelFormat(ref Guid pixelFormat);
    [PreserveSig] int SetColorContexts();
    [PreserveSig] int SetPalette();
    [PreserveSig] int SetThumbnail();
    [PreserveSig] int WritePixels();
    [PreserveSig] int WriteSource(IntPtr source, IntPtr rect);
    [PreserveSig] int Commit();
}

/// ocidl.h
[StructLayout(LayoutKind.Sequential)]
struct PROPBAG2
{
    public uint dwType;
    public ushort vt;
    public ushort cfType;
    public uint dwHint;
    public IntPtr pstrName;
    public Guid clsid;
}

/// A VARIANT holding a float (VT_R4), which is all the encoder's options take here.
[StructLayout(LayoutKind.Explicit, Size = 24)]
struct VARIANT
{
    [FieldOffset(0)] public ushort vt;
    [FieldOffset(8)] public float r4;
}
