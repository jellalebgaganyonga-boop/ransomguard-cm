namespace RansomGuard.Agent.Core.Detection.Sentinel.Etw;

/// <summary>The kernel FileIO operations SENTINEL reads (ADR-025): openings and writes.</summary>
public enum FileIoOperation
{
    /// <summary>An opening (FileIO/Create). An opening for reading never attributes (ADR-025, rule A).</summary>
    Create,

    /// <summary>A write request (FileIO/Write).</summary>
    Write,

    /// <summary>A metadata change (FileIO/SetInfo): rename, delete disposition, end of file.</summary>
    SetInfo,

    /// <summary>A delete (FileIO/Delete).</summary>
    Delete,

    /// <summary>A rename (FileIO/Rename).</summary>
    Rename,
}

/// <summary>
/// One kernel FileIO event, as the session hands it over: the operation, the file, and the PID the
/// event carries. Nothing is decided here; the attribution rules (ADR-025, section 2 and rule A)
/// belong to the commits that read these records.
/// </summary>
/// <param name="Operation">The operation.</param>
/// <param name="FileName">The file, as TraceEvent resolves it (DiskFileIO keyword: names of files opened before the session included).</param>
/// <param name="ProcessId">The PID the event carries; 4 for the system's deferred writes, never an attribution.</param>
/// <param name="TimestampUtc">When the kernel logged the event.</param>
/// <param name="CreateDisposition">For <see cref="FileIoOperation.Create"/>: the NT disposition, high byte of CreateOptions (FILE_SUPERSEDE 0, FILE_OPEN 1, FILE_CREATE 2, FILE_OPEN_IF 3, FILE_OVERWRITE 4, FILE_OVERWRITE_IF 5); 0 otherwise.</param>
/// <param name="InfoClass">For <see cref="FileIoOperation.SetInfo"/>: the FILE_INFORMATION_CLASS; 0 otherwise.</param>
public readonly record struct FileIoRecord(
    FileIoOperation Operation,
    string FileName,
    int ProcessId,
    DateTime TimestampUtc,
    uint CreateDisposition,
    int InfoClass);

/// <summary>A process start seen in the session: the (PID, creation time) pair of ADR-025, 4b.</summary>
public readonly record struct ProcessStartRecord(int ProcessId, DateTime StartUtc);
