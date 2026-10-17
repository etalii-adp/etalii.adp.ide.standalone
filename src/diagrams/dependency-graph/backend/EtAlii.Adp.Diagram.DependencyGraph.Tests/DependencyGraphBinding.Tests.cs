using EtAlii.Adp.Specification.Fbl.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// The module's own binding, <c>dependency-graph.fbl</c>, embedded (dependency-graph-disl-fbl, task 7, Requirement 2.1):
/// it loads with no error and no warning, and a new document is its template.
/// </summary>
public class DependencyGraphBindingTests
{
    [Fact]
    public void TheEmbeddedBinding_LoadsWithNoErrorAndNoWarning()
    {
        // Act.
        var problems = FblDocumentLoader.Load(typeof(Diagram).Assembly.ResourceBytes("dependency-graph.fbl"), out var document);

        // Assert.
        Assert.Empty(problems);
        Assert.True(document!.Bindings.ContainsKey("dgr"));
    }

    [Fact]
    public void TheTemplate_IsTheBytesANewGraphHasAlwaysHad()
    {
        // Act.
        var template = DependencyGraphBody.Binding.Template?.Text;

        // Assert: the version marker and an empty element list, CRLF, and nothing else.
        Assert.Equal("dependencies: 1\r\nelements: []\r\n", template);
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
}
