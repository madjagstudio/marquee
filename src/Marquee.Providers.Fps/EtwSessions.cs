using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Marquee.Providers.Fps;

/// The machine's ETW sessions, through QueryAllTraces and ControlTrace. Both need administrator rights, as PresentMon
/// does, so they are only used when Marquee is elevated.
sealed class EtwSessions : IEtwSessions
{
    public static EtwSessions Instance { get; } = new();

    const int ERROR_MORE_DATA = 234;
    const int ERROR_WMI_INSTANCE_NOT_FOUND = 4201;
    const uint EVENT_TRACE_CONTROL_STOP = 1;

    /// ETW writes a session's name and log file name after its properties; each is at most 1,024 characters.
    static readonly int PropertiesSize = Marshal.SizeOf<EVENT_TRACE_PROPERTIES>();
    const int NameBytes = 1024 * sizeof(char);
    static readonly int BufferSize = PropertiesSize + 2 * NameBytes;

    /// Windows runs 64 sessions at once unless configured otherwise; room for more is made only when there are more.
    const int FirstGuess = 64;
    const int MostSessions = 4096;

    EtwSessions() { }

    public IReadOnlyList<string> Names()
    {
        for (var count = FirstGuess; ; count *= 2)
        {
            var buffers = new IntPtr[count];
            try
            {
                for (var i = 0; i < count; i++) buffers[i] = NewProperties();
                var error = QueryAllTraces(buffers, (uint)count, out var found);
                if (error == ERROR_MORE_DATA && count < MostSessions) continue;
                if (error != 0) throw new Win32Exception(error);

                var names = new List<string>();
                for (var i = 0; i < Math.Min(found, (uint)count); i++)
                {
                    var properties = Marshal.PtrToStructure<EVENT_TRACE_PROPERTIES>(buffers[i]);
                    if (properties.LoggerNameOffset > 0
                        && Marshal.PtrToStringUni(buffers[i] + (int)properties.LoggerNameOffset) is { Length: > 0 } name)
                        names.Add(name);
                }

                return names;
            }
            finally
            {
                foreach (var buffer in buffers)
                    if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            }
        }
    }

    public void Stop(string name)
    {
        var properties = NewProperties();
        try
        {
            var error = ControlTrace(0, name, properties, EVENT_TRACE_CONTROL_STOP);
            // ERROR_MORE_DATA only means the session's properties didn't fit; it has been stopped all the same.
            if (error is not (0 or ERROR_WMI_INSTANCE_NOT_FOUND or ERROR_MORE_DATA)) throw new Win32Exception(error);
        }
        finally
        {
            Marshal.FreeHGlobal(properties);
        }
    }

    /// Zeroed properties with room for both names after them, as both calls want.
    static IntPtr NewProperties()
    {
        var buffer = Marshal.AllocHGlobal(BufferSize);
        Marshal.Copy(new byte[BufferSize], 0, buffer, BufferSize);
        var properties = new EVENT_TRACE_PROPERTIES
        {
            Wnode = new WNODE_HEADER { BufferSize = (uint)BufferSize },
            LoggerNameOffset = (uint)PropertiesSize,
            LogFileNameOffset = (uint)(PropertiesSize + NameBytes),
        };
        Marshal.StructureToPtr(properties, buffer, fDeleteOld: false);
        return buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WNODE_HEADER
    {
        public uint BufferSize;
        public uint ProviderId;
        public ulong HistoricalContext;
        public long TimeStamp;
        public Guid Guid;
        public uint ClientContext;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct EVENT_TRACE_PROPERTIES
    {
        public WNODE_HEADER Wnode;
        public uint BufferSize;
        public uint MinimumBuffers;
        public uint MaximumBuffers;
        public uint MaximumFileSize;
        public uint LogFileMode;
        public uint FlushTimer;
        public uint EnableFlags;
        public int AgeLimit;
        public uint NumberOfBuffers;
        public uint FreeBuffers;
        public uint EventsLost;
        public uint BuffersWritten;
        public uint LogBuffersLost;
        public uint RealTimeBuffersLost;
        public IntPtr LoggerThreadId;
        public uint LogFileNameOffset;
        public uint LoggerNameOffset;
    }

    [DllImport("advapi32.dll", EntryPoint = "QueryAllTracesW", ExactSpelling = true)]
    static extern int QueryAllTraces([In] IntPtr[] propertyArray, uint propertyArrayCount, out uint loggerCount);

    [DllImport("advapi32.dll", EntryPoint = "ControlTraceW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int ControlTrace(ulong traceHandle, string instanceName, IntPtr properties, uint controlCode);
}
