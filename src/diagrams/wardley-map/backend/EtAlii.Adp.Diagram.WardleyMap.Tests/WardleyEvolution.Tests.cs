using Xunit;

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

public class WardleyEvolutionTests
{
    [Fact]
    public void Boundaries_AreTheReferenceRenderersOffsetsDividedByTheAxisWidth()
    {
        // Arrange. The derivation is what is pinned, not three literals: these are the
        // reference renderer's own EvoOffsets over an axis width of 20 (Requirement 8.2).
        const double axisWidth = 20d;

        // Act and assert.
        Assert.Equal(3.5d / axisWidth, WardleyEvolution.CustomBuilt);
        Assert.Equal(8d / axisWidth, WardleyEvolution.Product);
        Assert.Equal(14d / axisWidth, WardleyEvolution.Commodity);
    }

    [Fact]
    public void Boundaries_AreTheValuesTheSpecStates()
    {
        // Arrange, act and assert. The other half of the pin: if someone "simplifies" the
        // derivation above, the numbers it must still produce are written here.
        Assert.Equal(0.175d, WardleyEvolution.CustomBuilt);
        Assert.Equal(0.400d, WardleyEvolution.Product);
        Assert.Equal(0.700d, WardleyEvolution.Commodity);
    }

    [Fact]
    public void Stages_AreFourAndCoverTheWholeAxisWithoutGapsOrOverlaps()
    {
        // Act.
        var stages = WardleyEvolution.Stages;

        // Assert.
        Assert.Equal(4, stages.Count);
        Assert.Equal(0d, stages[0].Start);
        Assert.Equal(1d, stages[^1].End);
        for (var index = 1; index < stages.Count; index++)
        {
            Assert.Equal(stages[index - 1].End, stages[index].Start);
        }
    }

    [Fact]
    public void Stages_CarryTheNotationsOwnLabels_ParentheticalsIncluded()
    {
        // Act.
        var labels = WardleyEvolution.Stages.Select(stage => stage.Label).ToArray();

        // Assert. "Product" without "(+rental)" is a different claim about the stage.
        Assert.Equal(["Genesis", "Custom Built", "Product (+rental)", "Commodity (+utility)"], labels);
    }

    [Theory]
    [InlineData(0d, "Genesis")]
    [InlineData(0.1d, "Genesis")]
    [InlineData(0.174d, "Genesis")]
    [InlineData(0.175d, "Custom Built")]
    [InlineData(0.3d, "Custom Built")]
    [InlineData(0.399d, "Custom Built")]
    [InlineData(0.4d, "Product (+rental)")]
    [InlineData(0.5d, "Product (+rental)")]
    [InlineData(0.699d, "Product (+rental)")]
    [InlineData(0.7d, "Commodity (+utility)")]
    [InlineData(0.9d, "Commodity (+utility)")]
    [InlineData(1d, "Commodity (+utility)")]
    public void StageOf_ReadsAMaturityAgainstTheBands(double maturity, string expected)
    {
        // Act.
        var stage = WardleyEvolution.StageOf(maturity);

        // Assert. Each boundary is tested exactly, and just below, because "which side of the
        // line" is the only interesting question this function answers.
        Assert.Equal(expected, stage.Label);
    }

    [Theory]
    [InlineData(-0.5d, "Genesis")]
    [InlineData(1.5d, "Commodity (+utility)")]
    public void StageOf_ClampsRatherThanThrowing_ForACoordinateOutsideTheAxis(double maturity, string expected)
    {
        // Act.
        var stage = WardleyEvolution.StageOf(maturity);

        // Assert. An out-of-range coordinate is a document error Requirement 14.3 reports; it
        // must not stop the rest of the map rendering (Requirement 3.5).
        Assert.Equal(expected, stage.Label);
    }

    [Fact]
    public void StageOf_AgreesWithTheStageRangesItIsReadingAgainst()
    {
        // Arrange. A property rather than a case: whatever StageOf returns, the value must sit
        // inside that stage's own range. This catches a boundary edited in one place only.
        for (var maturity = 0d; maturity <= 1d; maturity += 0.001d)
        {
            // Act.
            var stage = WardleyEvolution.StageOf(maturity);

            // Assert.
            Assert.True(
                maturity >= stage.Start && (maturity < stage.End || stage.End == 1d),
                $"{maturity} was read as {stage.Label}, whose range is {stage.Start}..{stage.End}.");
        }
    }
}
