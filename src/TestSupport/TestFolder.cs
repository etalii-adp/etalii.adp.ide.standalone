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
    /// <summary>Where a given-up deletion is recorded, so a green run still leaves evidence.</summary>
    internal static string ReportPath { get; } = Path.Combine(Path.GetTempPath(), "EtAlii.Adp.undeleted-test-folders.log");

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
    /// </summary>
    internal static void TryDelete(string path, Func<string, bool> exists, string? caller)
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
        Report(path, last, caller);
    }

    private static void Report(string path, Exception? last, string? caller)
    {
        // The path is a GUID under %TEMP%, so it says which folder and not whose. The caller's
        // source file is filled in by the compiler, which turns "are the failures one class or
        // many?" into a question the report file can answer.
        var by = caller is null ? "unknown caller" : IoPath.GetFileName(caller);
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} gave up deleting {path}" +
                   $" after 5 attempts over 500ms ({last?.GetType().Name ?? "still present, no exception"}) from {by}";
        _failures.Enqueue(line);
        Console.Error.WriteLine("TestFolder: " + line);
        try
        {
            File.AppendAllText(ReportPath, line + Environment.NewLine);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The queue and the console still carry it; a report that cannot be written must not
            // fail the test whose folder merely survived.
        }
    }
}
