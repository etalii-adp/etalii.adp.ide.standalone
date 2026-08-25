using Xunit;

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// The seven notations this one module declares. They used to be seven projects with a test
/// class each, which said the same three things seven times over; here the repetition becomes
/// the theory data it always was.
/// </summary>
public class DiagramTests
{
    /// <summary>Origin key, title, and whether the type keeps its body in a `.dsl` sibling.</summary>
    public static TheoryData<string, string, bool> EveryType() => new()
    {
        { "c4/context", "System Context", true },
        { "c4/container", "Container", true },
        { "c4/component", "Component", true },
        // The one type with no document of its own: C4 discourages drawing this level, ADP
        // draws no canvas for it, and claiming `.dsl` would put it in the running for every
        // bare `.dsl` file in a project (c4-diagrams Requirement 11).
        { "c4/code", "Code (optional)", false },
        { "c4/system-landscape", "System Landscape (supplementary)", true },
        { "c4/dynamic", "Dynamic (supplementary)", true },
        { "c4/deployment", "Deployment (supplementary)", true },
    };

    [Theory]
    [MemberData(nameof(EveryType))]
    public void EveryCatalogedType_IsDeclared_WithItsTitleAndItsBodysHome(string key, string title, bool hasSibling)
    {
        // Act.
        var definition = Assert.Single(Diagram.Definitions, candidate => candidate.Origin.Key == key);

        // Assert.
        Assert.Equal(title, definition.Title);
        Assert.Equal(hasSibling, definition.HasDocumentSibling);
        Assert.Equal(hasSibling ? ".dsl" : "", definition.Extension);
    }

    [Fact]
    public void TheSevenTypes_AreTheWholeOfWhatThisModuleDeclares()
    {
        // Act and assert.
        // Seven and only seven: an eighth added without a row in EveryType would pass every
        // theory case above and still be untested, which is exactly what this catches.
        Assert.Equal(
            EveryType().Select(row => row.Data.Item1).Order(StringComparer.Ordinal),
            Diagram.Definitions.Select(definition => definition.Origin.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EveryTypeIsVendoredToC4_WithNoSubtype()
    {
        // Act and assert.
        Assert.All(Diagram.Definitions, definition =>
        {
            Assert.Equal("c4", definition.Origin.Vendor);
            Assert.Equal("", definition.Origin.Subtype);
        });
    }

    [Fact]
    public void TheSixTypesWithACanvas_AllShareTheOneDocumentExtension()
    {
        // Act and assert.
        // The point of the family: several diagrams over one model document, so a rename in
        // one view is a rename in all of them (c4-diagrams Requirement 2.2).
        var withBodies = Diagram.Definitions.Where(definition => definition.HasDocumentSibling).ToArray();

        Assert.Equal(6, withBodies.Length);
        Assert.All(withBodies, definition => Assert.Equal(Diagram.DocumentExtension, definition.Extension));
    }

    [Fact]
    public void EveryTypeDescribesItself_WithoutRestatingItsTitle()
    {
        // Act and assert.
        // The seven descriptions came from the seven modules this one replaced; a lossy
        // consolidation would show up here rather than in the Add dialog.
        Assert.All(Diagram.Definitions, definition =>
        {
            Assert.NotEmpty(definition.Description);
            Assert.NotEqual(definition.Title, definition.Description);
        });
    }
}
