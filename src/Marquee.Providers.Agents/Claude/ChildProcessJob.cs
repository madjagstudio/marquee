using System.ComponentModel;
using System.Runtime.InteropServices;
using static Marquee.Providers.Agents.Claude.NativeMethods;

namespace Marquee.Providers.Agents.Claude;

/// A Win32 job object that kills every process in it when its handle closes. Each `claude` child gets a job of its own,
/// created just before it starts and closed when its run is over: if Marquee exits or crashes meanwhile, the kernel
/// closes the handle and the child dies with it, and closing it after a normal run ends anything the child left behind.
///
/// A job of its own, not one shared job, because Windows lets a process that is already in a job join another only if
/// that job is empty or in the same job tree. A child started with the desktop shell's token (UnelevatedProcess) begins
/// inside the Secondary Logon service's job, and Marquee itself may run inside a job (`dotnet test`, Task Scheduler), so a
/// shared job that had held earlier children was refused with "Access is denied". A new, empty job is always accepted.
internal sealed class ChildProcessJob : IDisposable
{
    readonly KernelHandle _job;

    public ChildProcessJob()
    {
        _job = CreateJobObject(IntPtr.Zero, IntPtr.Zero);
        if (_job.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateJobObject failed");

        var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        if (!SetInformationJobObject(_job, JobObjectExtendedLimitInformation, ref limits, Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
        {
            var error = Marshal.GetLastWin32Error();
            _job.Dispose();
            throw new Win32Exception(error, "SetInformationJobObject failed");
        }
    }

    /// Called right after a process is put in a job. Tests set it to see the job a run used; it flows with the test's
    /// async context, so tests running in parallel don't see each other's.
    internal static readonly AsyncLocal<Action<ChildProcessJob, SafeHandle>?> Assigned = new();

    /// Puts the process in the job. Throws Win32Exception if Windows refuses.
    public void Assign(SafeHandle process)
    {
        if (!AssignProcessToJobObject(_job, process))
        {
            var error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, $"AssignProcessToJobObject failed: {new Win32Exception(error).Message.TrimEnd('.')} (error {error})");
        }

        Assigned.Value?.Invoke(this, process);
    }

    /// Whether the process is in this job. For tests.
    internal bool Contains(SafeHandle process) =>
        IsProcessInJob(process, _job, out var result) ? result : throw new Win32Exception(Marshal.GetLastWin32Error(), "IsProcessInJob failed");

    /// Closes the job handle, which kills every process still in it.
    public void Dispose() => _job.Dispose();
}
