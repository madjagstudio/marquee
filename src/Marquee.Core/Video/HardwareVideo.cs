using System.Runtime.InteropServices;

namespace Marquee.Core.Video;

/// A Direct3D 11 device and the Media Foundation device manager that shares it, which let the source reader decode on
/// the graphics card and convert to RGB there too: a software H.264 decode of a 480×1920 clip costs about 8 ms of CPU a
/// frame, the same on the GPU about 1. Create returns null wherever there is no such device (no graphics driver, a basic
/// display adapter, some Remote Desktop sessions), and the decoder then uses the software path, as before.
///
/// The decoder owns one per clip and disposes it after the reader that uses it. Its own threads and Media Foundation's
/// share the device, so it is made thread-safe (multithread protected), as Media Foundation requires.
sealed class HardwareVideo : IDisposable
{
    const int D3D_DRIVER_TYPE_HARDWARE = 1;
    const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    const uint D3D11_CREATE_DEVICE_VIDEO_SUPPORT = 0x800;
    const uint D3D11_SDK_VERSION = 7;

    static readonly Guid ID3D10MultithreadId = new("9b7e4e00-342c-4106-a19f-4f2704f689f0");

    IntPtr _device; // ID3D11Device
    IMFDXGIDeviceManager? _manager;

    HardwareVideo(IntPtr device, IMFDXGIDeviceManager manager) => (_device, _manager) = (device, manager);

    /// A device manager on the default graphics card, or null if this PC can't decode video on one.
    public static HardwareVideo? Create()
    {
        IntPtr device = IntPtr.Zero, context = IntPtr.Zero;
        IMFDXGIDeviceManager? manager = null;
        try
        {
            if (D3D11CreateDevice(IntPtr.Zero, D3D_DRIVER_TYPE_HARDWARE, IntPtr.Zero,
                    D3D11_CREATE_DEVICE_VIDEO_SUPPORT | D3D11_CREATE_DEVICE_BGRA_SUPPORT, IntPtr.Zero, 0, D3D11_SDK_VERSION,
                    out device, out _, out context) < 0)
                return null;

            if (!MakeMultithreaded(device)) return null;
            if (MFCreateDXGIDeviceManager(out var token, out manager) < 0 || manager is null) return null;
            if (manager.ResetDevice(device, token) < 0) return null;

            var made = new HardwareVideo(device, manager);
            (device, manager) = (IntPtr.Zero, null); // owned by `made` now
            return made;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or COMException)
        {
            return null;
        }
        finally
        {
            if (context != IntPtr.Zero) Marshal.Release(context);
            if (device != IntPtr.Zero) Marshal.Release(device);
            MediaFoundation.Release(manager);
        }
    }

    /// The device manager as an IUnknown pointer, for the source reader's MF_SOURCE_READER_D3D_MANAGER attribute. The
    /// caller releases the pointer; the attribute store holds its own reference.
    public IntPtr ManagerPointer() =>
        Marshal.GetIUnknownForObject(_manager ?? throw new ObjectDisposedException(nameof(HardwareVideo)));

    public void Dispose()
    {
        MediaFoundation.Release(_manager);
        _manager = null;
        if (_device != IntPtr.Zero) Marshal.Release(_device);
        _device = IntPtr.Zero;
    }

    static bool MakeMultithreaded(IntPtr device)
    {
        if (Marshal.QueryInterface(device, in ID3D10MultithreadId, out var pointer) < 0) return false;
        try
        {
            var multithread = (ID3D10Multithread)Marshal.GetObjectForIUnknown(pointer);
            try
            {
                multithread.SetMultithreadProtected(1);
                return true;
            }
            finally
            {
                MediaFoundation.Release(multithread);
            }
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr featureLevels,
        uint featureLevelCount, uint sdkVersion, out IntPtr device, out int featureLevel, out IntPtr immediateContext);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    static extern int MFCreateDXGIDeviceManager(out uint resetToken, out IMFDXGIDeviceManager? manager);
}

// Vtable order as in the Windows SDK headers (d3d10misc.h, mfobjects.h); only the methods used are called.

[ComImport, Guid("9b7e4e00-342c-4106-a19f-4f2704f689f0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ID3D10Multithread
{
    [PreserveSig] void Enter();
    [PreserveSig] void Leave();
    [PreserveSig] int SetMultithreadProtected(int protect);
    [PreserveSig] int GetMultithreadProtected();
}

[ComImport, Guid("eb533d5d-2db6-40f8-97a9-494692014f07"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFDXGIDeviceManager
{
    [PreserveSig] int CloseDeviceHandle(IntPtr device);
    [PreserveSig] int GetVideoService(IntPtr device, in Guid riid, out IntPtr service);
    [PreserveSig] int LockDevice(IntPtr device, in Guid riid, out IntPtr unknown, int block);
    [PreserveSig] int OpenDeviceHandle(out IntPtr device);
    [PreserveSig] int ResetDevice(IntPtr device, uint resetToken);
    [PreserveSig] int TestDevice(IntPtr device);
    [PreserveSig] int UnlockDevice(IntPtr device, int saveState);
}
