using EtAlii.Adp.Diagram.DependencyGraph.Tests.Parity;
using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// <see cref="DependencyGraphReading"/>, the stand-in for the two language gaps the binding cannot state
/// (dependency-graph-disl-fbl, task 7, Requirements 2.3 and 2.6), compared with the parser as the oracle: nodes are
/// read before dependencies whatever the file's order (finding B5), and a coordinate or a row that will not read is 0
/// (finding B2, Q4 "as today").
/// </summary>
public class DependencyGraphReadingTests
{
    [Fact]
    public void RelationsWrittenAboveTheElements_LeaveTheSharedIdToTheNode()
    {
        // Arrange.
        var text = Text("inline/relations-before-elements");
        var oracle = Oracle(text);

        // Act.
        var diagram = DependencyGraphBody.Parse(text).Disl.Diagram;

        // Assert: the node keeps the id the dependency also holds, and the dependency's ends are the two nodes.
        Assert.Equal(oracle.Elements.Select(element => element.Id), diagram.NodesOfType("Node").Select(DependencyGraphDefinition.WrittenId));
        Assert.Equal("shared", diagram.NodesOfType("Node")[1].Id);
        var dependency = Assert.Single(diagram.RelationsOfType("DependsOn"));
        Assert.NotEqual("shared", dependency.Id);
        Assert.Equal(("aaa", "bbb"), (dependency.Source?.Id, dependency.Target?.Id));
    }

    [Fact]
    public void RelationsWrittenAboveTheElements_ReportTheDependencyAsTheLaterHolder_AtItsLine()
    {
        // Act.
        var problems = DependencyGraphFindings.Judge(DependencyGraphBody.Parse(Text("inline/relations-before-elements")));

        // Assert: the dependency on line 3 is the later holder of 'shared', as the tool has always read it.
        var duplicate = Assert.Single(problems, problem => problem.RuleId == DependencyGraphRules.DuplicateId);
        Assert.Equal(3u, Assert.IsType<DiagramProblemLineLocation>(duplicate.Location).Number);
    }

    [Fact]
    public void ValuesThatWillNotRead_AreReadAsTheParserReadsThem()
    {
        // Arrange.
        var text = Text("inline/unreadable-values");
        var oracle = Oracle(text);

        // Act.
        var nodes = DependencyGraphBody.Parse(text).Disl.Diagram.NodesOfType("Node");

        // Assert: 'sideways' is 0, 1e3 is 1000, a fractional row is 0, and every node is drawn.
        Assert.Equal(oracle.Elements.Select(element => element.X).Cast<object?>(), nodes.Select(node => node.ValueOf("x")));
        Assert.Equal(oracle.Elements.Select(element => (long)element.Row).Cast<object?>(), nodes.Select(node => node.ValueOf("row")));
        Assert.Equal(oracle.Elements.Select(element => element.Label).Cast<object?>(), nodes.Select(node => node.ValueOf("label")));
    }

    [Fact]
    public void ValuesThatWillNotRead_RaiseNoFinding()
    {
        // Act.
        var problems = DependencyGraphFindings.Judge(DependencyGraphBody.Parse(Text("inline/unreadable-values")));

        // Assert.
        Assert.Empty(problems);
    }

    private static string Text(string name) => DependencyGraphCorpus.Inline.Single(document => document.Name == name).Text;

    private static DependencyGraphModel Oracle(string text) => DependencyGraphParser.Parse(LineDocument.Parse(text));
}
