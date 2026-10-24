using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RansomGuard.Agent.Core.Detection.Sentinel.Response;

/// <summary>What a suspend or resume call came to.</summary>
public enum SuspendOutcome
{
    /// <summary>Every thread of the process is suspended (or resumed).</summary>
    Done,

    /// <summary>No process with this PID.</summary>
    NotFound,

    /// <summary>The process cannot be opened for suspension (protected process, missing right).</summary>
    AccessDenied,

    /// <summary>The call failed for another reason; the error says which.</summary>
    Failed,
}

/// <summary>Suspends and resumes a whole process (ADR-026, 2b); replaced in tests.</summary>
public interface IProcessSuspender
{
    /// <summary>Suspends every thread of the process, in one kernel call.</summary>
    SuspendOutcome Suspend(int processId, out string? error);

    /// <summary>Resumes every thread of the process.</summary>
    SuspendOutcome Resume(int processId, out string? error);
}

/// <summary>
/// NtSuspendProcess / NtResumeProcess (ntdll). One kernel call suspends every thread, including
/// one created between an enumeration and the action: SuspendThread per thread leaves that window
/// open, and a thread born during the loop keeps encrypting. Undocumented, stable since Windows XP,
/// what Process Explorer uses.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NtProcessSuspender : IProcessSuspender
{
    private const uint ProcessSuspendResume = 0x0800;
    private const int ErrorAccessDenied = 5;
    private const int ErrorInvalidParameter = 87;

    /// <inheritdoc />
    public SuspendOutcome Suspend(int processId, out string? error) => Call(processId, NtSuspendProcess, out error);

    /// <inheritdoc />
    public SuspendOutcome Resume(int processId, out string? error) => Call(processId, NtResumeProcess, out error);

    private static SuspendOutcome Call(int processId, Func<IntPtr, int> call, out string? error)
    {
        IntPtr handle = OpenProcess(ProcessSuspendResume, false, (uint)processId);
        if (handle == IntPtr.Zero)
        {
            int win32 = Marshal.GetLastWin32Error();
            error = $"{new Win32Exception(win32).Message} (0x{win32:X})";
            return win32 switch
            {
                ErrorAccessDenied => SuspendOutcome.AccessDenied,
                ErrorInvalidParameter => SuspendOutcome.NotFound,
                _ => SuspendOutcome.Failed,
            };
        }

        try
        {
            int status = call(handle);
            if (status != 0)
            {
                error = $"NTSTATUS 0x{status:X8}";
                return SuspendOutcome.Failed;
            }

            error = null;
            return SuspendOutcome.Done;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(IntPtr processHandle);
}
