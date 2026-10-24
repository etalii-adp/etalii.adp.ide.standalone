using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.Sankey.Tests;

/// <summary>
/// The definition's plugin functions are the module's own: the five layout functions answer what
/// <see cref="SankeyLayout"/> computes for the parsed document, from one layout per diagram, and
/// <c>writtenEnd</c> answers a flow's ends as written (sankey-disl-fbl task 6).
/// </summary>
public class SankeyFunctionsTests
{
    public static TheoryData<string> Examples() => [SankeyExamples.BrightwaterCoffee, SankeyExamples.UkEnergy, SankeyExamples.RecentGraduates];

    [Theory]
    [MemberData(nameof(Examples))]
    public async Task TheLayoutFunctions_AnswerTheModulesLayout(string path)
    {
        // Arrange.
        var model = SankeyReader.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        var layout = SankeyLayout.Of(model);
        var diagram = SankeyReader.BodyOf(model)!.Disl.Diagram;

        // Act and assert.
        Assert.Equal((long)layout.ColumnOrder.Count, Evaluate("sankeyColumnCount()", diagram, null));
        Assert.Equal(layout.Scale, Evaluate("sankeyScale()", diagram, null));
        Assert.NotEmpty(layout.Nodes);
        foreach (var node in layout.Nodes)
        {
            var element = SankeyDefinition.ElementOf(diagram, node.Id)!;
            Assert.Equal((long)layout.Columns[node.Id], Evaluate("sankeyColumn(self)", diagram, element));
            Assert.Equal(layout.Boxes[node.Id].Y, Evaluate("sankeyTop(self)", diagram, element));
        }

        Assert.NotEmpty(layout.Flows);
        foreach (var flow in layout.Flows)
        {
            var element = SankeyDefinition.ElementOf(diagram, flow.Id)!;
            Assert.Equal(layout.Bands[flow.Id].SourceAt, Evaluate("bandAt(self, 'source')", diagram, element));
            Assert.Equal(layout.Bands[flow.Id].TargetAt, Evaluate("bandAt(self, 'target')", diagram, element));
        }
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public async Task TheLayout_IsComputedOncePerDiagram(string path)
    {
        // Arrange.
        var model = SankeyReader.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        var diagram = SankeyReader.BodyOf(model)!.Disl.Diagram;

        // Act.
        var first = SankeyFunctions.LayoutOf(diagram);
        _ = Evaluate("sankeyColumnCount()", diagram, null);

        // Assert.
        Assert.Same(first, SankeyFunctions.LayoutOf(diagram));
    }

    [Fact]
    public void ANodeThatIsNotDrawn_HasNoColumn()
    {
        // Arrange: the second holder of an id is kept and not drawn.
        var diagram = DiagramOf("sankey: 1\nnodes:\n  - id: a\n  - id: a\n  - id: b\nflows:\n  - from: a\n    to: b\n    value: 1\n");
        var later = diagram.NodesOfType("Node")[1];

        // Act.
        var value = Evaluate("sankeyColumn(self)", diagram, later);

        // Assert.
        Assert.IsType<CelError>(value);
    }

    [Theory]
    [InlineData("  - from: a\n    to: b\n", "a", "b")]
    [InlineData("  - from: a\n    to: ghost\n", "a", "ghost")]
    [InlineData("  - to: b\n", "", "b")]
    public void WrittenEnd_IsTheEndAsWritten_ResolvedDanglingOrAbsent(string flow, string source, string target)
    {
        // Arrange.
        var diagram = DiagramOf($"sankey: 1\nnodes:\n  - id: a\n  - id: b\nflows:\n{flow}");
        var element = diagram.RelationsOfType("Flow").Single();

        // Act and assert.
        Assert.Equal(source, Evaluate("writtenEnd(self, 'source')", diagram, element));
        Assert.Equal(target, Evaluate("writtenEnd(self, 'target')", diagram, element));
    }

    private static DislDiagram DiagramOf(string text) => SankeyReader.BodyOf(SankeyReader.Parse(text))!.Disl.Diagram;

    private static object? Evaluate(string expression, DislDiagram diagram, DislElement? self) =>
        SankeyDefinition.Specification.Environment(DislContext.Element).Compile(expression)
            .Evaluate(new Dictionary<string, object?> { ["self"] = self, ["diagram"] = diagram, ["env"] = new DislEnv().ToCel() });
}
