using System.Runtime.InteropServices;

namespace Marquee.Core.Video;

/// Windows Media Foundation: the platform calls, attribute keys and error codes the video decoder uses. Its DLLs load
/// from System32 only, like every native library Core calls (see AssemblyInfo.cs), never from the exe's folder.
static class MediaFoundation
{
    static readonly Lazy<bool> Started = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    /// Whether Media Foundation is installed and started. It isn't on Windows N editions until the user installs the
    /// Media Feature Pack, nor on Windows Server without its Media Foundation feature. The first call starts it.
    public static bool IsAvailable => Started.Value;

    /// Media Foundation is started once and never shut down. MFShutdown is only safe once every Media Foundation object
    /// in the process is released, and decoders are opened and disposed on several threads as themes load and reload,
    /// so a shutdown would need a process-wide reference count for no gain: Windows releases it all at exit, and a
    /// started Media Foundation costs nothing while no clip is open.
    static bool Start()
    {
        try
        {
            // mfreadwrite.dll (the source reader) needs mfplat.dll, so loading it checks for both.
            if (!NativeLibrary.TryLoad("mfreadwrite.dll", typeof(MediaFoundation).Assembly, DllImportSearchPath.System32, out _))
                return false;
            return MFStartup(MF_VERSION, MFSTARTUP_LITE) >= 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// MF_SDK_VERSION 0x0002 in the high word, MF_API_VERSION 0x0070 in the low word (mfapi.h).
    const uint MF_VERSION = 0x00020070;

    /// Starts without the sockets library, which only network sources need.
    const uint MFSTARTUP_LITE = 0x1;

    public const uint MF_SOURCE_READER_FIRST_VIDEO_STREAM = 0xFFFFFFFC;
    public const uint MF_SOURCE_READER_ALL_STREAMS = 0xFFFFFFFE;

    public const uint MF_SOURCE_READERF_ERROR = 0x1;
    public const uint MF_SOURCE_READERF_ENDOFSTREAM = 0x2;
    public const uint MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED = 0x20;

    public const uint MFVideoInterlace_Progressive = 2;
    public const ushort VT_I8 = 20;

    public const int MF_E_INVALIDSTREAMNUMBER = unchecked((int)0xC00D36B3);
    public const int MF_E_INVALIDMEDIATYPE = unchecked((int)0xC00D36B4);
    public const int MF_E_INVALID_FILE_FORMAT = unchecked((int)0xC00D36BE);
    public const int MF_E_UNSUPPORTED_BYTESTREAM_TYPE = unchecked((int)0xC00D36C4);
    public const int MF_E_END_OF_STREAM = unchecked((int)0xC00D3E84);
    public const int MF_E_TOPO_CODEC_NOT_FOUND = unchecked((int)0xC00D5212);

    public static readonly Guid MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING = new("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");
    public static readonly Guid MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING = new("0f81da2c-b537-4672-a8b2-a681b17307a3");
    public static readonly Guid MF_SOURCE_READER_D3D_MANAGER = new("ec822da2-e1e9-4b29-a0d8-563c719f5269");
    public static readonly Guid MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = new("a634a91c-822b-41b9-a494-4de4643612b0");
    public static readonly Guid MF_MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    public static readonly Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static readonly Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
    public static readonly Guid MF_MT_FRAME_RATE = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    public static readonly Guid MF_MT_DEFAULT_STRIDE = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    public static readonly Guid MF_MT_MINIMUM_DISPLAY_APERTURE = new("d7388766-18fe-48c6-a177-ee894867c8c4");
    public static readonly Guid MF_MT_GEOMETRIC_APERTURE = new("66758743-7e5f-400d-980a-aa8596c85696");
    public static readonly Guid MF_MT_PIXEL_ASPECT_RATIO = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    public static readonly Guid MF_MT_AVG_BITRATE = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    public static readonly Guid MF_MT_INTERLACE_MODE = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    public static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00aa00389b71");
    public static readonly Guid MFVideoFormat_RGB32 = new("00000016-0000-0010-8000-00aa00389b71");
    public static readonly Guid MFVideoFormat_H264 = new("34363248-0000-0010-8000-00aa00389b71");

    /// The file name a byte stream came from, whose extension picks the container parser, as it would for a URL.
    public static readonly Guid MF_BYTESTREAM_ORIGIN_NAME = new("fc358288-3cb6-460c-a424-b6681260375a");

    /// Two 32-bit values packed in one UINT64 attribute (frame size, frame rate, aspect ratio): high word first.
    public static ulong Pack(uint high, uint low) => ((ulong)high << 32) | low;

    public static (uint High, uint Low) Unpack(ulong value) => ((uint)(value >> 32), (uint)value);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    static extern int MFStartup(uint version, uint flags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateAttributes(out IMFAttributes? attributes, uint initialSize);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateMediaType(out IMFMediaType? mediaType);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateMFByteStreamOnStream(System.Runtime.InteropServices.ComTypes.IStream stream,
        out IMFByteStream? byteStream);

    [DllImport("mfreadwrite.dll", ExactSpelling = true)]
    public static extern int MFCreateSourceReaderFromByteStream(IMFByteStream byteStream, IMFAttributes? attributes,
        out IMFSourceReader? reader);

    /// Releases a Media Foundation object now instead of when the garbage collector finds its wrapper. Each object
    /// here is wrapped once and used by one owner, so the final release is the right one.
    public static void Release(object? comObject)
    {
        if (comObject is not null) Marshal.FinalReleaseComObject(comObject);
    }
}

/// A PROPVARIANT holding a 64-bit integer, the only kind the decoder passes (a seek position). The union is 16 bytes on
/// 64-bit Windows; the callee reads only the type and the first 8 bytes.
[StructLayout(LayoutKind.Explicit, Size = 24)]
struct PropVariant
{
    [FieldOffset(0)] public ushort Type;
    [FieldOffset(8)] public long Value;
}

/// MFVideoArea (mfobjects.h): a rectangle within a frame, as an aperture attribute's blob. The offsets are fixed-point
/// with a 16-bit fraction, which is always 0 for the whole pixels decoders use.
[StructLayout(LayoutKind.Sequential)]
struct MFVideoArea
{
    public ushort OffsetXFraction;
    public short OffsetX;
    public ushort OffsetYFraction;
    public short OffsetY;
    public int Width;
    public int Height;
}

// The interfaces follow the Windows SDK headers' vtable order exactly. COM interop can't inherit an interface's methods,
// so IMFMediaType and IMFSample repeat IMFAttributes' 30 methods, in order, before their own. Each interface is declared
// up to the last method Marquee calls; methods it never calls take raw pointers.

/// mfobjects.h
[ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFAttributes
{
    [PreserveSig] int GetItem(in Guid key, IntPtr value);
    [PreserveSig] int GetItemType(in Guid key, out uint type);
    [PreserveSig] int CompareItem(in Guid key, IntPtr value, out int result);
    [PreserveSig] int Compare(IntPtr theirs, int matchType, out int result);
    [PreserveSig] int GetUINT32(in Guid key, out uint value);
    [PreserveSig] int GetUINT64(in Guid key, out ulong value);
    [PreserveSig] int GetDouble(in Guid key, out double value);
    [PreserveSig] int GetGUID(in Guid key, out Guid value);
    [PreserveSig] int GetStringLength(in Guid key, out uint length);
    [PreserveSig] int GetString(in Guid key, IntPtr buffer, uint bufferSize, IntPtr length);
    [PreserveSig] int GetAllocatedString(in Guid key, out IntPtr value, out uint length);
    [PreserveSig] int GetBlobSize(in Guid key, out uint size);
    [PreserveSig] int GetBlob(in Guid key, IntPtr buffer, uint bufferSize, IntPtr size);
    [PreserveSig] int GetAllocatedBlob(in Guid key, out IntPtr buffer, out uint size);
    [PreserveSig] int GetUnknown(in Guid key, in Guid iid, out IntPtr value);
    [PreserveSig] int SetItem(in Guid key, IntPtr value);
    [PreserveSig] int DeleteItem(in Guid key);
    [PreserveSig] int DeleteAllItems();
    [PreserveSig] int SetUINT32(in Guid key, uint value);
    [PreserveSig] int SetUINT64(in Guid key, ulong value);
    [PreserveSig] int SetDouble(in Guid key, double value);
    [PreserveSig] int SetGUID(in Guid key, in Guid value);
    [PreserveSig] int SetString(in Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
    [PreserveSig] int SetBlob(in Guid key, IntPtr buffer, uint size);
    [PreserveSig] int SetUnknown(in Guid key, IntPtr unknown);
    [PreserveSig] int LockStore();
    [PreserveSig] int UnlockStore();
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetItemByIndex(uint index, out Guid key, IntPtr value);
    [PreserveSig] int CopyAllItems(IntPtr destination);
}

/// mfobjects.h. IMFAttributes' methods, then IMFMediaType's own (none of which Marquee calls).
[ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFMediaType
{
    [PreserveSig] int GetItem(in Guid key, IntPtr value);
    [PreserveSig] int GetItemType(in Guid key, out uint type);
    [PreserveSig] int CompareItem(in Guid key, IntPtr value, out int result);
    [PreserveSig] int Compare(IntPtr theirs, int matchType, out int result);
    [PreserveSig] int GetUINT32(in Guid key, out uint value);
    [PreserveSig] int GetUINT64(in Guid key, out ulong value);
    [PreserveSig] int GetDouble(in Guid key, out double value);
    [PreserveSig] int GetGUID(in Guid key, out Guid value);
    [PreserveSig] int GetStringLength(in Guid key, out uint length);
    [PreserveSig] int GetString(in Guid key, IntPtr buffer, uint bufferSize, IntPtr length);
    [PreserveSig] int GetAllocatedString(in Guid key, out IntPtr value, out uint length);
    [PreserveSig] int GetBlobSize(in Guid key, out uint size);
    [PreserveSig] int GetBlob(in Guid key, IntPtr buffer, uint bufferSize, IntPtr size);
    [PreserveSig] int GetAllocatedBlob(in Guid key, out IntPtr buffer, out uint size);
    [PreserveSig] int GetUnknown(in Guid key, in Guid iid, out IntPtr value);
    [PreserveSig] int SetItem(in Guid key, IntPtr value);
    [PreserveSig] int DeleteItem(in Guid key);
    [PreserveSig] int DeleteAllItems();
    [PreserveSig] int SetUINT32(in Guid key, uint value);
    [PreserveSig] int SetUINT64(in Guid key, ulong value);
    [PreserveSig] int SetDouble(in Guid key, double value);
    [PreserveSig] int SetGUID(in Guid key, in Guid value);
    [PreserveSig] int SetString(in Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
    [PreserveSig] int SetBlob(in Guid key, IntPtr buffer, uint size);
    [PreserveSig] int SetUnknown(in Guid key, IntPtr unknown);
    [PreserveSig] int LockStore();
    [PreserveSig] int UnlockStore();
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetItemByIndex(uint index, out Guid key, IntPtr value);
    [PreserveSig] int CopyAllItems(IntPtr destination);
}

/// mfobjects.h. IMFAttributes' methods, then IMFSample's own up to AddBuffer.
[ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFSample
{
    [PreserveSig] int GetItem(in Guid key, IntPtr value);
    [PreserveSig] int GetItemType(in Guid key, out uint type);
    [PreserveSig] int CompareItem(in Guid key, IntPtr value, out int result);
    [PreserveSig] int Compare(IntPtr theirs, int matchType, out int result);
    [PreserveSig] int GetUINT32(in Guid key, out uint value);
    [PreserveSig] int GetUINT64(in Guid key, out ulong value);
    [PreserveSig] int GetDouble(in Guid key, out double value);
    [PreserveSig] int GetGUID(in Guid key, out Guid value);
    [PreserveSig] int GetStringLength(in Guid key, out uint length);
    [PreserveSig] int GetString(in Guid key, IntPtr buffer, uint bufferSize, IntPtr length);
    [PreserveSig] int GetAllocatedString(in Guid key, out IntPtr value, out uint length);
    [PreserveSig] int GetBlobSize(in Guid key, out uint size);
    [PreserveSig] int GetBlob(in Guid key, IntPtr buffer, uint bufferSize, IntPtr size);
    [PreserveSig] int GetAllocatedBlob(in Guid key, out IntPtr buffer, out uint size);
    [PreserveSig] int GetUnknown(in Guid key, in Guid iid, out IntPtr value);
    [PreserveSig] int SetItem(in Guid key, IntPtr value);
    [PreserveSig] int DeleteItem(in Guid key);
    [PreserveSig] int DeleteAllItems();
    [PreserveSig] int SetUINT32(in Guid key, uint value);
    [PreserveSig] int SetUINT64(in Guid key, ulong value);
    [PreserveSig] int SetDouble(in Guid key, double value);
    [PreserveSig] int SetGUID(in Guid key, in Guid value);
    [PreserveSig] int SetString(in Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
    [PreserveSig] int SetBlob(in Guid key, IntPtr buffer, uint size);
    [PreserveSig] int SetUnknown(in Guid key, IntPtr unknown);
    [PreserveSig] int LockStore();
    [PreserveSig] int UnlockStore();
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetItemByIndex(uint index, out Guid key, IntPtr value);
    [PreserveSig] int CopyAllItems(IntPtr destination);

    [PreserveSig] int GetSampleFlags(out uint flags);
    [PreserveSig] int SetSampleFlags(uint flags);
    [PreserveSig] int GetSampleTime(out long time);
    [PreserveSig] int SetSampleTime(long time);
    [PreserveSig] int GetSampleDuration(out long duration);
    [PreserveSig] int SetSampleDuration(long duration);
    [PreserveSig] int GetBufferCount(out uint count);
    [PreserveSig] int GetBufferByIndex(uint index, out IMFMediaBuffer? buffer);
    [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer? buffer);
    [PreserveSig] int AddBuffer(IMFMediaBuffer buffer);
}

/// mfobjects.h. Marquee only hands it on, and sets its attributes through IMFAttributes, so none of its own methods
/// are declared.
[ComImport, Guid("ad4c1b00-4bf7-422f-9175-756693d9130d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFByteStream
{
}

/// mfobjects.h, complete.
[ComImport, Guid("045fa593-8799-42b8-bc8d-8968c6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFMediaBuffer
{
    [PreserveSig] int Lock(out IntPtr data, out uint maxLength, out uint currentLength);
    [PreserveSig] int Unlock();
    [PreserveSig] int GetCurrentLength(out uint length);
    [PreserveSig] int SetCurrentLength(uint length);
    [PreserveSig] int GetMaxLength(out uint length);
}

/// mfreadwrite.h, up to ReadSample.
[ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFSourceReader
{
    [PreserveSig] int GetStreamSelection(uint streamIndex, out int selected);
    [PreserveSig] int SetStreamSelection(uint streamIndex, int selected);
    [PreserveSig] int GetNativeMediaType(uint streamIndex, uint mediaTypeIndex, out IMFMediaType? mediaType);
    [PreserveSig] int GetCurrentMediaType(uint streamIndex, out IMFMediaType? mediaType);
    [PreserveSig] int SetCurrentMediaType(uint streamIndex, IntPtr reserved, IMFMediaType mediaType);
    [PreserveSig] int SetCurrentPosition(in Guid timeFormat, in PropVariant position);
    [PreserveSig] int ReadSample(uint streamIndex, uint controlFlags, out uint actualStreamIndex, out uint streamFlags,
        out long timestamp, out IMFSample? sample);
}
