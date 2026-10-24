using Xunit;

namespace EtAlii.Adp.Diagram.Tests.Support;

/// <summary>
/// Today's code reproduces the module's frozen transcript byte for byte: the oracle every
/// switch-over to the DISL definition is held to. A module's test derives from it with its adapter.
/// </summary>
/// <remarks>
/// A difference is either a regression or an intended change. Only the second is fixed by
/// regenerating - <c>ADP_WRITE_PARITY_TRANSCRIPTS=1</c> rewrites the file before comparing - and the
/// diff of the checked-in file is then the review of that change.
/// </remarks>
public abstract class TranscriptTests<TAdapter>
    where TAdapter : TranscriptAdapter, new()
{
    [Fact]
    public async Task TodaysCode_ReproducesTheCheckedInTranscript_ByteForByte()
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        var adapter = new TAdapter();

        // Act.
        var generated = await TranscriptHarness.GenerateAsync(adapter, cancellationToken);
        if (Environment.GetEnvironmentVariable(TranscriptHarness.RegenerateVariable) == "1")
        {
            await File.WriteAllBytesAsync(adapter.CheckedInPath, generated, cancellationToken);
        }

        var checkedIn = await File.ReadAllBytesAsync(adapter.CheckedInPath, cancellationToken);

        // Assert.
        Assert.True(checkedIn.AsSpan().SequenceEqual(generated), TranscriptText.FirstDifference(checkedIn, generated));
    }
}
