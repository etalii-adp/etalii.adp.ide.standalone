using Xunit;

namespace EtAlii.Adp.Diagram.HelmChart.Tests.Parity;

/// <summary>
/// The module reproduces the frozen transcript byte for byte: the oracle every step of the
/// conversion to the DISL definition and the FBL binding is held to, frozen from the hand-written
/// code before any of it was changed (helm-chart-disl-fbl, Requirement 1.3).
/// </summary>
/// <remarks>
/// A difference is either a regression or an intended change, and this conversion names none. Only
/// an intended change is fixed by regenerating - <c>ADP_WRITE_PARITY_TRANSCRIPTS=1</c> rewrites the
/// file before comparing - and the diff of the checked-in file is then the review of that change.
/// </remarks>
public sealed class HelmTranscriptTests
{
    [Fact]
    public async Task TheModule_ReproducesTheCheckedInTranscript_ByteForByte()
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act.
        var generated = await HelmTranscript.GenerateAsync(cancellationToken);
        if (Environment.GetEnvironmentVariable("ADP_WRITE_PARITY_TRANSCRIPTS") == "1")
        {
            await File.WriteAllBytesAsync(HelmTranscript.CheckedInPath, generated, cancellationToken);
        }

        var checkedIn = await File.ReadAllBytesAsync(HelmTranscript.CheckedInPath, cancellationToken);

        // Assert.
        Assert.True(checkedIn.AsSpan().SequenceEqual(generated), TranscriptText.FirstDifference(checkedIn, generated));
    }
}
