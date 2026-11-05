using System.Text.Json;
using Xunit;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The module's binding claims what the four readings declare: the origins in the order they are offered, the one
/// reading a bare file opens as, and the markers that suggest each sibling (rdf-disl-fbl finding B12).
/// </summary>
public class RdfBindingTests
{
    private static JsonElement Claims => typeof(Diagram).Assembly.ResourceJson("w3c-turtle.fbl").GetProperty("bindings").GetProperty("turtle").GetProperty("claims");

    [Fact]
    public void TheBindingsOrigins_AreTheReadingsInTheOrderTheyAreOffered()
    {
        // Act.
        var origins = Claims.GetProperty("origins").EnumerateArray().Select(origin => origin.GetString());

        // Assert.
        Assert.Equal(Diagram.Definitions.Select(definition => definition.Origin.Key), origins);
    }

    [Fact]
    public void OnlyTheRdfGraph_OpensABareFile()
    {
        // Act.
        var bare = Claims.GetProperty("readings").EnumerateObject()
            .Where(reading => reading.Value.TryGetProperty("bare", out var flag) && flag.GetBoolean())
            .Select(reading => reading.Name);

        // Assert.
        Assert.Equal([Diagram.Rdf.Origin.Key], bare);
        Assert.False(Diagram.Rdf.SharedExtension);
        Assert.Null(Diagram.Rdf.SuggestsBody);
        Assert.All(Diagram.Definitions.Skip(1), sibling => Assert.True(sibling.SharedExtension));
    }

    [Theory]
    [InlineData("w3c/owl")]
    [InlineData("w3c/skos")]
    [InlineData("w3c/shacl")]
    public void EverySiblingsMarker_IsTheBindingsSuggestion_AndNoOther(string origin)
    {
        // Arrange.
        var sibling = Assert.Single(Diagram.Definitions, definition => definition.Origin.Key == origin);
        var markers = Claims.GetProperty("readings").GetProperty(origin).GetProperty("suggest").GetProperty("contains").EnumerateArray().Select(marker => marker.GetString()!).ToList();
        var others = Diagram.Definitions.Where(definition => definition.Origin.Key != origin && definition.SuggestsBody is not null)
            .SelectMany(definition => Claims.GetProperty("readings").GetProperty(definition.Origin.Key).GetProperty("suggest").GetProperty("contains").EnumerateArray().Select(marker => marker.GetString()!));

        // Act and assert: each marker of the binding suggests the reading alone, and a body holding none of its markers does not.
        Assert.All(markers, marker => Assert.True(sibling.SuggestsBody!(marker), marker));
        Assert.All(others.Where(other => !markers.Any(other.Contains)), other => Assert.False(sibling.SuggestsBody!(other), other));
        Assert.False(sibling.SuggestsBody!("ex:a ex:b ex:c ."));
    }
}
