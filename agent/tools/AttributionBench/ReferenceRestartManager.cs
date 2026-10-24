using System.Runtime.InteropServices;

// Instrument check 1 (AGT-BENCH-001): a Restart Manager query written independently of the
// production RestartManagerHelper, by the book -- RmStartSession, RmRegisterResources, RmGetList
// twice (ERROR_MORE_DATA sizes the array, then it is filled), RmEndSession. Compared with the
// production call on every event: if only this one finds a holder, the production wrapper is
// blind where Windows is not, and the bench's 0 % would be a defect of the wrapper.

/// <summary>The holders the Restart Manager reports for one file, or the API error that prevented it.</summary>
internal sealed record ReferenceResult(IReadOnlyList<int> Pids, int Error)
{
    public bool Failed => Error != 0;
}

internal static class ReferenceRestartManager
{
    private const int ErrorSuccess = 0;
    private const int ErrorMoreData = 234;
    private const int CchRmSessionKey = 32; // CCH_RM_SESSION_KEY: sizeof(GUID) * 2
    private const int CchRmMaxAppName = 255;
    private const int CchRmMaxSvcName = 63;

    public static ReferenceResult Holders(string path)
    {
        var key = new char[CchRmSessionKey + 1];
        int error = RmStartSession(out uint session, 0, key);
        if (error != ErrorSuccess)
        {
            return new ReferenceResult([], error);
        }

        try
        {
            error = RmRegisterResources(session, 1, [path], 0, IntPtr.Zero, 0, null);
            if (error != ErrorSuccess)
            {
                return new ReferenceResult([], error);
            }

            // First call: no array. ERROR_MORE_DATA and the size needed, or success and nobody.
            uint needed = 0;
            uint count = 0;
            uint reasons = 0;
            error = RmGetList(session, out needed, ref count, null, ref reasons);
            if (error == ErrorSuccess)
            {
                return new ReferenceResult([], 0);
            }

            if (error != ErrorMoreData)
            {
                return new ReferenceResult([], error);
            }

            // Second call: the array, sized from the first. Holders can come and go between the
            // two calls: ERROR_MORE_DATA again means "grew", retried once with the new size.
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var infos = new RmProcessInfo[needed];
                count = needed;
                error = RmGetList(session, out needed, ref count, infos, ref reasons);
                if (error == ErrorSuccess)
                {
                    return new ReferenceResult(infos.Take((int)count).Select(i => i.Process.ProcessId).ToList(), 0);
                }

                if (error != ErrorMoreData)
                {
                    return new ReferenceResult([], error);
                }
            }

            return new ReferenceResult([], error);
        }
        finally
        {
            RmEndSession(session);
        }
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint sessionHandle, int sessionFlags, [Out] char[] sessionKey);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint sessionHandle,
        uint fileCount,
        string[] fileNames,
        uint applicationCount,
        IntPtr applications,
        uint serviceCount,
        string[]? serviceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(
        uint sessionHandle,
        out uint procInfoNeeded,
        ref uint procInfo,
        [In, Out] RmProcessInfo[]? affectedApps,
        ref uint rebootReasons);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint sessionHandle);

    [StructLayout(LayoutKind.Sequential)]
    private struct RmUniqueProcess
    {
        public int ProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RmProcessInfo
    {
        public RmUniqueProcess Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxAppName + 1)]
        public string AppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxSvcName + 1)]
        public string ServiceShortName;

        public int ApplicationType;
        public uint AppStatus;
        public uint TsSessionId;

        [MarshalAs(UnmanagedType.Bool)]
        public bool Restartable;
    }
}
