using System.Collections.Concurrent;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Editor.Plain.Tests;

/// <summary>
/// The refusal record reaches a Serilog pipeline installed AFTER this class was first used, which is
/// the ordinary case in a host.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this class's silence mattered.</b> Its warning is the only record that a re-read was
/// refused and a notification therefore discarded. Written through a cached
/// <c>private static readonly ILogger</c>, that warning may never be emitted at all: Serilog's unset
/// <c>Log.Logger</c> is a <c>SilentLogger</c>, a static field evaluated at that moment IS that
/// logger for the life of the process, and type initialisation runs in first-use order. During the
/// deadline-flake investigation "no evidence of a drop" and "no channel for evidence of a drop" were
/// indistinguishable for two days, and this is the channel.
/// </para>
/// <para>
/// <b>This assembly cannot reproduce the field condition, and the same is true one project over.</b>
/// <c>LogCapture</c> installs a pipeline from a module initializer before anything else runs,
/// precisely so no static logger field ever binds to the silent logger - so the production defect
/// cannot occur here by construction. What this pins is the property the fix buys: a logger resolved
/// at the call site follows a pipeline installed later, and a cached one does not.
/// <c>AdpFileWriterSpeaksAfterLateConfigurationTests</c> says the same thing about the writer and is
/// the pattern this follows.
/// </para>
/// <para>
/// <b>Seen to fail:</b> reverting the property to
/// <c>private static readonly ILogger _logger = Log.ForContext&lt;PlainEditorSession&gt;()</c> reddens
/// this and nothing else - the cached field keeps writing to the pipeline of its first use, where
/// this sink is not.
/// </para>
/// <para>
/// <b>AND THE FIRST VERSION OF THIS TEST PASSED AGAINST THAT REVERT, which is the trap worth
/// carrying.</b> Its arrange merely CONSTRUCTED a session before replacing the pipeline - and
/// constructing one touches no static field. C# <c>beforefieldinit</c> defers a static field
/// initialiser to the first ACCESS of that field, so a cached logger did not bind until the first
/// warning, which happens after the replacement: the test would have certified the very shape it
/// exists to reject. Forcing a refusal in the arrange is what makes the binding happen on the old
/// pipeline. <b>Using a class is not the same as making it log, and only the second binds a cached
/// logger.</b>
/// </para>
/// <para>
/// <b>Which makes the 90 classes still holding the cached shape harder to guard than anyone has
/// assumed.</b> Any future sweep over that pattern has this same trap: a test that exercises a class
/// without making it emit will find its logger unbound, follow the replacement, and report health -
/// so <b>a sweep's green would be worthless for exactly the reason this test's first green was.</b>
/// Each subject has to be made to LOG before the pipeline moves, which is per-class work rather than
/// something a loop can do. Worth knowing before the deferred conversion is picked up, because the
/// cheap version of that guard cannot work.
/// </para>
/// <para>
/// <b>And the warm-up was timed rather than ordered, which made this flaky in both directions.</b>
/// It slept 300 ms and hoped its refusal had been logged. Under a loaded gate it had not: the
/// warm-up's refusal about <c>warm-up.txt</c> landed AFTER the late pipeline was installed, the wait
/// loop - which stopped on any "no longer opens" line - took it for the one it was waiting for, and
/// the <c>notes.txt</c> assertion failed (Developer 2's task 18 gate, 2026-09-25; Developer 2's
/// diagnosis). The same late arrival had a quieter cost: a warm-up that logs after the swap binds a
/// cached logger to the NEW pipeline, so on exactly those runs this test could not have caught the
/// shape it exists to reject. And two separate assertions - one line saying "no longer opens",
/// any line saying <c>notes.txt</c> - could be satisfied by two different lines. So the warm-up now
/// logs to a pipeline of its own and the test WAITS until that pipeline has heard it before the late
/// one exists, and the wait and the assertion both ask for ONE line naming <c>notes.txt</c>. The
/// warm-up refusal stays: it is load-bearing, as the paragraph above says (Architect 3, who wrote
/// this test and its warm-up).
/// </para>
/// </remarks>
public class PlainEditorSessionSpeaksAfterLateConfigurationTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly string _folder = IoPath.Combine(IoPath.GetTempPath(), "adp-plain-late-" + Guid.NewGuid().ToString("N"));
    private readonly ILogger _original = Log.Logger;

    public PlainEditorSessionSpeaksAfterLateConfigurationTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        // Restored, never disposed: the original pipeline is the assembly's one substrate and it
        // outlives every test in it.
        Log.Logger = _original;
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder a virus scanner still holds is not a test failure.
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ARefusedReReadIsRecorded_OnAPipelineInstalledAfterTheSessionWasFirstUsed()
    {
        // Arrange. THE CLASS MUST BE MADE TO LOG BEFORE THE PIPELINE CHANGES, not merely used.
        // Constructing a session touches no static field, and C# beforefieldinit defers a static
        // field initialiser to the first ACCESS of that field - so a cached logger would not bind
        // until the first warning, which is after the replacement, and the test would pass against
        // the very shape it exists to reject. Forcing a refusal here is what makes the binding
        // happen on the old pipeline. (Found by reverting the property and watching this pass.)
        //
        // ORDERED, NOT TIMED. The warm-up logs to a pipeline of its own - the host's bootstrap one -
        // and the test waits until that pipeline has HEARD the refusal before the late pipeline is
        // installed. A sleep only hoped for that, and a warm-up logging after the swap both confused
        // the wait below and bound a cached logger to the new pipeline, where it would pass.
        var early = ConfigureSerilog();
        var warmUp = IoPath.Combine(_folder, "warm-up.txt");
        await File.WriteAllTextAsync(warmUp, "before", TestContext.Current.CancellationToken);
        await using (new PlainEditorSession(warmUp))
        {
            File.Delete(warmUp);
            await UntilRefusalOf(early, "warm-up.txt");
        }

        Assert.True(
            early.Any(line => IsRefusalOf(line, "warm-up.txt")),
            "The arrangement failed: the warm-up's refusal never reached the first pipeline, so nothing was bound to it before the pipeline moved.");

        var path = IoPath.Combine(_folder, "notes.txt");
        await File.WriteAllTextAsync(path, "before", TestContext.Current.CancellationToken);
        await using var session = new PlainEditorSession(path);
        Assert.Equal("before", session.Content);

        // Only then is a new pipeline installed - a host that opens a file before it builds logging.
        var heard = ConfigureSerilog();

        // Act. The file goes away for good, so the retry runs out and the refusal is recorded. This
        // also exercises the Deleted subscription: without it nothing would even be notified.
        File.Delete(path);

        // Assert. The record arrived HERE, on the new pipeline. A cached logger would still be
        // writing to the old one, and nothing else about the run would look different - which is
        // exactly how the absence of this line was read as the absence of the event. ONE line
        // saying both: the warm-up may still add a late refusal of its own, and a "no longer opens"
        // about another file is not this one.
        await UntilRefusalOf(heard, "notes.txt");

        Assert.Contains(heard, line => IsRefusalOf(line, "notes.txt"));
    }

    private static bool IsRefusalOf(string line, string fileName) =>
        line.Contains("no longer opens", StringComparison.Ordinal)
        && line.Contains(fileName, StringComparison.Ordinal);

    private static async Task UntilRefusalOf(ConcurrentQueue<string> heard, string fileName)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < deadline && !heard.Any(line => IsRefusalOf(line, fileName)))
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Installs a pipeline as a host would at startup, WITHOUT taking the substrate away from the
    /// captures already open in other tests - a bare replacement starves every capture in the
    /// assembly, which cost a gate once already and is recorded on the writer's equivalent.
    /// </summary>
    private static ConcurrentQueue<string> ConfigureSerilog()
    {
        var heard = new ConcurrentQueue<string>();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(new LogCaptureCaptureSink())
            .WriteTo.Sink(new QueueSink(heard))
            .CreateLogger();
        return heard;
    }

    private sealed class QueueSink(ConcurrentQueue<string> heard) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => heard.Enqueue(logEvent.RenderMessage());
    }
}
