using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// The module's own binding, <c>dependency-graph.fbl</c>, embedded (dependency-graph-disl-fbl, task 7, Requirement 2.1):
/// it loads with no error and only the warning of its newer FBL version, and a new document is its template.
/// </summary>
public class DependencyGraphBindingTests
{
    [Fact]
    public void TheEmbeddedBinding_LoadsWithNoError_AndOnlyTheWarningOfItsNewerVersion()
    {
        // Act.
        var problems = FblDocumentLoader.Load(typeof(Diagram).Assembly.ResourceBytes("dependency-graph.fbl"), out var document);

        // Assert: FBL 0.23 for its document id and linked nodes (spec 092), which this library reads, beside the
        // version warning every binding newer than the library's full reading carries (FBL §2.7).
        var problem = Assert.Single(problems);
        Assert.Equal(LoadSeverity.Warning, problem.Severity);
        Assert.Contains("FBL 0.23", problem.Message, StringComparison.Ordinal);
        Assert.True(document!.Bindings.ContainsKey("dgr"));
    }

    [Fact]
    public void TheTemplate_IsTheVersionMarkerANewDocumentIdAndAnEmptyList()
    {
        // Act.
        var template = DependencyGraphBody.Binding.Template?.Text;

        // Assert: the version marker, the document id a new graph is given (FBL 17.2, spec 092) and an empty element list, CRLF, and nothing else.
        Assert.Equal("dependencies: 1\r\nid: {newid:document}\r\nelements: []\r\n", template);
    }

    [Fact]
    public void TheTemplate_ReadsAsAnEmptyGraph()
    {
        // Act.
        var body = DependencyGraphBody.Parse(DependencyGraphBody.Binding.Template!.Text);

        // Assert.
        Assert.False(body.IsUnreadable);
        Assert.Empty(body.Disl.Diagram.Elements);
        Assert.Empty(DependencyGraphFindings.Judge(body));
    }

    [Fact]
    public void AnEntryWithALink_IsALinkedNode_AndTheTopLevelIdIsTheDocumentId()
    {
        // Act: spec 089's form of a dependency graph that links to an element of another document (spec 092, FR-001).
        var body = DependencyGraphBody.Parse("dependencies: 1\r\nid: k3m9q2xa\r\nelements:\r\n  - id: a\r\n    label: API\r\n  - id: b\r\n    link: $r7t2w9pa@roadmap.tml#e1.label=Launch\r\n    x: 3\r\nrelations:\r\n  - id: d\r\n    from: a\r\n    to: b\r\n");

        // Assert.
        Assert.Equal("k3m9q2xa", body.Model.DocumentId);
        var linked = Assert.Single(body.Disl.Diagram.Elements, element => element.Type.Name == "LinkedNode");
        Assert.Equal("$r7t2w9pa@roadmap.tml#e1.label=Launch", linked.ValueOf("link"));
        Assert.Single(body.Disl.Diagram.Elements, element => element.Type.Name == "Node");
        Assert.Single(body.Disl.Diagram.Relations);
        Assert.Empty(DependencyGraphFindings.Judge(body));
    }
}
