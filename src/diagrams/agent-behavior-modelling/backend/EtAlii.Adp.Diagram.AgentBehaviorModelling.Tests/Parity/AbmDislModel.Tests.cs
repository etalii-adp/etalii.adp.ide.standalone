using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests.Parity;

/// <summary>
/// The behavior model's ids and its <c>Child</c> relations are the definition's to compute: the
/// derived-id rule of <c>persistence.ids</c> and the derived relation type <c>Child</c>. Built from the
/// parser's nodes with no ids (the module has no FBL reader yet), the completed model has the parser's
/// ids, and one <c>Child</c> per parent and child, in the parser's order.
/// </summary>
public class AbmDislModelTests
{
    /// <summary>Three roots and nesting four deep, which the corpus lacks: its documents each have one root.</summary>
    internal const string Forest =
        "## Behavior\n- **Do:** A\n- **Do in order:** B\n  - **Do:** B1\n  - **Try in order:** B2\n    - **Do:** B2a\n    - **Retry up to 2 times:** B2b\n      - **Ask the user:** B2b1\n  - **Check:** B3\n- **Check:** C\n";

    public static TheoryData<string> Documents() => [.. Texts().Select(document => document.Name)];

    private static IEnumerable<(string Name, string Text)> Texts() => AbmDisl.Corpus().Append(("inline/forest", Forest));

    [Theory]
    [MemberData(nameof(Documents))]
    public void TheDerivedIds_AreTheParsersIds(string name)
    {
        // Arrange.
        var model = AbmDisl.Parse(Texts().Single(document => document.Name == name).Text);
        var diagram = AbmDisl.DiagramOf(model, ids: false);

        // Act.
        var findings = DislModelBuilder.Complete(diagram);

        // Assert.
        Assert.Empty(findings);
        Assert.Equal(model.Nodes.Select(node => node.Id), diagram.Nodes.Select(node => node.Id));
        Assert.Equal(model.Nodes.Select(node => node.ParentId), diagram.Nodes.Select(node => node.Parent?.Id));
        Assert.Equal(model.Nodes.Select(node => string.Join(",", node.ChildIds)), diagram.Nodes.Select(node => string.Join(",", node.Children.Select(child => child.Id))));
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void TheDerivedChildRelations_AreTheParsersParents(string name)
    {
        // Arrange.
        var model = AbmDisl.Parse(Texts().Single(document => document.Name == name).Text);
        var diagram = AbmDisl.DiagramOf(model, ids: false);

        // Act.
        DislModelBuilder.Complete(diagram);

        // Assert.
        var expected = model.Nodes.Where(node => node.ParentId is not null).Select(node => $"child:{node.Id} {node.ParentId} -> {node.Id} from {node.Id}");
        var actual = diagram.Relations.Select(relation =>
            $"{relation.Id} {relation.Source?.Id} -> {relation.Target?.Id} from {string.Join(",", relation.Sources.Select(source => source.Id))}");
        Assert.Equal(expected, actual);
        Assert.All(diagram.Relations, relation => Assert.True(relation.IsDerived && relation.Type.Name == "Child" && !relation.IdIsStored));
    }

    /// <summary>An id read from storage that differs from the computed one gives way to it (DISL §11.5.2).</summary>
    [Fact]
    public void AStoredIdThatDiffersFromTheDerivedOne_TakesTheDerivedOne()
    {
        var model = AbmDisl.Parse(AbmCorpus.WriterTree);
        var diagram = new DislDiagram(AbmDisl.Specification);
        var byId = new Dictionary<string, DislElement>(StringComparer.Ordinal);
        foreach (var node in model.Nodes)
        {
            byId[node.Id] = AbmDisl.NodeOf(diagram, node, "stale-" + node.Id, node.ParentId is { } parent ? byId[parent] : null);
        }

        DislModelBuilder.Complete(diagram);

        Assert.Equal(model.Nodes.Select(node => node.Id), diagram.Nodes.Select(node => node.Id));
    }

    [Fact]
    public void TheDocuments_ReachSeveralRootsAndDeepNesting()
    {
        var nodes = Texts().SelectMany(document => AbmDisl.Parse(document.Text).Nodes).ToList();
        Assert.Contains(nodes, node => node.Id.Count(c => c == '.') >= 3);
        Assert.Contains(nodes, node => node.ParentId is null && node.Id == "3");
    }
}
