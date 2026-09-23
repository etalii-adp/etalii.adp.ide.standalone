using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Serilog;

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
    /// <summary>
    /// Resolved AT THE CALL SITE rather than cached in a static field, which is this repository's
    /// usual shape and is unsafe for THIS class.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Measured:</b> Serilog's unset <c>Log.Logger</c> is a <c>SilentLogger</c>, and a
    /// <c>private static readonly ILogger</c> evaluated at that moment IS that silent logger — for
    /// the life of the process. Configuring <c>Log.Logger</c> afterwards changes nothing for it,
    /// while a logger resolved after configuration logs normally: same process, same sink, one line
    /// apart. <b>Type-initialisation order is FIRST-USE order</b>, so two classes in one assembly
    /// can differ with nothing visible to tell them apart.
    /// </para>
    /// <para>
    /// <b>It happened.</b> The fifth field occurrence of <c>0x80070497</c> produced NO record at
    /// all — not "no process was holding it", which is an answer, but silence. The failure's own
    /// stack placed execution inside this class's catch while its warning appeared nowhere in the
    /// run. An instrument that is wired in some runs and not others is worse than a broken one,
    /// because it works often enough to be trusted.
    /// </para>
    /// <para>
    /// <b>What this does NOT fix:</b> 92 production classes hold a logger this way and 90 still do.
    /// Any of them whose type initialiser runs before the host configures Serilog is mute for that
    /// process's life, and the only symptom is the absence of lines nobody is looking for. The
    /// tree-wide question is <b>deferred by the user, not declined</b> — the pattern remains house
    /// style, and no session should convert files on its own initiative.
    /// </para>
    /// </remarks>
    private static ILogger Logger => Log.ForContext(typeof(FileHolders));

    /// <summary>The answer when the file is held by nobody the query can see.</summary>
    public const string None = "no process was holding it when asked";

    /// <summary>
    /// How long the failing save waits for the query before carrying on WITHOUT abandoning it.
    /// Settable for guards only; production never sets it.
    /// </summary>
    internal static TimeSpan Budget { get; set; } = TimeSpan.FromSeconds(2);

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
    /// <remarks>
    /// <para>
    /// <b>AN INSTRUMENT WHOSE BUDGET EXPIRES EXACTLY WHEN THE FAILURE IS MOST LIKELY IS WEAKEST
    /// WHERE IT IS NEEDED.</b> The Restart Manager talks to a service, so it is slowest under the
    /// load that makes a publish fail - and a fixed budget therefore turned the third field
    /// occurrence into "the query did not answer within 2 seconds", which is a MISSING
    /// MEASUREMENT and not an answer. So the query is no longer abandoned when the budget
    /// expires: the save carries on at once, and the query logs its own line when it answers.
    /// </para>
    /// <para>
    /// <b>Two lines, one record.</b> The one-record rule exists so a reader is not made to
    /// assemble a story from scattered lines, not to forbid a measurement that arrives after the
    /// event it describes. The second line repeats the PATH and the PID of the first and says how
    /// long it took, so the two join without a timestamp comparison.
    /// </para>
    /// <para>
    /// <b>Slow and never are different answers, and no budget can tell them apart.</b> A second
    /// line after nine seconds says the service was slow; no second line at all says it never
    /// answered. Both are facts about the machine at the moment of a failure, and neither was
    /// recoverable while the query was being abandoned.
    /// </para>
    /// </remarks>
    public static string Describe(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "no path to ask about";
        }

        var query = Query ?? DefaultQuery;
        var clock = Stopwatch.StartNew();
        Task<string> running;
        try
        {
            // On its own thread: RmGetList talks to a service, and a save that is already failing
            // must not wait on a diagnostic.
            running = Task.Run(() => query(path));
        }
        catch (Exception exception)
        {
            return $"holders could not be determined: {exception.GetType().Name} {exception.Message}";
        }

        try
        {
            if (running.Wait(Budget))
            {
                return $"{running.Result} (answered in {clock.ElapsedMilliseconds} ms)";
            }
        }
        catch (Exception exception)
        {
            // The query itself threw inside the budget - including whatever the platform throws
            // when the Restart Manager is absent. The save's own failure is the news; this is a
            // footnote to it.
            var reason = exception is AggregateException aggregate && aggregate.InnerException is { } inner ? inner : exception;
            return $"holders could not be determined: {reason.GetType().Name} {reason.Message}";
        }

        AnswerLater(path, running, clock);
        return $"holders not yet known after {Budget.TotalSeconds:0.##}s; a later line for this path says what the query found, or none does and it never answered";
    }

    /// <summary>
    /// The second half of the record: the query kept running, and says what it found whenever it
    /// finds it. Repeats the path and the pid so a reader joins the two lines without comparing
    /// timestamps, and reports its own elapsed time so slow is distinguishable from stuck.
    /// </summary>
    private static void AnswerLater(string path, Task<string> running, Stopwatch clock) =>
        running.ContinueWith(
            finished =>
            {
                if (finished.IsFaulted)
                {
                    var reason = finished.Exception?.InnerException ?? (Exception?)finished.Exception;
                    Logger.Warning(
                        "Holders of {Path}, asked from pid {ProcessId} when a publish failed, could not be determined after {Elapsed} ms: {Reason}",
                        path,
                        Environment.ProcessId,
                        clock.ElapsedMilliseconds,
                        reason is null ? "the query faulted" : $"{reason.GetType().Name} {reason.Message}");
                    return;
                }

                Logger.Warning(
                    "Holders of {Path}, asked from pid {ProcessId} when a publish failed, answered after {Elapsed} ms: {Holders}",
                    path,
                    Environment.ProcessId,
                    clock.ElapsedMilliseconds,
                    finished.Result);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

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
