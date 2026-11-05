using EtAlii.Adp.Designer;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Editor;
using Xunit;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// The envelope decides (spec 029 FR-002, R3): a <c>.md</c> whose first line is a designer's envelope is that designer's
/// document, with or without a registration, and whatever another tool's registration says of it; a <c>.md</c> without
/// it routes as it always has.
/// </summary>
public class EnvelopeRoutingTests : IDisposable
{
    /// <summary>A diagram type that claims a shared <c>.md</c> by its registration, as Agent Behavior Modelling does.</summary>
    private static readonly DiagramDefinition Behaviour =
        new(new ToolOrigin("fixture", "behaviour"), "Fixture behaviour", Extension: ".md", SharedExtension: true);

    private static readonly EditorDefinition Plain = new("plain", "Plain Text", IsFallback: true);
    private static readonly EditorDefinition Markdown = new("markdown", "Markdown", Extensions: [".md"]);

    private readonly TestFolder _folder = TestFolder.Create("envelope-routing");
    private readonly ToolRouter _router;

    public EnvelopeRoutingTests()
    {
        Directory.CreateDirectory(Root);
        _router = new ToolRouter(
            new DiagramFileRouter(new TestDiagramDefinitionCatalog(Behaviour)),
            new DesignerFileRouter(new DesignerDefinitionCatalog { All = [RequirementsDesigner.Definition] }),
            new EditorResolver(new EditorDefinitionCatalog { All = [Plain, Markdown] }));
    }

    private string Root => _folder.Combine("project");

    public void Dispose() => _folder.Dispose();

    private string Write(string name, string content)
    {
        var path = Path.Combine(Root, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void AnEnvelopedBodyWithoutARegistration_IsTheDesigners_WithNoRegistration()
    {
        // Arrange.
        var path = Write("plan.md", RequirementsDesigner.Body());

        // Act.
        var routing = _router.Route(path, Root);

        // Assert.
        var routed = Assert.IsType<DesignerRouted>(routing);
        Assert.Equal(RequirementsDesigner.Definition, routed.Definition);
        Assert.Null(routed.RegistrationPath);
        Assert.Equal(path, routed.BodyPath);
    }

    [Fact]
    public void AnEnvelopedBodyWithItsOwnRegistration_IsTheDesigners_WithThatRegistration()
    {
        // Arrange.
        var path = Write("plan.md", RequirementsDesigner.Body());
        var registration = Write("plan.adp", RequirementsDesigner.Origin + "\r\n");

        // Act.
        var routing = _router.Route(path, Root);

        // Assert.
        var routed = Assert.IsType<DesignerRouted>(routing);
        Assert.Equal(registration, routed.RegistrationPath);
        Assert.Equal(path, routed.BodyPath);
    }

    [Fact]
    public void AnEnvelopedBodyThatAnotherToolsRegistrationNames_IsTheDesigners_AndThatRegistrationIsUnreadable()
    {
        // Arrange: a diagram's registration naming the body, as one made before the envelope was written would.
        var path = Write("plan.md", RequirementsDesigner.Body());
        var registration = Write("plan.adp", Behaviour.Origin.MimeType + "\r\n");

        // Act.
        var body = _router.Route(path, Root);
        var other = _router.Route(registration, Root);

        // Assert: the envelope decides the body; the other registration is refused, saying why.
        var routed = Assert.IsType<DesignerRouted>(body);
        Assert.Equal(RequirementsDesigner.Definition, routed.Definition);
        Assert.Null(routed.RegistrationPath);
        var refused = Assert.IsType<ToolUnreadable>(other);
        Assert.Equal(registration, refused.Path);
        Assert.Contains(RequirementsDesigner.Definition.Title, refused.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AMarkdownFileWithoutTheEnvelope_RoutesAsItAlwaysHas()
    {
        // Arrange: the envelope on line 2 is no envelope.
        var notes = Write("notes.md", "# Notes\r\n" + RequirementsDesigner.Envelope + "\r\n");
        var drawn = Write("drawn.md", "# Behaviour\r\n");
        var registration = Write("drawn.adp", Behaviour.Origin.MimeType + "\r\n");

        // Act and assert.
        Assert.Equal(Markdown, Assert.IsType<EditorRouted>(_router.Route(notes, Root)).Definition);
        Assert.Equal(Behaviour, Assert.IsType<DiagramRouted>(_router.Route(drawn, Root)).Definition);
        Assert.Equal(Behaviour, Assert.IsType<DiagramRouted>(_router.Route(registration, Root)).Definition);
    }

    [Fact]
    public void TheFirstLine_IsReadUpToItsLineBreakOrALimit()
    {
        // Arrange.
        var cache = new FirstLineCache();
        var shortLine = Write("short.md", RequirementsDesigner.Envelope + "\r\nmore\r\n");
        var longLine = Write("long.md", new string('x', 3000) + "\n");

        // Act and assert.
        Assert.Equal(RequirementsDesigner.Envelope, System.Text.Encoding.UTF8.GetString(cache.Read(shortLine)));
        Assert.Equal(FirstLineCache.Limit, cache.Read(longLine).Length);
    }

    [Fact]
    public void TheFirstLine_IsKeptUntilTheFileChanges()
    {
        // Arrange: a first read, then other bytes of the same length written under the same time.
        var cache = new FirstLineCache();
        var path = Write("plan.md", "AAAA\n");
        var written = File.GetLastWriteTimeUtc(path);
        Assert.Equal("AAAA"u8.ToArray(), cache.Read(path));
        File.WriteAllText(path, "BBBB\n");
        File.SetLastWriteTimeUtc(path, written);

        // Act and assert: unchanged by length and time, the line is the one kept.
        Assert.Equal("AAAA"u8.ToArray(), cache.Read(path));

        // Act and assert: a change of length is a change.
        File.WriteAllText(path, "CCCCC\n");
        File.SetLastWriteTimeUtc(path, written);
        Assert.Equal("CCCCC"u8.ToArray(), cache.Read(path));
    }
}
