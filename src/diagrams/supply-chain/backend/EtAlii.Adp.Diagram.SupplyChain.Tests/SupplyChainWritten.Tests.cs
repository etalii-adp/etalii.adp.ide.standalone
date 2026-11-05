using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests;

/// <summary>
/// The definition's plugin functions answer what the file writes, where the model only knows what resolves: the id of
/// an entry without one or of a later holder, the group a node names that is not there, the end of a flow that names no
/// node.
/// </summary>
public sealed class SupplyChainWrittenTests
{
    private const string Document =
        "supply-chain: 1\r\n"
        + "groups:\r\n"
        + "  - id: eu\r\n    name: Europe\r\n"
        + "nodes:\r\n"
        + "  - id: mine\r\n    type: source\r\n    group: eu\r\n"
        + "  - id: mine\r\n    type: hub\r\n    group: nowhere\r\n"
        + "  - type: consumer\r\n"
        + "flows:\r\n"
        + "  - id: ore\r\n    from: mine\r\n    to: elsewhere\r\n";

    private static readonly DislDiagram _diagram = SupplyChainDefinition.Diagram(SupplyChainReading.Parse(Document));

    [Theory]
    [InlineData(0, "written(self, 'id')", "mine")]
    [InlineData(1, "written(self, 'id')", "mine")]
    [InlineData(2, "written(self, 'id')", "")]
    [InlineData(0, "written(self, 'group')", "eu")]
    [InlineData(1, "written(self, 'group')", "nowhere")]
    [InlineData(1, "has(self.group)", false)]
    [InlineData(1, "written(self, 'type')", "hub")]
    [InlineData(0, "laterHolder(self)", false)]
    [InlineData(1, "laterHolder(self)", true)]
    [InlineData(2, "laterHolder(self)", false)]
    public void ANode_IsReadAsWritten(int index, string expression, object expected) =>
        Assert.Equal(expected, Evaluate(expression, _diagram.NodesOfType("Stage").ElementAt(index)));

    [Theory]
    [InlineData("written(self, 'id')", "ore")]
    [InlineData("written(self, 'from')", "mine")]
    [InlineData("written(self, 'to')", "elsewhere")]
    [InlineData("has(self.target)", false)]
    public void AFlow_IsReadAsWritten(string expression, object expected) =>
        Assert.Equal(expected, Evaluate(expression, Assert.Single(_diagram.RelationsOfType("Flow"))));

    [Fact]
    public void AKeyItDoesNotRead_IsAnEvaluationError() =>
        Assert.Equal(
            "written() reads id, group, from, to or type, and not 'name'.",
            Assert.IsType<CelError>(Evaluate("written(self, 'name')", _diagram.NodesOfType("Stage").First())).Message);

    private static object? Evaluate(string expression, DislElement self) =>
        SupplyChainDefinition.Specification.Environment(DislContext.Element).Compile(expression)
            .Evaluate(new Dictionary<string, object?> { ["self"] = self, ["diagram"] = _diagram, ["env"] = null });
}
