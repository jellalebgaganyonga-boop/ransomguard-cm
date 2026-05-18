using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace RansomGuard.Agent.Core.Detection.Sentinel;

/// <summary>
/// Wraps the Windows Restart Manager API to identify processes that hold locks
/// on specific files. Used for canary alert process attribution.
/// Thread-safe: a lock protects the session lifecycle.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RestartManagerHelper
{
    private readonly ILogger<RestartManagerHelper> _logger;
    private readonly object _sessionLock = new();

    // Restart Manager P/Invoke declarations
    private const int RmRebootReasonNone = 0;
    private const int ErrorSuccess = 0;
    private const int CchRmMaxAppName = 255;
    private const int CchRmMaxSvcName = 63;

    /// <summary>
    /// Initializes the Restart Manager helper.
    /// </summary>
    public RestartManagerHelper(ILogger<RestartManagerHelper> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the list of processes that hold locks on the specified file.
    /// Returns an empty list if no processes are found or if the API call fails.
    /// </summary>
    /// <param name="filePath">Full path to the file to query.</param>
    /// <returns>List of processes locking the file.</returns>
    public IReadOnlyList<ProcessAttribution> GetProcessesLockingFile(string filePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var results = new List<ProcessAttribution>();

        lock (_sessionLock)
        {
            int res = RmStartSession(out uint sessionHandle, 0, Guid.NewGuid().ToString());
            if (res != ErrorSuccess)
            {
                _logger.LogDebug("RmStartSession failed with error {ErrorCode}", res);
                return results;
            }

            try
            {
                string[] resources = [filePath];
                res = RmRegisterResources(sessionHandle, (uint)resources.Length, resources, 0, null, 0, null);
                if (res != ErrorSuccess)
                {
                    _logger.LogDebug("RmRegisterResources failed with error {ErrorCode}", res);
                    return results;
                }

                uint needed = 0;
                uint rebootReason = RmRebootReasonNone;

                // First call to get the count
                res = RmGetList(sessionHandle, out needed, ref needed, null, ref rebootReason);

                if (needed == 0)
                {
                    return results;
                }

                var processInfo = new RmProcessInfo[needed];
                uint count = needed;

                res = RmGetList(sessionHandle, out needed, ref count, processInfo, ref rebootReason);
                if (res != ErrorSuccess)
                {
                    _logger.LogDebug("RmGetList failed with error {ErrorCode}", res);
                    return results;
                }

                for (int i = 0; i < count; i++)
                {
                    int pid = processInfo[i].Process.dwProcessId;
                    string appName = processInfo[i].strAppName;

                    string? execPath = null;
                    DateTime? startTime = null;

                    try
                    {
                        using Process process = Process.GetProcessById(pid);
                        execPath = process.MainModule?.FileName;
                        startTime = process.StartTime.ToUniversalTime();
                    }
                    catch
                    {
                        // Process may have exited or access denied
                    }

                    results.Add(new ProcessAttribution
                    {
                        ProcessId = pid,
                        ProcessName = appName,
                        ExecutablePath = execPath,
                        StartTime = startTime
                    });
                }
            }
            finally
            {
                RmEndSession(sessionHandle);
            }
        }

        return results;
    }

    // P/Invoke declarations for Restart Manager
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint pSessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint pSessionHandle,
        uint nFiles, string[]? rgsFileNames,
        uint nApplications, RmUniqueProcess[]? rgApplications,
        uint nServices, string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(
        uint dwSessionHandle,
        out uint pnProcInfoNeeded,
        ref uint pnProcInfo,
        [In, Out] RmProcessInfo[]? rgAffectedApps,
        ref uint lpdwRebootReasons);

    [StructLayout(LayoutKind.Sequential)]
    private struct RmUniqueProcess
    {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RmProcessInfo
    {
        public RmUniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxAppName + 1)]
        public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxSvcName + 1)]
        public string strServiceShortName;
        public int ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }
}
