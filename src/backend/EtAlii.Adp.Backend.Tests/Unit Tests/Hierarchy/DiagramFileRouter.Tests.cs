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

    // ---- one document format, several types of one vendor (c4-diagrams Requirements 2.4-2.6) ----

    private static readonly DiagramDefinition C4Context = new(new DiagramOrigin("c4", "context"), "System Context", ".dsl");
    private static readonly DiagramDefinition C4Container = new(new DiagramOrigin("c4", "container"), "Container", ".dsl");
    private static readonly DiagramDefinition RivalDsl = new(new DiagramOrigin("other", "thing"), "Rival", ".dsl");

    [Fact]
    public void Route_ARegistrationNamingASharedBody_ResolvesToThatBody()
    {
        Directory.CreateDirectory(IoPath.Combine(_root, "shared"));
        Write(IoPath.Combine("shared", "model.dsl"), "workspace {}");
        var adp = Write("containers.adp", "c4/container\nbody: shared/model.dsl\nview: containers\n");

        var routing = Router(C4Context, C4Container).Route(adp, _root);

        var routed = Assert.IsType<DiagramRouting.Routed>(routing);
        Assert.Equal(IoPath.Combine(_root, "shared", "model.dsl"), routed.BodyPath);
        Assert.Equal("c4/container", routed.Definition.Origin.Key);
    }

    [Fact]
    public void Route_ARegistrationWithNoBodyHeader_StillUsesItsDerivedSibling()
    {
        var adp = Write("solo.adp", "c4/context\n");

        var routed = Assert.IsType<DiagramRouting.Routed>(Router(C4Context).Route(adp, _root));

        Assert.Equal(IoPath.Combine(_root, "solo.dsl"), routed.BodyPath);
    }

    [Fact]
    public void Route_ABodyHeaderEscapingTheProject_IsRefused()
    {
        var adp = Write("escape.adp", "c4/context\nbody: ../outside.dsl\n");

        Assert.IsType<DiagramRouting.Unreadable>(Router(C4Context).Route(adp, _root));
    }

    [Fact]
    public void Route_ABareBodyClaimedByOneVendorsFamily_RoutesToThatFamily()
    {
        // Seven C4 types share .dsl by design: which one a document is depends on the view it
        // declares, which only the module can read. Refusing to route would make Requirement
        // 2.6's "openable without an .adp" impossible.
        var dsl = Write("model.dsl", "workspace {}");

        var routed = Assert.IsType<DiagramRouting.Routed>(Router(C4Context, C4Container).Route(dsl));

        Assert.Equal("c4", routed.Definition.Origin.Vendor);
        Assert.Null(routed.RegistrationPath);
        Assert.Equal(dsl, routed.BodyPath);
    }

    [Fact]
    public void Route_ABareBodyClaimedByTwoVendors_IsStillAmbiguous()
    {
        // The guard still guards: unrelated modules claiming one extension cannot be resolved
        // by reading the document, because neither owns it.
        var dsl = Write("model.dsl", "workspace {}");

        var ambiguous = Assert.IsType<DiagramRouting.Ambiguous>(Router(C4Context, RivalDsl).Route(dsl));

        Assert.Equal(".dsl", ambiguous.Extension);
    }

    [Fact]
    public void AmbiguousExtensions_DoesNotReportOneVendorsFamily_ButStillReportsRivalVendors()
    {
        Assert.Empty(Router(C4Context, C4Container).AmbiguousExtensions());
        Assert.Equal([".dsl"], Router(C4Context, RivalDsl).AmbiguousExtensions());
    }

    private sealed class Catalog(IReadOnlyList<DiagramDefinition> definitions) : IDiagramDefinitionCatalog
    {
        public IReadOnlyList<DiagramDefinition> All { get; } = definitions;
    }
}
