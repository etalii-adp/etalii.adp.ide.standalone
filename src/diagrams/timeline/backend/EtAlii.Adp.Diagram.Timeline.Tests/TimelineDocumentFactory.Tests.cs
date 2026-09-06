using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Common;
using Xunit;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// The document a new timeline starts as, and - more importantly - what happens to it next.
/// </summary>
public class TimelineDocumentFactoryTests
{
    private readonly TimelineDocumentFactory _factory = new();

    [Fact]
    public void ANewDocument_IsTheSchemaMarkerAndAnEmptyListAndNothingElse()
    {
        // Act.
        var text = _factory.CreateEmptyDocument("plan");

        // Assert.
        var model = TimelineParser.Parse(LineDocument.Parse(text));
        Assert.Empty(model.Elements);
        Assert.Empty(model.Connections);
        Assert.StartsWith("timeline: 1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("plan", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ItAnswersForThisDiagramType()
    {
        // Assert.
        // Resolved by origin; a mismatch is an Add that fails for this type with no error
        // pointing here.
        Assert.Equal(Diagram.Timeline.Origin, _factory.Origin);
    }

    [Fact]
    public void TheFirstElementAddedToAFreshDocument_ProducesValidYaml()
    {
        // Arrange.
        // The trap this test exists for: `elements: []` already carries a value, and appending a
        // block entry after it without opening the section leaves the key with two values and
        // the file unparseable - a bug that would surface on the very first Add of every new
        // timeline.
        var document = LineDocument.Parse(_factory.CreateEmptyDocument("plan"));
        var model = TimelineParser.Parse(document);

        // Act.
        TimelineWriter.InsertElement(document, model, "first001", "First", "2026-01-01", "2026-01-31", 0);

        // Assert.
        var reparsed = TimelineParser.Parse(LineDocument.Parse(document.Text));
        var element = Assert.Single(reparsed.Elements);
        Assert.Equal("First", element.Label);
        Assert.DoesNotContain("[]", document.Text, StringComparison.Ordinal);
    }
}
