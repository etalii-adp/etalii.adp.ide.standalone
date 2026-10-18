using EtAlii.Adp.Specification.Fbl;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// Several definitions bundled for one module, found by origin, each answering for its own reading of
/// one body: two test definitions of one metamodel, which differ in their toolbox and their menu.
/// </summary>
public class BundledDefinitionSetTests
{
    private const string Wide = "example/wide";

    private const string Narrow = "example/narrow";

    private static BundledDefinitionSet Set() => BundledDefinitionSet.Load(Resources.Assembly, DislPluginFunctions.None, Resources.WideReading, Resources.NarrowReading);

    /// <summary>One body as a reader gives it: a box and a note.</summary>
    private static FblModel Body() => new(
    [
        new FblElement("b1", true, "Box", "box", false, new Dictionary<string, object?> { ["name"] = "First" }, null, null, null, null, new Span(0, 0), 1),
        new FblElement("n1", true, "Note", "note", false, new Dictionary<string, object?> { ["text"] = "Remember" }, null, null, null, null, new Span(0, 0), 2),
    ], [], false);

    [Theory]
    [InlineData(Wide, "Box|Note", "Remove box")]
    [InlineData(Narrow, "Sticky note", "Rename")]
    public void TwoDefinitionsOverOneBody_EachAnswerWithTheirOwnToolboxAndMenu(string origin, string tools, string menu)
    {
        // Arrange.
        var specification = Set().For(origin).Specification;
        var diagram = DislModelBuilder.From(Body(), specification).Diagram;
        var ids = WireIdMap.None;

        // Act.
        var toolbox = ToolboxDerivation.Derive(specification, ids);
        var groups = ContextMenuDerivation.Derive(specification, DislMenuTarget.Element(diagram.ElementById("b1")!), new DislEnv(), ids);

        // Assert.
        Assert.Equal(tools, string.Join("|", toolbox.Select(tool => tool.Label)));
        Assert.Equal(menu, string.Join("|", groups.SelectMany(group => group.Entries).Select(entry => entry.Label)));
    }

    [Fact]
    public void TheSet_KeepsTheOrderItWasGiven_WithEachDefinitionsProvenance()
    {
        // Act.
        var set = Set();

        // Assert.
        Assert.Equal([Wide, Narrow], set.Origins);
        Assert.All(set.Definitions, definition => Assert.Empty(definition.Diagnostics));
        Assert.Equal(
            ["src/backend/EtAlii.Adp.Specification.Disl.Tests/Definitions/wide-reading.dis", "src/backend/EtAlii.Adp.Specification.Disl.Tests/Definitions/narrow-reading.dis"],
            set.Definitions.Select(definition => definition.Provenance.Path));
        Assert.Same(set.Definitions[1], set.For(Narrow));
    }

    [Fact]
    public void AnOriginTheSetLacks_IsFoundAsNothing_AndRefusedByFor()
    {
        // Arrange.
        var set = Set();

        // Act.
        var exception = Assert.Throws<KeyNotFoundException>(() => set.For("example/other"));

        // Assert.
        Assert.Null(set.Find("example/other"));
        Assert.Null(set.Find("EXAMPLE/WIDE"));
        Assert.Equal("No bundled definition has the origin example/other; the set has example/wide, example/narrow.", exception.Message);
    }

    [Fact]
    public void TwoDefinitionsOfOneOrigin_AreRefused()
    {
        // Arrange.
        var wide = BundledDefinition.Load(Resources.Assembly, Resources.WideReading);

        // Act.
        var exception = Assert.Throws<InvalidOperationException>(() => BundledDefinitionSet.Of(wide, wide));

        // Assert.
        Assert.Equal(
            "src/backend/EtAlii.Adp.Specification.Disl.Tests/Definitions/wide-reading.dis and src/backend/EtAlii.Adp.Specification.Disl.Tests/Definitions/wide-reading.dis both declare the origin example/wide.",
            exception.Message);
    }

    [Fact]
    public void ADefinitionWithoutAnOrigin_IsRefused()
    {
        // Arrange.
        var specification = Specifications.Loaded(Specifications.With());
        var definition = new BundledDefinition(specification, new DefinitionProvenance("r", "plain.dis", "v", "s"), []);

        // Act.
        var exception = Assert.Throws<InvalidOperationException>(() => BundledDefinitionSet.Of(definition));

        // Assert.
        Assert.Equal("plain.dis declares no language.origin, so no reading can name it.", exception.Message);
    }

    [Fact]
    public void TheExtensionPropertyAnOriginSupersedes_IsReadWhereNoOriginIsDeclared_AndLosesToOne()
    {
        // Act.
        var extension = Specifications.Loaded(Specifications.With("""
            "language": { "id": "org.example.test", "version": "1.0.0", "x-adp-origin": "example/older" }
            """));
        var both = Specifications.Loaded(Specifications.With("""
            "language": { "id": "org.example.test", "version": "1.0.0", "origin": "example/newer", "x-adp-origin": "example/older" }
            """));

        // Assert.
        Assert.Equal("example/older", extension.Origin);
        Assert.Equal("example/newer", both.Origin);
    }
}
