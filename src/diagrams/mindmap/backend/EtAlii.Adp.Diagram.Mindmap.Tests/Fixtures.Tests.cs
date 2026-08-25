using System.Xml.Linq;
using Xunit;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

/// <summary>
/// Guards the test corpus itself, before anything parses it.
/// </summary>
/// <remarks>
/// Two things can go wrong long before the parser exists and would otherwise be found as a
/// confusing parser failure: the fixture stops being copied beside the test binary, or someone
/// edits it into something that is no longer the thing it was chosen to be. These assert the
/// features Fixtures/readme.md says the file carries, so a fixture quietly simplified over time
/// fails here rather than silently weakening the round-trip tests that depend on it.
/// </remarks>
public class FixturesTests
{
    private const string Map = "Fixtures/architecture.mm";

    private static XDocument Load() => XDocument.Load(Map);

    /// <summary>All `node` elements, which excludes Freeplane's `stylenode` inside the style hook.</summary>
    private static IReadOnlyList<XElement> Nodes(XDocument document) =>
        [.. document.Descendants("node")];

    [Fact]
    public void TheMap_IsCopiedBesideTheTestBinary()
    {
        // Arrange, act and assert.
        // If this fails the csproj's Content item is gone, and every fixture-based test below
        // would fail with a file-not-found that says nothing about the cause.
        Assert.True(File.Exists(Map), $"{Map} was not copied to the test output directory");
    }

    [Fact]
    public void TheMap_IsWellFormedFreeplane()
    {
        // Act.
        var document = Load();

        // Assert.
        Assert.Equal("map", document.Root!.Name.LocalName);
        Assert.StartsWith("freeplane", document.Root.Attribute("version")!.Value, StringComparison.Ordinal);
        Assert.Single(document.Root.Elements("node")); // exactly one root node
    }

    [Fact]
    public void TheMap_StaysComplexEnoughToBeWorthTesting()
    {
        // Act.
        var nodes = Nodes(Load());

        // Assert.
        Assert.True(nodes.Count >= 20, $"the corpus has shrunk to {nodes.Count} nodes");
        // A genuine save always carries IDs: Freeplane assigns one to every node it writes,
        // which is why the corpus - now Freeplane's own output - can never contain an ID-less
        // node. That tolerance is exercised by the hand-written map inside
        // MindmapDocumentTests.AssigningMissingIds instead, where it belongs: an ID-less node
        // only ever occurs in a file no Freeplane has saved yet.
        Assert.All(nodes, node => Assert.NotNull(node.Attribute("ID")));
        Assert.Contains(nodes, node => node.Attribute("FOLDED")?.Value == "true");
        Assert.Contains(nodes, node => node.Attribute("TEXT")?.Value == "");
        Assert.Contains(nodes, node => node.Attribute("LINK") is not null);
    }

    [Fact]
    public void TheMap_CarriesLinksInBothFormsRequirement12Names()
    {
        // Arrange and act.
        var links = Nodes(Load())
            .Select(node => node.Attribute("LINK")?.Value)
            .Where(link => link is not null)
            .ToList();

        // Assert.
        Assert.Contains(links, link => link!.StartsWith("..", StringComparison.Ordinal));
        Assert.Contains(links, link => link!.StartsWith("https://", StringComparison.Ordinal));
    }

    [Fact]
    public void TheMap_CarriesElementsAdpDoesNotUnderstand()
    {
        // Arrange.
        // Requirement 3.2: these must survive a round trip untouched. A fixture without them
        // cannot prove that, so their absence is a fixture bug.
        var document = Load();

        // Act and assert, step by step.
        foreach (var name in new[] { "hook", "map_styles", "stylenode", "font", "edge", "icon", "cloud", "arrowlink", "attribute" })
        {
            Assert.True(document.Descendants(name).Any(), $"the corpus no longer contains a '{name}' element");
        }
    }

    [Fact]
    public void TheMap_CarriesRichContentForBothANoteAndANodesText()
    {
        // Arrange and act.
        var kinds = Load().Descendants("richcontent")
            .Select(element => element.Attribute("TYPE")?.Value)
            .ToList();

        // Assert.
        Assert.Contains("NOTE", kinds);
        Assert.Contains("NODE", kinds);
    }
}
