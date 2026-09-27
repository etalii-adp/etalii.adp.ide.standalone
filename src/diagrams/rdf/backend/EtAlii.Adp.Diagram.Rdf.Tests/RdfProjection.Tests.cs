using System.Text;
using EtAlii.Adp.Documents;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The data-graph reading: cards for IRI terms, literals as rows, types as badges, collections
/// as ordered member edges with their plumbing left in the file, blank nodes marked - and the
/// budget cutting deterministically in document order (rdf-diagram Requirements 3 and 8).
/// </summary>
public class RdfProjectionTests
{
    private static RdfProjectionResult ProjectFixture(string name)
    {
        var text = File.ReadAllText(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name));
        return RdfProjection.Project(RdfParser.Parse(LineDocument.Parse(text)));
    }

    [Fact]
    public void TheConstructCorpus_ProjectsTheAdoptedPositions()
    {
        // Arrange & act.
        var projection = ProjectFixture("constructs.ttl");

        // Assert.
        // IRI subjects draw as cards with prefixed-name displays.
        var alice = projection.Nodes.Single(node => node.Id == "res:http://example.org/alice");
        Assert.Equal("ex:alice", alice.Display);

        // Types are badges, not edges: no edge carries rdf:type.
        Assert.Equal(["foaf:Person"], alice.Types);
        Assert.DoesNotContain(projection.Edges, edge => edge.PredicateIri == RdfVocabulary.Type);

        // Literals fold into their subject's rows with their annotations.
        Assert.Contains(alice.Rows, row => row is { Predicate: "foaf:name", Value: "Alice", Annotation: "" });
        Assert.Contains(alice.Rows, row => row.Value.StartsWith("A description", StringComparison.Ordinal) && row.Annotation == "@en");

        // IRI objects draw as edges between cards.
        Assert.Contains(projection.Edges, edge =>
            edge.FromId == "res:http://example.org/alice" && edge.ToId == "res:http://example.org/bob" && edge.Predicate == "foaf:knows");

        // The collection renders as ordered member edges; its cons cells never become nodes.
        var carol = "res:http://example.org/carol";
        Assert.Contains(projection.Edges, edge => edge.FromId == carol && edge.Predicate == "foaf:interests [1]");
        Assert.Contains(projection.Edges, edge => edge.FromId == carol && edge.Predicate == "foaf:interests [2]");
        var carolNode = projection.Nodes.Single(node => node.Id == carol);
        // The collection's literal member lands as a row carrying its index.
        Assert.Contains(carolNode.Rows, row => row is { Predicate: "foaf:interests [3]", Value: "4.5" });

        // Blank nodes draw, marked - labeled and anonymous alike - and cons cells do not.
        Assert.Contains(projection.Nodes, node => node is { Blank: true, Display: "_:c" });
        Assert.All(projection.Nodes.Where(node => node.Blank), node => Assert.StartsWith("blank:", node.Id));

        // The property-list blank carries its rows like any card.
        var dave = projection.Nodes.Single(node => node.Rows.Any(row => row.Value == "Dave"));
        Assert.True(dave.Blank);

        // Nothing was cut.
        Assert.False(projection.Truncated);
        Assert.Equal(projection.Nodes.Count, projection.Shown);
    }

    [Fact]
    public void TheBudget_CutsInDocumentOrder_Deterministically()
    {
        // Arrange: more subjects than the budget allows, in one generated document.
        var builder = new StringBuilder("@prefix ex: <http://example.org/> .\r\n");
        for (var i = 0; i < 30; i++)
        {
            builder.Append($"ex:s{i:D3} ex:knows ex:s{(i + 1):D3} .\r\n");
        }

        var model = RdfParser.Parse(LineDocument.Parse(builder.ToString()));

        // Act.
        var projection = RdfProjection.Project(model, budget: 10);
        var again = RdfProjection.Project(model, budget: 10);

        // Assert.
        Assert.True(projection.Truncated);
        Assert.Equal(10, projection.Shown);
        Assert.Equal(31, projection.Total);
        // First N in document order of first appearance.
        Assert.Equal("res:http://example.org/s000", projection.Nodes[0].Id);
        Assert.Equal("res:http://example.org/s009", projection.Nodes[9].Id);
        // Edges to nodes that did not make the cut go with them.
        Assert.All(projection.Edges, edge => Assert.NotEqual("res:http://example.org/s010", edge.ToId));
        // The same file always truncates the same way.
        Assert.Equal(projection.Nodes.Select(node => node.Id), again.Nodes.Select(node => node.Id));
    }

    [Fact]
    public void ADisplayName_FallsBackFromPrefixedName_ToLocalName()
    {
        // Arrange & act: the foaf namespace is not declared, so no prefix reaches it.
        var model = RdfParser.Parse(LineDocument.Parse(
            "@prefix ex: <http://example.org/> .\r\nex:a ex:p <http://xmlns.com/foaf/0.1/Person> .\r\n"));
        var projection = RdfProjection.Project(model);

        // Assert.
        Assert.Contains(projection.Nodes, node => node.Display == "Person");
    }
}
