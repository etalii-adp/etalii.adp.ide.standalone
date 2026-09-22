using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using IoPath = System.IO.Path;

namespace EtAlii.Adp;

/// <summary>
/// Deletes a test's scratch folder, tolerating the short window in which Windows still
/// holds handles onto files inside it - a just-disposed FileSystemWatcher whose handle
/// the OS releases asynchronously, a stream closed a moment ago, an antivirus scan.
/// Retries briefly, then gives up - <b>and says so</b>: an orphaned folder under %TEMP% is
/// still preferable to a red suite whose tests all passed, but a failure nobody is told about
/// is a fact the system knew and chose not to say. Compiled into every *.Tests project via
/// src/Directory.Build.targets.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the silence had to end.</b> 3,694 folders accumulated under
/// <c>%TEMP%/EtAlii.Adp.IntegrationTests/</c> without one line of evidence anywhere, and two
/// separate sessions spent a day inferring the cause from folder contents and modification
/// times. The measurement that settled it - <c>Directory.Exists</c> still true on the way out of
/// this method - is something this method knew all along. Giving up is a decision; giving up
/// quietly is a missing instrument.
/// </para>
/// <para>
/// <b>Why a file and not only the console.</b> Measured on 2026-09-12: the test platform buffers
/// console output per test and prints it only for tests that FAIL, so a report written only to
/// the console is invisible on exactly the green runs that leave the folders behind. The file
/// survives that, and {@link Failures} lets a test assert the report rather than trusting it.
/// </para>
/// </remarks>
internal static class TestFolder
{
    /// <summary>
    /// The environment variable a gate sets to collect this run's reports in a directory of its
    /// own. Unset, a local run still reports to one file under %TEMP%.
    /// </summary>
    internal const string DirectoryVariable = "ADP_UNDELETED_FOLDERS_DIR";

    /// <summary>Where a given-up deletion is recorded, so a green run still leaves evidence.</summary>
    internal static string ReportPath =>
        ReportFileFor(Environment.GetEnvironmentVariable(DirectoryVariable), Environment.ProcessId);

    /// <summary>
    /// The report file for a directory and a process. <b>One file per test process</b> when a
    /// directory is named: a single shared file mixed every session's runs together, so a gate
    /// keeping it would have kept other runs' entries beside its own. Every test assembly runs in
    /// its own process, so the process id keeps them apart. Unset or blank, the one %TEMP% file a
    /// local run has always used.
    /// </summary>
    internal static string ReportFileFor(string? directory, int processId) =>
        string.IsNullOrWhiteSpace(directory)
            ? Path.Combine(Path.GetTempPath(), "EtAlii.Adp.undeleted-test-folders.log")
            : Path.Combine(directory, $"{processId}.log");

    private static readonly ConcurrentQueue<string> _failures = new();

    /// <summary>
    /// Every folder this assembly's tests could not delete, newest last. Assertable, which is
    /// what makes the report a behaviour rather than a hope.
    /// </summary>
    internal static IReadOnlyCollection<string> Failures => _failures;

    public static void TryDelete(string path, [CallerFilePath] string? caller = null) =>
        TryDelete(path, Directory.Exists, caller);

    /// <summary>
    /// The loop, with the existence check handed in. <b>The seam exists because the case that
    /// matters cannot be provoked reliably:</b> a delete that returns without throwing and leaves
    /// the directory happens 14 times in 870 real teardowns, and a guard that recreated the
    /// folder from another thread to force it passed 3 runs in 5 - a flaky guard teaches people
    /// that red means noise. Handing in <c>exists</c> lets a test put the loop in exactly that
    /// state, deterministically, through the same code the suite runs.
    /// <para>
    /// <paramref name="reportTarget"/> is where such a deliberately provoked failure is reported.
    /// The guards pass a file inside their own scratch folder, so the failures they cause on
    /// purpose never reach the real report: before they did, every green run added two lines to
    /// it, and a report that grows on every passing run is the noise that hides a real entry.
    /// Found by Architect 1 counting report lines against leftover folders across three gates.
    /// </para>
    /// </summary>
    internal static void TryDelete(string path, Func<string, bool> exists, string? caller, string? reportTarget = null)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (attempt > 0)
            {
                Thread.Sleep(50 * attempt);
            }
            try
            {
                if (!exists(path))
                {
                    return;
                }
                Directory.Delete(path, recursive: true);

                // A DELETE THAT DID NOT THROW IS NOT A DELETE THAT HAPPENED. Measured across one
                // run: 870 teardowns, 856 folders gone, and 14 where this call returned with no
                // exception and the directory was still there - which is exactly that run's
                // leftover count. A file written between the recursive delete's enumeration and
                // its removal of the root leaves the root behind, and .NET does not call that an
                // error. Checking the outcome rather than the absence of a throw is what makes
                // those 14 visible; the first version of this reporting trusted the non-throw and
                // therefore stayed silent through all of them.
                if (!exists(path))
                {
                    return;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Still held; wait a little longer and try again.
                last = e;
            }
        }

        // Still there after five attempts over 500ms. The suite stays green - that decision has
        // not changed - but the fact is now recorded rather than swallowed.
        Report(path, last, caller, reportTarget ?? ReportPath);
    }

    private static void Report(string path, Exception? last, string? caller, string target)
    {
        // The path is a GUID under %TEMP%, so it says which folder and not whose. The caller's
        // source file is filled in by the compiler, which turns "are the failures one class or
        // many?" into a question the report file can answer.
        var by = caller is null ? "unknown caller" : IoPath.GetFileName(caller);
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} gave up deleting {path}" +
                   $" after 5 attempts over 500ms ({last?.GetType().Name ?? "still present, no exception"}) from {by}";
        _failures.Enqueue(line);
        Console.Error.WriteLine("TestFolder: " + line);
        Append(target, line);
    }

    private static readonly Lock _reportGate = new();

    /// <summary>Writes one report line to <paramref name="target"/>, creating its directory if needed.</summary>
    /// <remarks>
    /// <b>One writer at a time within the process.</b> Unlocked, 32 threads appending 3200 lines lost
    /// 1031, 1175 and 2668 of them across three runs: the appends contend for the handle, the loser
    /// throws, and the catch below - there so a report can never fail a test - swallowed every one.
    /// The file per process keeps processes apart; this lock is what keeps a process's own parallel
    /// test classes from losing each other's lines. A write refused for a reason the lock cannot
    /// prevent - a full disk, a scanner holding the file - is still swallowed, and still rare.
    /// </remarks>
    internal static void Append(string target, string line)
    {
        lock (_reportGate)
        {
            try
            {
                var directory = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.AppendAllText(target, line + Environment.NewLine);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The queue and the console still carry it; a report that cannot be written must
                // not fail the test whose folder merely survived.
            }
        }
    }
}
