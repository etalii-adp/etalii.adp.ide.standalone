using Xunit;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests.Parity;

/// <summary>
/// Today's code reproduces the frozen transcript byte for byte: the oracle every switch-over to the
/// DISL definition is held to (runtime plan step S2).
/// </summary>
/// <remarks>
/// A difference is either a regression or an intended change. Only the second is fixed by
/// regenerating - <c>ADP_WRITE_PARITY_TRANSCRIPTS=1</c> rewrites the file before comparing - and the
/// diff of the checked-in file is then the review of that change.
/// </remarks>
public sealed class GhgTranscriptTests
{
    [Fact]
    public async Task TodaysCode_ReproducesTheCheckedInTranscript_ByteForByte()
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act.
        var generated = await GhgTranscript.GenerateAsync(cancellationToken);
        if (Environment.GetEnvironmentVariable("ADP_WRITE_PARITY_TRANSCRIPTS") == "1")
        {
            await File.WriteAllBytesAsync(GhgTranscript.CheckedInPath, generated, cancellationToken);
        }

        var checkedIn = await File.ReadAllBytesAsync(GhgTranscript.CheckedInPath, cancellationToken);

        // Assert.
        Assert.True(checkedIn.AsSpan().SequenceEqual(generated), TranscriptText.FirstDifference(checkedIn, generated));
    }
}
