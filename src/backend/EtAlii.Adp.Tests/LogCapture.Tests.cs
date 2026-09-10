using Serilog;
using Xunit;

namespace EtAlii.Adp.Tests;

/// <summary>
/// What a <see cref="LogCapture"/> collects - and, because every test project compiles it in,
/// what it must not: the events some other test caused.
/// </summary>
/// <remarks>
/// Deliberately NOT in <see cref="LogCapture.Collection"/>. The collection serialises its
/// members against each other and does nothing about a test outside it, so a guard that only
/// holds inside the collection would prove the wrong thing. This class runs alongside every
/// other test in the assembly, which is the condition the flake needed.
/// </remarks>
public class LogCaptureTests
{
    [Fact]
    public void AnEventLoggedOnAFlowThisTestDidNotStart_IsNotCaptured()
    {
        // Arrange.
        // Found as a flake: DatabricksDocumentStoreMissingBodyTests asserts no warnings, and
        // failed once in a full run because another test class - one of eighteen in that
        // assembly that never joined the collection - warned while its capture was open.
        // Waiting for the timing to line up is not a guard, so the other test is modelled
        // here deterministically: a thread that does not inherit this test's execution
        // context, which is exactly what every other test's work looks like from here.
        using var capture = LogCapture.Start();
        var sentinel = Guid.NewGuid().ToString("N");
        var foreign = new Thread(() =>
            Log.ForContext("SourceContext", "Somebody.Else.Entirely").Warning("Another test's warning {Sentinel}", sentinel));

        // Act.
        using (ExecutionContext.SuppressFlow())
        {
            foreign.Start();
        }
        foreign.Join();

        // Assert.
        Assert.DoesNotContain(capture.Warnings, warning => warning.Contains(sentinel, StringComparison.Ordinal));
    }

    [Fact]
    public void AnotherTestStartingACapture_DoesNotOrphanThisOne()
    {
        // Arrange.
        // The second face of the same single slot, and the worse one. Found while writing this
        // file: a capture started in a constructor came back EMPTY in a full run and passed 3
        // of 3 alone, because a concurrent Start() replaced the one active capture rather than
        // joining it - so the first stopped receiving its own events. That breaks presence
        // assertions, not just absence ones. Again modelled deterministically: the other test's
        // Start() runs on a thread that does not carry this test's context.
        using var mine = LogCapture.Start();
        var sentinel = Guid.NewGuid().ToString("N");
        LogCapture? theirs = null;
        var other = new Thread(() => theirs = LogCapture.Start());
        using (ExecutionContext.SuppressFlow())
        {
            other.Start();
        }
        other.Join();

        try
        {
            // Act.
            Log.ForContext("SourceContext", "The.Subject").Warning("Logged after the other capture started {Sentinel}", sentinel);

            // Assert.
            Assert.Contains(mine.Warnings, warning => warning.Contains(sentinel, StringComparison.Ordinal));
        }
        finally
        {
            theirs?.Dispose();
        }
    }

    [Fact]
    public async Task AnEventACollaboratorLogsOnThisTestsFlow_IsStillCaptured()
    {
        // Arrange.
        // The floor, and the one that rules out the tempting fix. A subject rarely logs
        // everything itself - it calls a reader, a writer, a router, each logging under its
        // OWN source context, and often awaits work on the thread pool. Filtering a capture by
        // the subject's source context would drop exactly these, and an Assert.Empty over the
        // warnings would then pass because it could no longer see them: a loud flake traded
        // for a silent hole.
        using var capture = LogCapture.Start();
        var sentinel = Guid.NewGuid().ToString("N");

        // Act.
        Log.ForContext("SourceContext", "A.Collaborator").Warning("On the test's own thread {Sentinel}", sentinel);
        await Task.Run(() => Log.ForContext("SourceContext", "Another.Collaborator").Warning("On a pooled thread the test awaited {Sentinel}", sentinel), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(2, capture.Warnings.Count(warning => warning.Contains(sentinel, StringComparison.Ordinal)));
    }
}
