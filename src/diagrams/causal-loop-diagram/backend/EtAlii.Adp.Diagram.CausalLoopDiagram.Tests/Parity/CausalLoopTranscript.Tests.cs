using Xunit;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests.Parity;

/// <summary>
/// Today's code reproduces the frozen transcript byte for byte: the oracle every step of the
/// conversion to the DISL definition and the FBL binding is held to (causal-loop-disl-fbl
/// Requirement 1.3).
/// </summary>
/// <remarks>
/// A difference is either a regression or an intended change. Only the second is fixed by
/// regenerating - <c>ADP_WRITE_PARITY_TRANSCRIPTS=1</c> rewrites the file before comparing - and the
/// diff of the checked-in file is then the review of that change.
/// </remarks>
public sealed class CausalLoopTranscriptTests
{
    [Fact]
    public async Task TodaysCode_ReproducesTheCheckedInTranscript_ByteForByte()
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act.
        var generated = await CausalLoopTranscript.GenerateAsync(cancellationToken);
        if (Environment.GetEnvironmentVariable("ADP_WRITE_PARITY_TRANSCRIPTS") == "1")
        {
            await File.WriteAllBytesAsync(CausalLoopTranscript.CheckedInPath, generated, cancellationToken);
        }

        var checkedIn = await File.ReadAllBytesAsync(CausalLoopTranscript.CheckedInPath, cancellationToken);

        // Assert.
        Assert.True(checkedIn.AsSpan().SequenceEqual(generated), TranscriptText.FirstDifference(checkedIn, generated));
    }
}
