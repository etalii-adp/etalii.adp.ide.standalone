using Xunit;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests.Parity;

/// <summary>
/// The module reproduces the frozen transcript byte for byte: the oracle every switch-over to the
/// DISL definition is held to, and the one that survives the hand-written code it was frozen from.
/// </summary>
/// <remarks>
/// A difference is either a regression or an intended change. Only the second is fixed by
/// regenerating - <c>ADP_WRITE_PARITY_TRANSCRIPTS=1</c> rewrites the file before comparing - and the
/// diff of the checked-in file is then the review of that change.
/// </remarks>
public sealed class DependencyGraphTranscriptTests
{
    [Fact]
    public async Task TheModule_ReproducesTheCheckedInTranscript_ByteForByte()
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act.
        var generated = await DependencyGraphTranscript.GenerateAsync(cancellationToken);
        if (Environment.GetEnvironmentVariable("ADP_WRITE_PARITY_TRANSCRIPTS") == "1")
        {
            await File.WriteAllBytesAsync(DependencyGraphTranscript.CheckedInPath, generated, cancellationToken);
        }

        var checkedIn = await File.ReadAllBytesAsync(DependencyGraphTranscript.CheckedInPath, cancellationToken);

        // Assert.
        Assert.True(checkedIn.AsSpan().SequenceEqual(generated), TranscriptText.FirstDifference(checkedIn, generated));
    }
}
