using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Diagram;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

public class DiagramFileRouterTests : IDisposable
{
    private static readonly DiagramDefinition Mindmap = new(new DiagramOrigin("freeplane", "mindmap"), "Mind map", ".mm");
    private static readonly DiagramDefinition ClassDiagram = new(new DiagramOrigin("uml", "class"), "Class diagram");

    private readonly string _root;

    public DiagramFileRouterTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Write(string name, string content)
    {
        var path = IoPath.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static DiagramFileRouter Router(params DiagramDefinition[] definitions) => new(new Catalog(definitions));

    [Fact]
    public void Route_ARegistrationFile_ByItsFirstLine()
    {
        var adp = Write("domain.adp", "freeplane/mindmap\n");

        var routed = Assert.IsType<DiagramRouting.Routed>(Router(Mindmap, ClassDiagram).Route(adp));

        Assert.Same(Mindmap, routed.Definition);
        Assert.Equal(adp, routed.RegistrationPath);
        Assert.Equal(IoPath.Combine(_root, "domain.mm"), routed.BodyPath);
    }

    [Fact]
    public void Route_ARegistrationOfATypeWithNoSibling_BodyIsTheRegistrationItself()
    {
        var adp = Write("classes.adp", "uml/class\n");

        var routed = Assert.IsType<DiagramRouting.Routed>(Router(ClassDiagram).Route(adp));

        Assert.Equal(adp, routed.BodyPath);
    }

    [Fact]
    public void Route_ARegistrationNamingAnUnknownType_ReportsTheMimeTypeItRead()
    {
        var adp = Write("mystery.adp", "nobody/knows\n");

        var unknown = Assert.IsType<DiagramRouting.UnknownType>(Router(Mindmap).Route(adp));

        Assert.Equal("nobody/knows", unknown.MimeType);
    }

    [Fact]
    public void Route_ABareBodyFile_ByItsExtension()
    {
        // A map made in Freeplane and dropped into the folder (Requirement 2.7).
        var mm = Write("dropped.mm", "<map/>");

        var routed = Assert.IsType<DiagramRouting.Routed>(Router(Mindmap).Route(mm));

        Assert.Same(Mindmap, routed.Definition);
        Assert.Null(routed.RegistrationPath);
        Assert.Equal(mm, routed.BodyPath);
        Assert.False(File.Exists(IoPath.Combine(_root, "dropped.adp")), "a registration file was created implicitly");
    }

    [Fact]
    public void Route_ABodyFileWithARegistrationBesideIt_TheRegistrationWins()
    {
        // The .adp file decides, not the extension (Requirement 2.3): here the registration
        // says the body is a class diagram even though its extension says mindmap.
        var classAsMm = new DiagramDefinition(new DiagramOrigin("uml", "class"), "Class diagram", ".mm");
        Write("domain.adp", "uml/class\n");
        var mm = Write("domain.mm", "<map/>");

        var routed = Assert.IsType<DiagramRouting.Routed>(Router(Mindmap, classAsMm).Route(mm));

        Assert.Same(classAsMm, routed.Definition);
    }

    [Fact]
    public void Route_AnExtensionTwoTypesClaim_RefusesToGuess()
    {
        var other = new DiagramDefinition(new DiagramOrigin("other", "mindmap"), "Another mindmap", ".mm");
        var mm = Write("which.mm", "<map/>");

        var ambiguous = Assert.IsType<DiagramRouting.Ambiguous>(Router(Mindmap, other).Route(mm));

        Assert.Equal(".mm", ambiguous.Extension);
        Assert.Equal(2, ambiguous.Claimants.Count);
    }

    [Fact]
    public void Route_AnExtensionTwoTypesClaim_StillRoutesARegistrationFile()
    {
        // Requirement 2.8: the ambiguity disables extension routing, not the .adp route.
        var other = new DiagramDefinition(new DiagramOrigin("other", "mindmap"), "Another mindmap", ".mm");
        var adp = Write("domain.adp", "freeplane/mindmap\n");

        var routed = Assert.IsType<DiagramRouting.Routed>(Router(Mindmap, other).Route(adp));

        Assert.Same(Mindmap, routed.Definition);
    }

    [Fact]
    public void AmbiguousExtensions_NamesEveryExtensionClaimedTwice()
    {
        var other = new DiagramDefinition(new DiagramOrigin("other", "mindmap"), "Another mindmap", ".mm");

        Assert.Equal([".mm"], Router(Mindmap, other, ClassDiagram).AmbiguousExtensions());
        Assert.Empty(Router(Mindmap, ClassDiagram).AmbiguousExtensions());
    }

    [Fact]
    public void Route_AFileNoTypeClaims_IsNotADiagram()
    {
        var txt = Write("notes.txt", "hello");

        Assert.IsType<DiagramRouting.NotADiagram>(Router(Mindmap).Route(txt));
    }

    private sealed class Catalog(IReadOnlyList<DiagramDefinition> definitions) : IDiagramDefinitionCatalog
    {
        public IReadOnlyList<DiagramDefinition> All { get; } = definitions;
    }
}
