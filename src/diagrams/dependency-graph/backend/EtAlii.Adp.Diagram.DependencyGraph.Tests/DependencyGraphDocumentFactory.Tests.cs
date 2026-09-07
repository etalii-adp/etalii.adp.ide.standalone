using EtAlii.Adp.Common;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using Xunit;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// The document a new graph starts as, and - more importantly - what happens to it next.
/// </summary>
public class DependencyGraphDocumentFactoryTests
{
    private readonly DependencyGraphDocumentFactory _factory = new();

    [Fact]
    public void ANewDocument_IsTheSchemaMarkerAndAnEmptyListAndNothingElse()
    {
        // Act.
        var text = _factory.CreateEmptyDocument("services");

        // Assert.
        var model = DependencyGraphParser.Parse(LineDocument.Parse(text));
        Assert.Empty(model.Elements);
        Assert.Empty(model.Relations);
        Assert.StartsWith("dependencies: 1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("services", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ItAnswersForThisDiagramType()
    {
        // Assert.
        // Resolved by origin; a mismatch is an Add that fails for this type with no error
        // pointing here.
        Assert.Equal(Diagram.DependencyGraph.Origin, _factory.Origin);
    }

    [Fact]
    public void TheFirstElementAddedToAFreshDocument_ProducesValidYaml()
    {
        // Arrange.
        // The trap this test exists for: `elements: []` already carries a value, and appending a
        // block entry after it without opening the section leaves the key with two values and
        // the file unparseable - a bug that would surface on the very first Add of every new
        // graph.
        var document = LineDocument.Parse(_factory.CreateEmptyDocument("services"));
        var model = DependencyGraphParser.Parse(document);

        // Act.
        DependencyGraphWriter.InsertElement(document, model, "first001", "First", 120, 0);

        // Assert.
        var reparsed = DependencyGraphParser.Parse(LineDocument.Parse(document.Text));
        var element = Assert.Single(reparsed.Elements);
        Assert.Equal("First", element.Label);
        Assert.DoesNotContain("[]", document.Text, StringComparison.Ordinal);
    }
}
