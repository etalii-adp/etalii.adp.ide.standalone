using Xunit;

namespace EtAlii.Adp.Diagram.Rdf.Tests.Parity;

/// <summary>
/// Today's code reproduces each of the four checked-in transcripts byte for byte: the oracles every task
/// of <c>rdf-disl-fbl</c> is held to (its Requirement 1).
/// </summary>
/// <remarks>
/// A difference is either a regression or an intended change. Only the second is fixed by regenerating -
/// <c>ADP_WRITE_PARITY_TRANSCRIPTS=1</c> rewrites the files before comparing - and the diff of a checked-in
/// file is then the review of that change, which the specification must name.
/// </remarks>
public sealed class RdfTranscriptTests
{
    public static TheoryData<string> Readings() => [.. RdfTranscript.Readings.Select(reading => reading.Name)];

    [Theory]
    [MemberData(nameof(Readings))]
    public async Task TodaysCode_ReproducesTheCheckedInTranscript_ByteForByte(string name)
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        var reading = RdfTranscript.Readings.Single(candidate => candidate.Name == name);

        // Act.
        var generated = await RdfTranscript.GenerateAsync(reading, cancellationToken);
        if (Environment.GetEnvironmentVariable("ADP_WRITE_PARITY_TRANSCRIPTS") == "1")
        {
            await File.WriteAllBytesAsync(RdfTranscript.CheckedInPath(reading), generated, cancellationToken);
        }

        var checkedIn = await File.ReadAllBytesAsync(RdfTranscript.CheckedInPath(reading), cancellationToken);

        // Assert.
        Assert.True(checkedIn.AsSpan().SequenceEqual(generated), TranscriptText.FirstDifference(checkedIn, generated));
    }
}
