using Xunit;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests.Parity;

/// <summary>
/// A loop's polarity, as the payload sends it, is the definition's (causal-loop-disl-fbl Requirement 7.3):
/// for every loop of every document of the parity corpus, the derived <c>computed</c> and <c>disagrees</c>
/// that <see cref="CausalLoopBody.Read"/> carries equal the hand-written arithmetic the module computed
/// them with (<see cref="LoopPolarity"/>, now the oracle).
/// </summary>
public class CausalLoopDerivedPolarityTests
{
    public static TheoryData<string> Documents() => [.. CausalLoopTranscript.Corpus()];

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryLoop_HasTheHandWrittenPolarity_AndDisagreement(string relative)
    {
        // Arrange.
        var text = await File.ReadAllTextAsync(Path.Combine(CausalLoopTranscript.SourceFolder, relative), TestContext.Current.CancellationToken);

        // Act.
        var model = CausalLoopBody.Parse(text).Read();

        // Assert.
        foreach (var loop in model.Loops)
        {
            var computed = LoopPolarity.Of(model, loop.Variables);
            var disagrees = computed != LoopPolarityResult.Undecidable
                && loop.ClaimsReinforcing is { } claimed
                && claimed != (computed == LoopPolarityResult.Reinforcing);
            Assert.True(computed == loop.Computed && disagrees == loop.Disagrees, $"{relative}, {loop.Id}: expected {computed} {disagrees}, read {loop.Computed} {loop.Disagrees}");
        }
    }
}
