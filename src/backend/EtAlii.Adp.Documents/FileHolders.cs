using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace EtAlii.Adp.Documents;

/// <summary>
/// Names the processes holding a file open, for a failure record to say WHO the other party was.
/// </summary>
/// <remarks>
/// <para>
/// A publish that fails on a contended file records an HResult, which says what happened but never
/// who did it. Measured against a live holder: two concurrent <c>File.Replace</c> calls on one
/// destination produce <c>0x80070497</c>, a concurrent delete-and-recreate produces it far more
/// often still, and a reader sharing only <c>Read</c> produces <c>0x80070020</c> - so the code
/// narrows the SHAPE of the other party and never its identity. Windows' Restart Manager answers
/// that question directly, and it is the same list the installer UI shows as "close these apps".
/// </para>
/// <para>
/// <b>What this cannot name, stated because a diagnostic that seems to say more than it does is
/// worse than none.</b> Restart Manager lists processes holding a handle AT THE MOMENT IT IS
/// ASKED. An actor that has already finished holds nothing: a completed delete, a replace that has
/// returned, a reader that has closed. That is precisely the dominant producer of
/// <c>0x80070497</c>, so <see cref="None"/> is the expected answer there and must not be read as
/// "nobody was involved". It also cannot see a holder on another machine over a share, and it
/// names a process, never the call inside it.
/// </para>
/// <para>
/// <b>It may not change what a save does.</b> Every failure here - unavailable, refused, slow,
/// throwing - answers with a description and nothing else. The caller logs that beside the
/// exception it was already going to throw.
/// </para>
/// </remarks>
public static class FileHolders
{
    /// <summary>The answer when the file is held by nobody the query can see.</summary>
    public const string None = "no process was holding it when asked";

    /// <summary>How long the query may take before it is abandoned and the save carries on.</summary>
    private static readonly TimeSpan _budget = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The query itself, swappable so a guard can drive the unavailable-and-slow cases. Production
    /// never sets it; a test that does restores it.
    /// </summary>
    internal static Func<string, string>? Query { get; set; }

    /// <summary>
    /// The processes holding <paramref name="path"/>, as one line for a log record. Never throws,
    /// never blocks longer than its budget, and never reports failure as absence: a query that
    /// could not run says so in its own words.
    /// </summary>
    public static string Describe(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "no path to ask about";
        }

        var query = Query ?? DefaultQuery;
        try
        {
            // On its own thread with a budget: RmGetList talks to a service, and a save that is
            // already failing must not wait on a diagnostic. An abandoned task is left to finish
            // on its own - it holds nothing of ours.
            var running = Task.Run(() => query(path));
            return running.Wait(_budget)
                ? running.Result
                : $"holders could not be determined: the query did not answer within {_budget.TotalSeconds:0} seconds";
        }
        catch (Exception exception)
        {
            // Including whatever the platform throws when the Restart Manager is absent. The save's
            // own failure is the news; this line is a footnote to it.
            return $"holders could not be determined: {exception.GetType().Name} {exception.Message}";
        }
    }

    private static string DefaultQuery(string path) =>
        OperatingSystem.IsWindows()
            ? WindowsHolders(path)
            : "holders could not be determined: only Windows is asked";

    [SupportedOSPlatform("windows")]
    private static string WindowsHolders(string path)
    {
        var key = new string('\0', CchSessionKey + 1);
        var started = RmStartSession(out var session, 0, key);
        if (started != 0)
        {
            return $"holders could not be determined: RmStartSession returned {started}";
        }

        try
        {
            var registered = RmRegisterResources(session, 1, [path], 0, null, 0, null);
            if (registered != 0)
            {
                return $"holders could not be determined: RmRegisterResources returned {registered}";
            }

            uint count = 0;

            // Asked twice on purpose: the first call says how many entries there are, the second
            // fills a buffer of exactly that size. ERROR_MORE_DATA from the first is the ordinary
            // path, not a failure.
            var listed = RmGetList(session, out var needed, ref count, null, out var reasons);
            if (listed != ErrorMoreData && listed != 0)
            {
                return $"holders could not be determined: RmGetList returned {listed}";
            }

            if (needed == 0)
            {
                return None;
            }

            var processes = new RmProcessInfo[needed];
            count = needed;
            listed = RmGetList(session, out needed, ref count, processes, out reasons);
            if (listed != 0)
            {
                return $"holders could not be determined: RmGetList returned {listed}";
            }

            if (count == 0)
            {
                return None;
            }

            var self = Environment.ProcessId;
            var described = processes
                .Take((int)count)
                .Select(process =>
                {
                    var id = (int)process.Process.ProcessId;
                    var name = string.IsNullOrWhiteSpace(process.AppName) ? "?" : process.AppName;
                    return id == self ? $"pid {id} {name} (THIS process)" : $"pid {id} {name}";
                });

            return string.Join(", ", described);
        }
        finally
        {
            RmEndSession(session);
        }
    }

    private const int CchSessionKey = 32;
    private const int ErrorMoreData = 234;

    [StructLayout(LayoutKind.Sequential)]
    private struct RmUniqueProcess
    {
        public uint ProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RmProcessInfo
    {
        public RmUniqueProcess Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string AppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string ServiceShortName;

        public int ApplicationType;
        public uint AppStatus;
        public uint TerminalServicesSessionId;

        [MarshalAs(UnmanagedType.Bool)]
        public bool Restartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint sessionHandle, int sessionFlags, string sessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint sessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint sessionHandle,
        uint files,
        string[]? fileNames,
        uint applications,
        object[]? applicationNames,
        uint services,
        string[]? serviceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(
        uint sessionHandle,
        out uint procInfoNeeded,
        ref uint procInfo,
        [In, Out] RmProcessInfo[]? processInfo,
        out uint rebootReasons);
}
