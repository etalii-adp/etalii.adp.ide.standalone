using Xunit;

namespace EtAlii.Adp.Diagram.Sparql.Tests.Parity;

/// <summary>
/// The module reproduces the frozen transcript byte for byte: the oracle every step of the
/// conversion to the DISL definition and the FBL binding is held to, frozen from the hand-written
/// code before any of it was changed (sparql-disl-fbl, Requirement 1.3).
/// </summary>
/// <remarks>
/// A difference is either a regression or an intended change, and this conversion names none by
/// default (Q2 to Q5). Only an intended change is fixed by regenerating -
/// <c>ADP_WRITE_PARITY_TRANSCRIPTS=1</c> rewrites the file before comparing - and the diff of the
/// checked-in file is then the review of that change (Requirement 1.5).
/// </remarks>
public sealed class SparqlTranscriptTests
{
    [Fact]
    public async Task TheModule_ReproducesTheCheckedInTranscript_ByteForByte()
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act.
        var generated = await SparqlTranscript.GenerateAsync(cancellationToken);
        if (Environment.GetEnvironmentVariable(TranscriptHarness.RegenerateVariable) == "1")
        {
            await File.WriteAllBytesAsync(SparqlTranscript.CheckedInPath, generated, cancellationToken);
        }

        var checkedIn = await File.ReadAllBytesAsync(SparqlTranscript.CheckedInPath, cancellationToken);

        // Assert.
        Assert.True(checkedIn.AsSpan().SequenceEqual(generated), TranscriptText.FirstDifference(checkedIn, generated));
    }

    [Fact]
    public async Task EveryScriptedStep_LeavesTheQueryByteIdentical()
    {
        // Arrange: the transcript is also the behavioural proof that nothing writes a query
        // (Requirement 1.4), so a step that changed one must not be recordable as reproduced.
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act.
        var text = TranscriptText.Decode(await SparqlTranscript.GenerateAsync(cancellationToken));

        // Assert.
        Assert.Contains("\"bodyUnchanged\": true", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"bodyUnchanged\": false", text, StringComparison.Ordinal);
    }
}
