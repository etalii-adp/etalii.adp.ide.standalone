using EtAlii.Adp.Designer;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Editor;
using Xunit;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// The one router of the three tool kinds and its precedence - diagrams, then designers, then editors
/// (code-structure FR-058, T167) - over the three routers it combines.
/// </summary>
public class ToolRouterTests : IDisposable
{
    private static readonly DiagramDefinition Mindmap = new(new ToolOrigin("freeplane", "mindmap"), "Mind map", Extension: ".mm");

    private static readonly DesignerDefinition Sheet = new(
        "fixture/sheet",
        "Fixture sheet",
        Formats: [new DesignerFormat("YAML", ".yaml")]);

    private static readonly EditorDefinition Plain = new("plain", "Plain Text", IsFallback: true);
    private static readonly EditorDefinition Markdown = new("markdown", "Markdown", Extensions: [".md"]);
    private static readonly EditorDefinition Yaml = new("yaml", "YAML", Extensions: [".yaml"]);
    private static readonly EditorDefinition FreeMind = new("freemind", "FreeMind XML", Extensions: [".mm"]);
    private static readonly EditorDefinition DupOne = new("dup-one", "One", Extensions: [".dup"]);
    private static readonly EditorDefinition DupTwo = new("dup-two", "Two", Extensions: [".dup"]);

    private readonly TestFolder _folder = TestFolder.Create("tool-router");
    private readonly ToolRouter _router;

    public ToolRouterTests()
    {
        Directory.CreateDirectory(_folder.Combine("project"));
        _router = new ToolRouter(
            new DiagramFileRouter(new TestDiagramDefinitionCatalog(Mindmap)),
            new DesignerFileRouter(new DesignerDefinitionCatalog { All = [Sheet] }),
            new EditorResolver(new EditorDefinitionCatalog { All = [Plain, Markdown, Yaml, FreeMind, DupOne, DupTwo] }));
    }

    private string Root => _folder.Combine("project");

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void ADiagramBody_IsTheDiagrams_EvenWhereAnEditorClaimsItsExtension()
    {
        // Arrange.
        var path = Write("ideas.mm", "<map/>");

        // Act.
        var routing = _router.Route(path, Root);

        // Assert.
        Assert.Equal(Mindmap, Assert.IsType<DiagramRouted>(routing).Definition);
    }

    [Fact]
    public void ADiagramRegistration_IsTheDiagrams()
    {
        // Arrange.
        Write("ideas.mm", "<map/>");
        var path = Write("ideas.adp", Mindmap.Origin.MimeType + "\r\n");

        // Act.
        var routing = _router.Route(path, Root);

        // Assert.
        Assert.Equal(path, Assert.IsType<DiagramRouted>(routing).RegistrationPath);
    }

    [Fact]
    public void ADiagramRegistrationWhoseBodyLeadsOutOfTheProject_IsRefused_NotOfferedToTheEditors()
    {
        // Arrange.
        var path = Write("ideas.adp", Mindmap.Origin.MimeType + "\r\nbody: ../../elsewhere.mm\r\n");

        // Act.
        var routing = _router.Route(path, Root);

        // Assert.
        Assert.Equal(path, Assert.IsType<ToolUnreadable>(routing).Path);
    }

    [Fact]
    public void ADesignerRegistration_IsTheDesigners()
    {
        // Arrange.
        Write("budget.yaml", "rows: []\n");
        var path = Write("budget.adp", "fixture/sheet\r\n");

        // Act.
        var routing = _router.Route(path, Root);

        // Assert.
        Assert.Equal(Sheet, Assert.IsType<DesignerRouted>(routing).Definition);
    }

    [Fact]
    public void ADesignerBody_IsTheDesigners_EvenWhereAnEditorClaimsItsExtension()
    {
        // Arrange.
        var path = Write("budget.yaml", "rows: []\n");
        Write("budget.adp", "fixture/sheet\r\n");

        // Act.
        var routing = _router.Route(path, Root);

        // Assert.
        Assert.Equal(Sheet, Assert.IsType<DesignerRouted>(routing).Definition);
    }

    [Fact]
    public void AFileNoDiagramOrDesignerClaims_IsTheEditors()
    {
        // Arrange.
        var notes = Write("notes.md", "# Notes\n");
        var yaml = Write("settings.yaml", "a: 1\n");
        var other = Write("other.txt", "text");

        // Act and assert: the claiming editor, and the fallback for what none claims.
        Assert.Equal(Markdown, Assert.IsType<EditorRouted>(_router.Route(notes, Root)).Definition);
        Assert.Equal(Yaml, Assert.IsType<EditorRouted>(_router.Route(yaml, Root)).Definition);
        Assert.Equal(Plain, Assert.IsType<EditorRouted>(_router.Route(other, Root)).Definition);
    }

    [Fact]
    public void ARegistrationOfATypeNothingDeployedKnows_IsTheEditors()
    {
        // Arrange.
        var path = Write("strange.adp", "vendor/unknown\r\n");

        // Act and assert.
        Assert.Equal(Plain, Assert.IsType<EditorRouted>(_router.Route(path, Root)).Definition);
    }

    [Fact]
    public void AFileSeveralEditorsClaimWithoutADefault_IsNoTool()
    {
        // Arrange.
        var path = Write("thing.dup", "?");

        // Act and assert.
        Assert.IsType<NoTool>(_router.Route(path, Root));
    }

    private string Write(string name, string content)
    {
        var path = _folder.Combine("project", name);
        File.WriteAllText(path, content);
        return path;
    }
}
