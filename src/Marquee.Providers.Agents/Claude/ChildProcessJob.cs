using System.ComponentModel;
using System.Runtime.InteropServices;
using static Marquee.Providers.Agents.Claude.NativeMethods;

namespace Marquee.Providers.Agents.Claude;

/// A Win32 job object that kills every process in it when its handle closes. Marquee keeps one (`Shared`) for the
/// `claude` children it starts and never closes it, so the kernel closes it when Marquee exits, even on a crash, and a
/// `claude` still running then is killed with it.
internal sealed class ChildProcessJob : IDisposable
{
    static readonly Lazy<ChildProcessJob> SharedJob = new(() => new ChildProcessJob());

    /// The job every `claude` child goes into. Created on first use.
    public static ChildProcessJob Shared => SharedJob.Value;

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

    /// Puts the process in the job. Throws Win32Exception if Windows refuses.
    public void Assign(SafeHandle process)
    {
        if (!AssignProcessToJobObject(_job, process))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "AssignProcessToJobObject failed");
    }

    /// Whether the process is in this job. For tests.
    internal bool Contains(SafeHandle process) =>
        IsProcessInJob(process, _job, out var result) ? result : throw new Win32Exception(Marshal.GetLastWin32Error(), "IsProcessInJob failed");

    /// Closes the job handle, which kills every process still in it. Only tests close a job; `Shared` is never closed.
    public void Dispose() => _job.Dispose();
}
