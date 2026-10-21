using Xunit;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests.Parity;

/// <summary>
/// Today's code reproduces the frozen transcript byte for byte: the oracle every step of
/// <c>functional-decomposition-graph-disl-fbl</c> is held to (its Requirement 1).
/// </summary>
/// <remarks>
/// A difference is either a regression or an intended change. Only the second is fixed by
/// regenerating - <c>ADP_WRITE_PARITY_TRANSCRIPTS=1</c> rewrites the file before comparing - and the
/// diff of the checked-in file is then the review of that change, which the specification's
/// Requirement 1.4 says must be one it names.
/// </remarks>
public sealed class FdgTranscriptTests
{
    [Fact]
    public async Task TodaysCode_ReproducesTheCheckedInTranscript_ByteForByte()
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act.
        var generated = await FdgTranscript.GenerateAsync(cancellationToken);
        if (Environment.GetEnvironmentVariable("ADP_WRITE_PARITY_TRANSCRIPTS") == "1")
        {
            await File.WriteAllBytesAsync(FdgTranscript.CheckedInPath, generated, cancellationToken);
        }

        var checkedIn = await File.ReadAllBytesAsync(FdgTranscript.CheckedInPath, cancellationToken);

        // Assert.
        Assert.True(checkedIn.AsSpan().SequenceEqual(generated), TranscriptText.FirstDifference(checkedIn, generated));
    }
}
