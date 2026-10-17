using System.ComponentModel;
using System.Runtime.InteropServices;
using Marquee.Core.Devices;
using Microsoft.Win32.SafeHandles;
using static Marquee.Core.Usb.WinUsbNative;

namespace Marquee.Core.Usb;

/// Bulk transfers through Windows' inbox WinUSB driver. Calls are synchronous (null OVERLAPPED).
public sealed class WinUsbTransport : IUsbTransport
{
    const byte OutPipe = 0x01;
    const byte InPipe = 0x81;

    readonly SafeFileHandle _file;
    readonly IntPtr _winUsb;
    readonly Dictionary<byte, uint> _timeouts = new();

    WinUsbTransport(SafeFileHandle file, IntPtr winUsb)
    {
        _file = file;
        _winUsb = winUsb;
    }

    public static IReadOnlyList<UsbDeviceEntry> Enumerate()
    {
        var guid = UsbDeviceInterface;
        var set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == new IntPtr(-1)) throw new Win32Exception();

        var entries = new List<UsbDeviceEntry>();
        try
        {
            var data = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data); i++)
            {
                if (ReadDevicePath(set, ref data) is { } path && UsbDeviceEntry.FromPath(path) is { } entry)
                    entries.Add(entry);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
        return entries;
    }

    public static WinUsbTransport Open(string devicePath)
    {
        var file = CreateFile(devicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (file.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            file.Dispose();
            throw IsBusy(error) ? Busy() : new Win32Exception(error);
        }

        if (!WinUsb_Initialize(file, out var winUsb))
        {
            var error = Marshal.GetLastWin32Error();
            file.Dispose();
            throw IsBusy(error) ? Busy() : new Win32Exception(error);
        }

        return new WinUsbTransport(file, winUsb);
    }

    public void Write(byte[] data, int timeoutMs)
    {
        SetTimeout(OutPipe, timeoutMs);
        if (!WinUsb_WritePipe(_winUsb, OutPipe, data, (uint)data.Length, out var written, IntPtr.Zero))
            throw new IOException("USB write failed.", new Win32Exception(Marshal.GetLastWin32Error()));
        if (written != data.Length)
            throw new IOException($"USB write sent {written} of {data.Length} bytes.");
    }

    public int Read(byte[] buffer, int timeoutMs)
    {
        SetTimeout(InPipe, timeoutMs);
        if (WinUsb_ReadPipe(_winUsb, InPipe, buffer, (uint)buffer.Length, out var read, IntPtr.Zero))
            return (int)read;

        var error = Marshal.GetLastWin32Error();
        if (error == ERROR_SEM_TIMEOUT) return 0;
        throw new IOException("USB read failed.", new Win32Exception(error));
    }

    public void Dispose()
    {
        WinUsb_Free(_winUsb);
        _file.Dispose();
    }

    void SetTimeout(byte pipe, int timeoutMs)
    {
        var value = (uint)timeoutMs;
        if (_timeouts.TryGetValue(pipe, out var current) && current == value) return;
        if (!WinUsb_SetPipePolicy(_winUsb, pipe, PIPE_TRANSFER_TIMEOUT, sizeof(uint), ref value))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        _timeouts[pipe] = value;
    }

    static string? ReadDevicePath(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data)
    {
        SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out var size, IntPtr.Zero);
        if (size == 0) return null;

        var detail = Marshal.AllocHGlobal((int)size);
        try
        {
            // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize: 8 on 64-bit, 6 on 32-bit.
            Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
            if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, size, out _, IntPtr.Zero)) return null;
            return Marshal.PtrToStringUni(detail + 4);
        }
        finally
        {
            Marshal.FreeHGlobal(detail);
        }
    }

    static bool IsBusy(int error) => error is ERROR_ACCESS_DENIED or ERROR_SHARING_VIOLATION;

    static DeviceBusyException Busy() =>
        new("The screen is in use by another program (usually L-Connect 3). Close it and try again.");
}
