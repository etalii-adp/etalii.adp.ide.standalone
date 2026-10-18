using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// A canvas's title, header band and legend (DISL §6.13), derived for what a host draws: the title a
/// diagram attribute it binds, the rows and the legend over <c>diagram.drawn</c>.
/// </summary>
public class CanvasChromeDerivationTests
{
    private const string Metamodel = """
        "disl": "0.3",
        "metamodel": {
          "diagram": { "attributes": {
            "caption": { "type": "string", "default": "" },
            "heading": { "type": "string", "derived": "self.caption != '' ? self.caption : 'Untitled ' + env.viewpoint" } } },
          "enums": { "Tone": { "values": { "warm": { "label": "Warm" }, "cool": { "label": "Cool" } } } },
          "types": {
            "Square": { "label": "Square", "attributes": { "outside": { "type": "bool", "default": false }, "tone": { "type": "Tone" } } },
            "Circle": { "label": "Circle", "attributes": { "outside": { "type": "bool", "default": false } } } } },
        "viewpoints": {
          "all": { "label": "All" },
          "plain": { "label": "Plain", "canvas": { "title": { "text": "Plain view" } } } }
        """;

    private static DislSpecification Specification(string legend = """
        "legend": { "visible": true, "position": "outside-bottom", "title": "Key", "from": "drawn",
          "computed": { "key": "self.type + (self.outside ? ' (outside)' : '')", "label": { "cel": "self.type + (self.outside ? ' outside' : '') + ' shape'" } } }
        """) => Specifications.Loaded(Specifications.With(Metamodel + $$"""
        ,
        "notation": { "canvas": {
          "title": { "id": "heading", "text": { "attribute": "heading" } },
          "header": { "rows": [
            { "text": { "cel": "string(diagram.drawn.size()) + ' of ' + string(diagram.nodes.size()) + ' drawn'" } },
            { "text": "Never shown", "visible": "diagram.drawn.size() > 100" },
            { "text": "Always shown" } ] },
          {{legend}} } }
        """));

    /// <summary>Four shapes in model order: a square, a circle, a square outside, a square.</summary>
    private static DislDiagram Diagram(DislSpecification specification, string caption = "", string viewpoint = "all")
    {
        var diagram = new DislDiagram(specification, new Dictionary<string, object?> { ["caption"] = caption }) { Env = new DislEnv(Viewpoint: viewpoint) };
        diagram.AddNode("Square", "s1", new Dictionary<string, object?> { ["tone"] = "warm" });
        diagram.AddNode("Circle", "c1");
        diagram.AddNode("Square", "s2", new Dictionary<string, object?> { ["outside"] = true });
        diagram.AddNode("Square", "s3");
        return diagram;
    }

    /// <summary>The drawn elements, given in an order other than the model's.</summary>
    private static List<DislElement> Drawn(DislDiagram diagram, params string[] ids) => [.. ids.Select(id => diagram.ElementById(id)!)];

    private static List<string> Lines(DerivedLegend legend) => [.. legend.Entries.Select(entry => $"{entry.Key} | {entry.Label} | {entry.First?.Id ?? "-"}")];

    [Theory]
    [InlineData("Declared caption", "Declared caption")]
    [InlineData("", "Untitled all")]
    public void TheTitle_IsTheDiagramAttributeItBinds(string caption, string title)
    {
        // Arrange.
        var diagram = Diagram(Specification(), caption);

        // Act.
        var chrome = CanvasChromeDerivation.Derive(diagram, diagram.Nodes, diagram.Env);

        // Assert.
        Assert.Equal(title, chrome.Title);
    }

    [Fact]
    public void TheHeaderRows_ReadWhatIsDrawn_AndARowThatIsNotVisibleIsLeftOut()
    {
        // Arrange.
        var diagram = Diagram(Specification());

        // Act.
        var chrome = CanvasChromeDerivation.Derive(diagram, Drawn(diagram, "s3", "c1"), diagram.Env);

        // Assert.
        Assert.Equal(["2 of 4 drawn", "Always shown"], chrome.Header);
    }

    [Fact]
    public void AComputedLegend_HasOneEntryPerKey_InModelOrder_LabelledOnTheFirstElementOfEach()
    {
        // Arrange.
        var diagram = Diagram(Specification());

        // Act.
        var legend = CanvasChromeDerivation.Derive(diagram, Drawn(diagram, "s3", "s2", "c1", "s1"), diagram.Env).Legend!;

        // Assert.
        Assert.Equal("Key", legend.Title);
        Assert.Equal("outside-bottom", legend.Position);
        Assert.Equal(
        [
            "Square | Square shape | s1",
            "Circle | Circle shape | c1",
            "Square (outside) | Square outside shape | s2",
        ], Lines(legend));
    }

    [Fact]
    public void AComputedLegend_CountsWhatIsDrawn_AndNothingElse()
    {
        // Arrange.
        var diagram = Diagram(Specification());

        // Act.
        var legend = CanvasChromeDerivation.Derive(diagram, Drawn(diagram, "s3", "s1"), diagram.Env).Legend!;

        // Assert.
        Assert.Equal(["Square | Square shape | s1"], Lines(legend));
    }

    [Fact]
    public void AComputedLegendsOrder_SortsItsEntries()
    {
        // Arrange.
        var diagram = Diagram(Specification("""
            "legend": { "computed": { "key": "self.type", "order": "self.type == 'Circle' ? 0 : 1" } }
            """));

        // Act.
        var legend = CanvasChromeDerivation.Derive(diagram, diagram.Nodes, diagram.Env).Legend!;

        // Assert.
        Assert.Equal(["Circle | Circle | c1", "Square | Square | s1"], Lines(legend));
    }

    [Theory]
    [InlineData("declared", "Circle | Circle | -", "warm | Warm | s1", "cool | Cool | -")]
    [InlineData("drawn", "warm | Warm | s1")]
    public void DeclaredEntries_FromWhatIsDrawn_AreThoseADrawnElementMatchesByTypeOrEnumValue(string from, params string[] lines)
    {
        // Arrange.
        var diagram = Diagram(Specification($$"""
            "legend": { "from": "{{from}}", "entries": ["Circle", "warm", "cool"] }
            """));

        // Act.
        var legend = CanvasChromeDerivation.Derive(diagram, Drawn(diagram, "s1", "s3"), diagram.Env).Legend!;

        // Assert.
        Assert.Equal(lines, Lines(legend));
    }

    [Fact]
    public void AViewpointsOwnCanvas_ReplacesTheTitle_AndKeepsTheNotationsLegend()
    {
        // Arrange.
        var diagram = Diagram(Specification(), viewpoint: "plain");

        // Act.
        var chrome = CanvasChromeDerivation.Derive(diagram, diagram.Nodes, diagram.Env);

        // Assert.
        Assert.Equal("Plain view", chrome.Title);
        Assert.Equal("Key", chrome.Legend?.Title);
    }

    [Fact]
    public void ALegendThatIsNotVisible_IsNone()
    {
        // Arrange.
        var diagram = Diagram(Specification("""
            "legend": { "visible": false, "title": "Key", "computed": { "key": "self.type" } }
            """));

        // Act.
        var chrome = CanvasChromeDerivation.Derive(diagram, diagram.Nodes, diagram.Env);

        // Assert.
        Assert.Null(chrome.Legend);
    }

    [Fact]
    public void ACanvasWithoutChrome_AnswersNone()
    {
        // Arrange.
        var diagram = new DislDiagram(Specifications.Loaded(Specifications.With()));

        // Act.
        var chrome = CanvasChromeDerivation.Derive(diagram, diagram.Nodes, diagram.Env);

        // Assert.
        Assert.Null(chrome.Title);
        Assert.Empty(chrome.Header);
        Assert.Null(chrome.Legend);
    }
}
