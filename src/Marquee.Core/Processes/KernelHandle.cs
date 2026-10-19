using Microsoft.Win32.SafeHandles;

namespace Marquee.Core.Processes;

/// A kernel handle closed with CloseHandle: a job, a process, a thread or a token.
public sealed class KernelHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public KernelHandle() : base(true) { }

    public KernelHandle(IntPtr handle) : base(true) => SetHandle(handle);

    protected override bool ReleaseHandle() => ProcessNative.CloseHandle(handle);
}
