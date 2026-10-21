using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Marquee.Core.Pipes;

/// The Win32 calls behind UserPipe that .NET has no way to make: creating a pipe whose security descriptor has a
/// mandatory label (PipeSecurity drops a label ACE, leaving an empty SACL that only an administrator may set), and
/// reading the label back.
static class UserPipeNative
{
    const uint PIPE_ACCESS_INBOUND = 0x00000001;
    const uint PIPE_ACCESS_OUTBOUND = 0x00000002;
    const uint PIPE_ACCESS_DUPLEX = 0x00000003;
    const uint FILE_FLAG_FIRST_PIPE_INSTANCE = 0x00080000;
    const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    const uint PIPE_TYPE_BYTE = 0x00000000;
    const uint PIPE_READMODE_BYTE = 0x00000000;
    const uint PIPE_WAIT = 0x00000000;
    const uint PIPE_REJECT_REMOTE_CLIENTS = 0x00000008;
    const uint PIPE_UNLIMITED_INSTANCES = 255;
    const int ERROR_ACCESS_DENIED = 5;

    const int SE_KERNEL_OBJECT = 6;
    const uint LABEL_SECURITY_INFORMATION = 0x00000010;
    const uint SDDL_REVISION_1 = 1;

    [StructLayout(LayoutKind.Sequential)]
    struct SECURITY_ATTRIBUTES
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        public int InheritHandle;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateNamedPipeW", ExactSpelling = true)]
    static extern SafePipeHandle CreateNamedPipe(string name, uint openMode, uint pipeMode, uint maxInstances, uint outBufferSize,
        uint inBufferSize, uint defaultTimeout, ref SECURITY_ATTRIBUTES securityAttributes);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    static extern uint GetSecurityInfo(SafeHandle handle, int objectType, uint securityInfo, IntPtr owner, IntPtr group,
        IntPtr dacl, IntPtr sacl, out IntPtr securityDescriptor);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode,
        EntryPoint = "ConvertSecurityDescriptorToStringSecurityDescriptorW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool ConvertSecurityDescriptorToStringSecurityDescriptor(IntPtr securityDescriptor, uint revision,
        uint securityInfo, out IntPtr text, out uint length);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    static extern IntPtr LocalFree(IntPtr memory);

    /// An instance of the local byte-mode pipe `name`, asynchronous, with no buffers, refusing remote clients (as
    /// NamedPipeServerStream's own), with the security descriptor `descriptor` in its binary form. Throws
    /// UnauthorizedAccessException when Windows denies it (with `firstInstance`, also when the pipe exists already), and
    /// IOException for the rest, such as all `maxInstances` instances being open already.
    public static NamedPipeServerStream Create(string name, PipeDirection direction, int maxInstances, bool firstInstance,
        byte[] descriptor)
    {
        var access = direction switch
        {
            PipeDirection.In => PIPE_ACCESS_INBOUND,
            PipeDirection.Out => PIPE_ACCESS_OUTBOUND,
            _ => PIPE_ACCESS_DUPLEX,
        };
        var openMode = access | FILE_FLAG_OVERLAPPED | (firstInstance ? FILE_FLAG_FIRST_PIPE_INSTANCE : 0);
        const uint PipeMode = PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS;

        var pinned = GCHandle.Alloc(descriptor, GCHandleType.Pinned);
        SafePipeHandle handle;
        try
        {
            var attributes = new SECURITY_ATTRIBUTES
            {
                Length = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
                SecurityDescriptor = pinned.AddrOfPinnedObject(),
            };
            handle = CreateNamedPipe($@"\\.\pipe\{name}", openMode, PipeMode, Instances(maxInstances), 0, 0, 0, ref attributes);
        }
        finally
        {
            pinned.Free();
        }

        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            var message = new Win32Exception(error).Message;
            throw error == ERROR_ACCESS_DENIED ? new UnauthorizedAccessException(message) : new IOException(message, unchecked((int)0x80070000) | error);
        }

        try
        {
            return new NamedPipeServerStream(direction, isAsync: true, isConnected: false, handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// NamedPipeServerStream.MaxAllowedServerInstances (-1) is Windows' PIPE_UNLIMITED_INSTANCES, as .NET maps it.
    static uint Instances(int maxInstances) =>
        maxInstances == NamedPipeServerStream.MaxAllowedServerInstances ? PIPE_UNLIMITED_INSTANCES
        : maxInstances is >= 1 and < (int)PIPE_UNLIMITED_INSTANCES ? (uint)maxInstances
        : throw new ArgumentOutOfRangeException(nameof(maxInstances));

    /// The label part of the pipe's security descriptor in SDDL, such as "S:(ML;;NWNR;;;ME)"; empty when it has none,
    /// which Windows treats as Medium with no-write-up only. Any handle with READ_CONTROL will do, a client's or the
    /// server's.
    public static string Label(SafePipeHandle pipe)
    {
        var error = GetSecurityInfo(pipe, SE_KERNEL_OBJECT, LABEL_SECURITY_INFORMATION, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
            IntPtr.Zero, out var descriptor);
        if (error != 0) throw new Win32Exception((int)error);
        try
        {
            if (!ConvertSecurityDescriptorToStringSecurityDescriptor(descriptor, SDDL_REVISION_1, LABEL_SECURITY_INFORMATION,
                    out var text, out _))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            try
            {
                return Marshal.PtrToStringUni(text) ?? "";
            }
            finally
            {
                LocalFree(text);
            }
        }
        finally
        {
            LocalFree(descriptor);
        }
    }
}
