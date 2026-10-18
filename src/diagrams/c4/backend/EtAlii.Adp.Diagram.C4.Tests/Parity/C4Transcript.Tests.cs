using Xunit;

namespace EtAlii.Adp.Diagram.C4.Tests.Parity;

/// <summary>
/// Today's code reproduces the frozen transcript byte for byte: the oracle every step of the C4
/// conversion to DISL definitions and an FBL binding is held to (c4-disl-fbl Requirement 1.3).
/// </summary>
/// <remarks>
/// A difference is either a regression or a change that specification names (Requirement 1.4). Only
/// the second is fixed by regenerating - <c>ADP_WRITE_PARITY_TRANSCRIPTS=1</c> rewrites the file before
/// comparing - and the diff of the checked-in file is then the review of that change.
/// </remarks>
public sealed class C4TranscriptTests
{
    [Fact]
    public async Task TodaysCode_ReproducesTheCheckedInTranscript_ByteForByte()
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act.
        var generated = await C4Transcript.GenerateAsync(cancellationToken);
        if (Environment.GetEnvironmentVariable("ADP_WRITE_PARITY_TRANSCRIPTS") == "1")
        {
            await File.WriteAllBytesAsync(C4Transcript.CheckedInPath, generated, cancellationToken);
        }

        var checkedIn = File.Exists(C4Transcript.CheckedInPath) ? await File.ReadAllBytesAsync(C4Transcript.CheckedInPath, cancellationToken) : [];

        // Assert.
        Assert.True(checkedIn.AsSpan().SequenceEqual(generated), TranscriptText.FirstDifference(checkedIn, generated));
    }
}
