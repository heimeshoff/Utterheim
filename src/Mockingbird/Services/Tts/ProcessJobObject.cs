using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Mockingbird.Services.Tts;

/// <summary>
/// Win32 Job Object wrapper used to bind the python sidecar (and every process
/// it ever spawns) to the host's lifetime.
///
/// Why this exists: <c>Process.Kill(entireProcessTree: true)</c> walks the
/// parent-PID tree at kill time. If a grandchild (e.g. a uvicorn worker spawned
/// by the python launcher) gets re-parented before we kill, or if the host
/// dies abruptly without running its shutdown path, the grandchildren survive
/// as zombies — exactly the symptom main-022 documents.
///
/// A Job Object with <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c> solves both:
///
///   - When we explicitly close the job handle on shutdown, Windows kills
///     every process currently in the job atomically.
///   - When the host process dies (crash, kill from outside), the job's
///     reference count drops to zero and Windows kills the job's processes
///     as part of cleanup. No orphans regardless of how we exit.
///
/// We disable <c>JOB_OBJECT_LIMIT_BREAKAWAY_OK</c> so the python child cannot
/// detach itself; and we use a nested-job-friendly setup so that even if a
/// child happens to create its own job (some packagers do), our parent job
/// still owns the tree.
/// </summary>
internal sealed class ProcessJobObject : IDisposable
{
    private IntPtr _handle;
    private bool _disposed;

    public ProcessJobObject()
    {
        _handle = CreateJobObject(IntPtr.Zero, lpName: null);
        if (_handle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "CreateJobObject failed.");

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                // KILL_ON_JOB_CLOSE: when the last handle to the job closes,
                // kill every process still in it. This is what guarantees no
                // python.exe survives the host.
                LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
            },
        };

        var size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, ptr, fDeleteOld: false);
            if (!SetInformationJobObject(_handle, JobObjectExtendedLimitInformation, ptr, (uint)size))
            {
                var err = Marshal.GetLastWin32Error();
                CloseHandle(_handle);
                _handle = IntPtr.Zero;
                throw new Win32Exception(err, "SetInformationJobObject failed.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    /// <summary>
    /// Bind the given OS process handle to this job. Every descendant process
    /// the bound process ever spawns is automatically a member of the job too.
    /// Caller must invoke this <em>before</em> the child has a chance to
    /// spawn its own children, otherwise those grandchildren will be missed.
    /// </summary>
    public void AssignProcess(IntPtr processHandle)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ProcessJobObject));
        if (_handle == IntPtr.Zero) throw new InvalidOperationException("Job handle not initialised.");

        if (!AssignProcessToJobObject(_handle, processHandle))
        {
            // ERROR_ACCESS_DENIED (5) typically means the process is already
            // in another job. On Windows 8+ jobs can be nested so this is
            // usually fine, but surface a clear error to logs.
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "AssignProcessToJobObject failed.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle != IntPtr.Zero)
        {
            // Closing the handle triggers KILL_ON_JOB_CLOSE — every process
            // still in the job dies as part of this call.
            CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
    }

    // ---- P/Invoke ----

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
    private const int JobObjectExtendedLimitInformation = 9;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob,
        int JobObjectInfoClass,
        IntPtr lpJobObjectInfo,
        uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
