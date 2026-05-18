using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace RansomGuard.Agent.Core.Detection.Genealogy;

/// <summary>
/// Captures process information using System.Diagnostics.Process and WMI
/// for data not available through the standard API (command line, parent PID).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessSnapshotService : IProcessSnapshotService
{
    private readonly ILogger<ProcessSnapshotService> _logger;

    /// <summary>
    /// Initializes the process snapshot service.
    /// </summary>
    public ProcessSnapshotService(ILogger<ProcessSnapshotService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public ProcessSnapshot? CaptureProcess(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            (int ppid, string? cmdLine) = GetWmiInfo(pid);

            return new ProcessSnapshot
            {
                ProcessId = pid,
                ParentProcessId = ppid,
                ProcessName = process.ProcessName,
                ExecutablePath = TryGetPath(process),
                CommandLine = cmdLine,
                StartTime = TryGetStartTime(process),
                WorkingSetBytes = process.WorkingSet64
            };
        }
        catch (ArgumentException)
        {
            // Process has exited
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cannot capture process {PID}", pid);
            return null;
        }
    }

    /// <inheritdoc />
    public ProcessTree? BuildProcessTree(int pid, int maxDepth = 10)
    {
        ProcessSnapshot? root = CaptureProcess(pid);
        if (root is null) return null;

        var ancestors = new List<ProcessSnapshot>();
        int currentPpid = root.ParentProcessId;
        int depth = 0;

        while (currentPpid > 0 && depth < maxDepth)
        {
            ProcessSnapshot? parent = CaptureProcess(currentPpid);
            if (parent is null) break;

            ancestors.Add(parent);
            currentPpid = parent.ParentProcessId;
            depth++;
        }

        return new ProcessTree
        {
            Root = root,
            Ancestors = ancestors
        };
    }

    private static (int ParentPid, string? CommandLine) GetWmiInfo(int pid)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT ParentProcessId, CommandLine FROM Win32_Process WHERE ProcessId = {pid}");

            foreach (ManagementObject obj in searcher.Get())
            {
                int ppid = Convert.ToInt32(obj["ParentProcessId"]);
                string? cmdLine = obj["CommandLine"]?.ToString();
                return (ppid, cmdLine);
            }
        }
        catch
        {
            // WMI query failed — access denied or WMI service issue
        }

        return (0, null);
    }

    private static string? TryGetPath(Process process)
    {
        try { return process.MainModule?.FileName; }
        catch { return null; }
    }

    private static DateTime? TryGetStartTime(Process process)
    {
        try { return process.StartTime.ToUniversalTime(); }
        catch { return null; }
    }
}
