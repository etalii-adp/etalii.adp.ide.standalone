using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// The test classes that install a <see cref="FileHolders"/> seam, kept off each other's
/// toes.
/// </summary>
/// <remarks>
/// <b>A global seam is shared with every test in the assembly.</b> FileHolders.Query and
/// FileHolders.Budget are static, xUnit runs CLASSES in parallel, and a second class writing
/// them was enough to make five tests fail for each other's work - a query installed here
/// answering a save started there. Asserting by path fixes the attribution question; it cannot
/// fix two classes needing DIFFERENT values of one static at the same moment. Naming a
/// collection is what serialises them, and it is cheaper than making the seam an instance the
/// production call site would have to carry.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class FileHoldersSeam
{
    public const string Name = "FileHolders seam";
}
/// <summary>
/// A holder query that outlives its budget answers late instead of being abandoned, and says how
/// long it took.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why.</b> The third field occurrence of <c>0x80070497</c> recorded
/// "holders could not be determined: the query did not answer within 2 seconds" - a MISSING
/// MEASUREMENT, not an answer, and produced by an instrument whose budget expires most easily
/// under exactly the load that makes a publish fail. An instrument weakest where it is needed is
/// worth fixing rather than reading around.
/// </para>
/// <para>
/// <b>What is pinned here:</b> a late answer arrives as its own line carrying the same path and
/// pid; a query that never answers produces NO second line, which is itself the distinction no
/// budget can make; and the save is never delayed by either.
/// </para>
/// </remarks>
[Collection(FileHoldersSeam.Name)]
public class FileHoldersAnswersLateTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly string _folder = IoPath.Combine(IoPath.GetTempPath(), "adp-holders-late-" + Guid.NewGuid().ToString("N"));

    public FileHoldersAnswersLateTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        FileHolders.Query = null;
        FileHolders.Budget = TimeSpan.FromSeconds(2);
        TestFolder.TryDelete(_folder);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AQueryThatOutlivesItsBudget_StillSaysWhatItFound()
    {
        // The case the field hit: slow, not absent. The save gets its record at once and the
        // measurement follows rather than being thrown away.
        var path = IoPath.Combine(_folder, "tea.owm");
        FileHolders.Budget = TimeSpan.FromMilliseconds(100);
        FileHolders.Query = _ =>
        {
            Thread.Sleep(TimeSpan.FromMilliseconds(400));
            return "pid 4242 someone.exe";
        };
        using var logs = LogCapture.Start();

        var described = FileHolders.Describe(path);

        // The inline half says the question is open, and does not pretend nobody was holding it.
        Assert.Contains("not yet known", described, StringComparison.Ordinal);
        Assert.DoesNotContain(FileHolders.None, described, StringComparison.Ordinal);

        // The late half arrives, naming the same path and this process, with its own elapsed time.
        var line = await EventuallyAsync(logs, warning =>
            warning.Contains(path, StringComparison.Ordinal) &&
            warning.Contains("pid 4242 someone.exe", StringComparison.Ordinal));
        Assert.Contains($"pid {Environment.ProcessId}", line, StringComparison.Ordinal);
        Assert.Contains("ms", line, StringComparison.Ordinal);
    }

    [Fact]
    public void AQueryThatAnswersInTime_SaysSoInline_WithItsElapsedTime()
    {
        // The ordinary case stays one line: nothing to join, and the elapsed time is what tells a
        // later reader whether the service was healthy at the moment of the failure.
        var path = IoPath.Combine(_folder, "tea.owm");
        FileHolders.Query = _ => "pid 7 quick.exe";

        var described = FileHolders.Describe(path);

        Assert.Contains("pid 7 quick.exe", described, StringComparison.Ordinal);
        Assert.Contains("answered in", described, StringComparison.Ordinal);
        Assert.DoesNotContain("not yet known", described, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AQueryThatNeverAnswers_LogsNoSecondLine()
    {
        // THE DISTINCTION NO BUDGET CAN MAKE. Slow produces a late line; stuck produces none, and
        // the absence is the answer. A fabricated "nobody was holding it" here would be the same
        // defect this whole instrument exists to avoid.
        var path = IoPath.Combine(_folder, "tea.owm");
        FileHolders.Budget = TimeSpan.FromMilliseconds(100);
        using var stuck = new ManualResetEventSlim(false);
        FileHolders.Query = _ =>
        {
            stuck.Wait(Patience);
            return "never gets here in this test";
        };
        using var logs = LogCapture.Start();

        var described = FileHolders.Describe(path);
        Assert.Contains("not yet known", described, StringComparison.Ordinal);

        await Task.Delay(TimeSpan.FromMilliseconds(600), TestContext.Current.CancellationToken);

        Assert.DoesNotContain(logs.Warnings, warning => warning.Contains(path, StringComparison.Ordinal));
        stuck.Set();
    }

    [Fact]
    public async Task AQueryThatFailsLate_ReportsTheFailureRatherThanAnAnswer()
    {
        // A late failure is still a measurement: it says the service was reachable enough to
        // refuse, which "no second line" does not.
        var path = IoPath.Combine(_folder, "tea.owm");
        FileHolders.Budget = TimeSpan.FromMilliseconds(100);
        FileHolders.Query = _ =>
        {
            Thread.Sleep(TimeSpan.FromMilliseconds(300));
            throw new InvalidOperationException("the Restart Manager refused");
        };
        using var logs = LogCapture.Start();

        FileHolders.Describe(path);

        var line = await EventuallyAsync(logs, warning =>
            warning.Contains(path, StringComparison.Ordinal) &&
            warning.Contains("could not be determined", StringComparison.Ordinal));
        Assert.Contains("the Restart Manager refused", line, StringComparison.Ordinal);
    }

    [Fact]
    public void ASlowQuery_DoesNotHoldTheCallerOpen()
    {
        // The property the budget still buys, now that expiring it costs no measurement.
        var path = IoPath.Combine(_folder, "tea.owm");
        FileHolders.Budget = TimeSpan.FromMilliseconds(100);
        using var stuck = new ManualResetEventSlim(false);
        FileHolders.Query = _ =>
        {
            stuck.Wait(Patience);
            return "never";
        };

        var clock = System.Diagnostics.Stopwatch.StartNew();
        FileHolders.Describe(path);
        clock.Stop();

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"Describe held its caller for {clock.Elapsed.TotalSeconds:0.0}s against a 100ms budget.");
        stuck.Set();
    }

    private static async Task<string> EventuallyAsync(LogCapture logs, Func<string, bool> matches)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < deadline)
        {
            var found = logs.Warnings.FirstOrDefault(matches);
            if (found is not null)
            {
                return found;
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        Assert.Fail("No matching line arrived within the patience window.");
        return "";
    }
}
