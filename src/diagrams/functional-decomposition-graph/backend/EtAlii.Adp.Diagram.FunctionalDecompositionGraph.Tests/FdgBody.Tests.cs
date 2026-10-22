using Xunit;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests;

/// <summary>A <c>.fdg</c> body read through the module's binding (functional-decomposition-graph-disl-fbl task 9).</summary>
public class FdgBodyTests
{
    /// <summary>A new document is the one the module has always written, now the binding's template.</summary>
    [Fact]
    public void TheTemplate_IsTheEmptyDocumentTheModuleHasAlwaysWritten() =>
        Assert.Equal(FdgParser.EmptyDocument("\r\n"), FdgBody.Template);

    /// <summary>Requirement 4: every value reads as the text it was written with, whatever YAML would make of it.</summary>
    [Theory]
    [InlineData("n-decimal", "name", "1.0")]
    [InlineData("n-true", "name", "true")]
    [InlineData("n-hex", "name", "0x10")]
    [InlineData("n-exp", "name", "1e2")]
    [InlineData("n-quoted", "name", "007")]
    [InlineData("007", "name", "007")]
    [InlineData("007", "description", "1.0")]
    [InlineData("t-decimal", "text", "1.0")]
    [InlineData("c-007", "name", "1.10")]
    public async Task AValue_ReadsAsWritten(string id, string attribute, string written)
    {
        // Arrange.
        var text = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "scalars-as-written.fdg"), TestContext.Current.CancellationToken);

        // Act.
        var element = FdgBody.Parse(text).Disl.Diagram.ElementById(id);

        // Assert.
        Assert.NotNull(element);
        Assert.Equal(written, FdgDefinition.TextOf(element, attribute));
    }

    /// <summary>Reading never throws on content: a body that is not a graph, or not YAML at all, still reads.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("\r\n")]
    [InlineData("- a\r\n- b\r\n")]
    [InlineData("just a scalar")]
    [InlineData("functional-decomposition-graph: [1\r\n")]
    [InlineData("functional-decomposition-graph: 1\r\nelements: 3\r\nconnections: {a: b}\r\n")]
    [InlineData("functional-decomposition-graph: 1\r\nelements:\r\n  - - nested\r\n  - type: [ui-element]\r\n    id: {a: b}\r\n    x: [1]\r\nconnections:\r\n  - from: [a]\r\n    to: {b: c}\r\n    type: shows\r\n")]
    [InlineData("functional-decomposition-graph: 1\r\nelements:\r\n  - &a {id: a, type: action}\r\n  - *a\r\nconnections: []\r\n")]
    [InlineData("\tfunctional-decomposition-graph: 1\r\n")]
    [InlineData("functional-decomposition-graph: 1\r\nelements:\r\n  - id: a\r\n    id: b\r\n")]
    public void Reading_NeverThrowsOnContent(string text)
    {
        // Act.
        var body = FdgBody.Parse(text);
        var findings = FdgDefinition.Findings(body);

        // Assert.
        Assert.NotNull(body.Disl);
        Assert.NotNull(findings);
    }
}
