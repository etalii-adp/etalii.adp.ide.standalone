using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.Sankey.Tests;

/// <summary>
/// The shipped examples: each draws every node and flow it declares, and the figures the readmes
/// state are the figures the diagram shows.
/// </summary>
public sealed class SankeyExamplesTests
{
    public static TheoryData<string> Every => [SankeyExamples.BrightwaterCoffee, SankeyExamples.UkEnergy, SankeyExamples.RecentGraduates];

    [Theory]
    [MemberData(nameof(Every))]
    public void AnExample_DrawsEverythingItDeclares_ReadingLeftToRight(string path)
    {
        // Act.
        var model = Load(path);
        var layout = SankeyLayout.Of(model);
        var elements = new SankeyElementMapper().All(model);

        // Assert.
        Assert.Empty(model.Problems);
        Assert.Equal(model.Nodes.Count, layout.Nodes.Count);
        Assert.Equal(model.Flows.Count, layout.Flows.Count);
        Assert.Equal(model.Nodes.Count + model.Flows.Count, elements.Count);
        Assert.All(layout.Bands.Values, band => Assert.False(band.Backward));
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void AnExample_KeepsItsNodesApart_InEveryColumn(string path)
    {
        // Act.
        var layout = SankeyLayout.Of(Load(path));

        // Assert.
        foreach (var column in layout.ColumnOrder)
        {
            for (var i = 1; i < column.Count; i++)
            {
                Assert.True(layout.Boxes[column[i]].Y >= layout.Boxes[column[i - 1]].Bottom + SankeyGeometry.NodeGap - 0.05, $"{column[i]} overlaps {column[i - 1]}");
            }
        }
    }

    [Fact]
    public void TheCompany_Balances_FromSegmentsToNetProfit()
    {
        // Act.
        var layout = SankeyLayout.Of(Load(SankeyExamples.BrightwaterCoffee));

        // Assert.
        Assert.Equal(3500, layout.Values["revenue"]);
        Assert.Equal(420, layout.Values["net-profit"]);
        Assert.Equal(5, layout.ColumnOrder.Count);
    }

    [Fact]
    public void TheEnergyExample_JoinsTheSplitNuclearLinks_IntoTheOriginalFigure()
    {
        // Act.
        var model = Load(SankeyExamples.UkEnergy);

        // Assert.
        Assert.Equal(48, model.Nodes.Count);
        Assert.Equal(68, model.Flows.Count);
        Assert.Equal(839.978, model.Flows.Single(flow => flow.Id == "nuclear->thermal-generation").Value);
    }

    [Fact]
    public void TheGraduatesExample_CountsEveryoneOnce()
    {
        // Act.
        var model = Load(SankeyExamples.RecentGraduates);
        var layout = SankeyLayout.Of(model);

        // Assert: what reaches the employed is what leaves them, by the kind of job - the
        // unclassified remainder closes the gap the survey leaves.
        var inflow = model.Flows.Where(flow => flow.To == "employed").Sum(flow => flow.Value!.Value);
        var outflow = model.Flows.Where(flow => flow.From == "employed").Sum(flow => flow.Value!.Value);
        Assert.Equal(inflow, outflow, 1);
        Assert.Equal(3, layout.ColumnOrder.Count);
        Assert.Equal(16, layout.ColumnOrder[0].Count);
    }

    private static SankeyModel Load(string path) => SankeyParser.Parse(LineDocument.Parse(File.ReadAllText(path)));
}
