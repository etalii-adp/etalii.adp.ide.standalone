using EtAlii.Adp.Common;
using EtAlii.Adp.Diagram;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy.Tests;

public class DiagramFileRouterTests : IDisposable
{
    private static readonly DiagramDefinition Mindmap = new(new DiagramOrigin("freeplane", "mindmap"), "Mind map", Extension: ".mm");
    private static readonly DiagramDefinition ClassDiagram = new(new DiagramOrigin("uml", "class"), "Class diagram");

    private readonly string _root;

    public DiagramFileRouterTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    private string Write(string name, string content)
    {
        var path = IoPath.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static DiagramFileRouter Router(params DiagramDefinition[] definitions) => new(new TestDiagramDefinitionCatalog(definitions));

    [Fact]
    public void Route_ARegistrationFile_ByItsFirstLine()
    {
        // Arrange.
        var adp = Write("domain.adp", "freeplane/mindmap\n");

        // Act.
        var routed = Assert.IsType<DiagramRouted>(Router(Mindmap, ClassDiagram).Route(adp));

        // Assert.
        Assert.Same(Mindmap, routed.Definition);
        Assert.Equal(adp, routed.RegistrationPath);
        Assert.Equal(IoPath.Combine(_root, "domain.mm"), routed.BodyPath);
    }

    [Fact]
    public void Route_ARegistrationOfATypeWithNoSibling_BodyIsTheRegistrationItself()
    {
        // Arrange.
        var adp = Write("classes.adp", "uml/class\n");

        // Act.
        var routed = Assert.IsType<DiagramRouted>(Router(ClassDiagram).Route(adp));

        // Assert.
        Assert.Equal(adp, routed.BodyPath);
    }

    [Fact]
    public void Route_ARegistrationNamingAnUnknownType_ReportsTheMimeTypeItRead()
    {
        // Arrange.
        var adp = Write("mystery.adp", "nobody/knows\n");

        // Act.
        var unknown = Assert.IsType<DiagramUnknownType>(Router(Mindmap).Route(adp));

        // Assert.
        Assert.Equal("nobody/knows", unknown.MimeType);
    }

    [Fact]
    public void Route_ABareBodyFile_ByItsExtension()
    {
        // Arrange.
        // A map made in Freeplane and dropped into the folder (Requirement 2.7).
        var mm = Write("dropped.mm", "<map/>");

        // Act.
        var routed = Assert.IsType<DiagramRouted>(Router(Mindmap).Route(mm));

        // Assert.
        Assert.Same(Mindmap, routed.Definition);
        Assert.Null(routed.RegistrationPath);
        Assert.Equal(mm, routed.BodyPath);
        Assert.False(File.Exists(IoPath.Combine(_root, "dropped.adp")), "a registration file was created implicitly");
    }

    [Fact]
    public void Route_ABodyFileWithARegistrationBesideIt_TheRegistrationWins()
    {
        // Arrange.
        // The .adp file decides, not the extension (Requirement 2.3): here the registration
        // says the body is a class diagram even though its extension says mindmap.
        var classAsMm = new DiagramDefinition(new DiagramOrigin("uml", "class"), "Class diagram", Extension: ".mm");
        Write("domain.adp", "uml/class\n");
        var mm = Write("domain.mm", "<map/>");

        // Act.
        var routed = Assert.IsType<DiagramRouted>(Router(Mindmap, classAsMm).Route(mm));

        // Assert.
        Assert.Same(classAsMm, routed.Definition);
    }

    [Fact]
    public void Route_AnExtensionTwoTypesClaim_RefusesToGuess()
    {
        // Arrange.
        var other = new DiagramDefinition(new DiagramOrigin("other", "mindmap"), "Another mindmap", Extension: ".mm");
        var mm = Write("which.mm", "<map/>");

        // Act.
        var ambiguous = Assert.IsType<DiagramAmbiguousExtension>(Router(Mindmap, other).Route(mm));

        // Assert.
        Assert.Equal(".mm", ambiguous.Extension);
        Assert.Equal(2, ambiguous.Claimants.Count);
    }

    [Fact]
    public void Route_AnExtensionTwoTypesClaim_StillRoutesARegistrationFile()
    {
        // Arrange.
        // Requirement 2.8: the ambiguity disables extension routing, not the .adp route.
        var other = new DiagramDefinition(new DiagramOrigin("other", "mindmap"), "Another mindmap", Extension: ".mm");
        var adp = Write("domain.adp", "freeplane/mindmap\n");

        // Act.
        var routed = Assert.IsType<DiagramRouted>(Router(Mindmap, other).Route(adp));

        // Assert.
        Assert.Same(Mindmap, routed.Definition);
    }

    [Fact]
    public void AmbiguousExtensions_NamesEveryExtensionClaimedTwice()
    {
        // Act.
        var other = new DiagramDefinition(new DiagramOrigin("other", "mindmap"), "Another mindmap", Extension: ".mm");

        // Assert.
        Assert.Equal([".mm"], Router(Mindmap, other, ClassDiagram).AmbiguousExtensions());
        Assert.Empty(Router(Mindmap, ClassDiagram).AmbiguousExtensions());
    }

    [Fact]
    public void Route_AFileNoTypeClaims_IsNotADiagram()
    {
        // Act.
        var txt = Write("notes.txt", "hello");

        // Assert.
        Assert.IsType<NotADiagram>(Router(Mindmap).Route(txt));
    }

    // ---- one document format, several types of one vendor (c4-diagrams Requirements 2.4-2.6) ----

    private static readonly DiagramDefinition C4Context = new(new DiagramOrigin("c4", "context"), "System Context", Extension: ".dsl");
    private static readonly DiagramDefinition C4Container = new(new DiagramOrigin("c4", "container"), "Container", Extension: ".dsl");
    private static readonly DiagramDefinition RivalDsl = new(new DiagramOrigin("other", "thing"), "Rival", Extension: ".dsl");

    [Fact]
    public void Route_ARegistrationNamingASharedBody_ResolvesToThatBody()
    {
        // Arrange.
        Directory.CreateDirectory(IoPath.Combine(_root, "shared"));
        Write(IoPath.Combine("shared", "model.dsl"), "workspace {}");
        var adp = Write("containers.adp", "c4/container\nbody: shared/model.dsl\nview: containers\n");

        var routing = Router(C4Context, C4Container).Route(adp, _root);

        // Act and assert, step by step.
        var routed = Assert.IsType<DiagramRouted>(routing);
        Assert.Equal(IoPath.Combine(_root, "shared", "model.dsl"), routed.BodyPath);
        Assert.Equal("c4/container", routed.Definition.Origin.Key);
    }

    [Fact]
    public void Route_ARegistrationWithNoBodyHeader_StillUsesItsDerivedSibling()
    {
        // Arrange.
        var adp = Write("solo.adp", "c4/context\n");

        // Act.
        var routed = Assert.IsType<DiagramRouted>(Router(C4Context).Route(adp, _root));

        // Assert.
        Assert.Equal(IoPath.Combine(_root, "solo.dsl"), routed.BodyPath);
    }

    [Fact]
    public void Route_ABodyHeaderEscapingTheProject_IsRefused()
    {
        // Act.
        var adp = Write("escape.adp", "c4/context\nbody: ../outside.dsl\n");

        // Assert.
        Assert.IsType<DiagramUnreadable>(Router(C4Context).Route(adp, _root));
    }

    [Fact]
    public void Route_ABareBodyClaimedByOneVendorsFamily_RoutesToThatFamily()
    {
        // Arrange.
        // Seven C4 types share .dsl by design: which one a document is depends on the view it
        // declares, which only the module can read. Refusing to route would make Requirement
        // 2.6's "openable without an .adp" impossible.
        var dsl = Write("model.dsl", "workspace {}");

        // Act.
        var routed = Assert.IsType<DiagramRouted>(Router(C4Context, C4Container).Route(dsl));

        // Assert.
        Assert.Equal("c4", routed.Definition.Origin.Vendor);
        Assert.Null(routed.RegistrationPath);
        Assert.Equal(dsl, routed.BodyPath);
    }

    [Fact]
    public void Route_ABareBodyClaimedByTwoVendors_IsStillAmbiguous()
    {
        // Arrange.
        // The guard still guards: unrelated modules claiming one extension cannot be resolved
        // by reading the document, because neither owns it.
        var dsl = Write("model.dsl", "workspace {}");

        // Act.
        var ambiguous = Assert.IsType<DiagramAmbiguousExtension>(Router(C4Context, RivalDsl).Route(dsl));

        // Assert.
        Assert.Equal(".dsl", ambiguous.Extension);
    }

    [Fact]
    public void AmbiguousExtensions_DoesNotReportOneVendorsFamily_ButStillReportsRivalVendors()
    {
        // Arrange, act and assert.
        Assert.Empty(Router(C4Context, C4Container).AmbiguousExtensions());
        Assert.Equal([".dsl"], Router(C4Context, RivalDsl).AmbiguousExtensions());
    }

    [Fact]
    public void ClaimsExtensionOf_ABareBodyExtension_IsTrue()
    {
        // Arrange, act and assert.
        Assert.True(Router(Mindmap).ClaimsExtensionOf(@"C:\project\ideas.mm"));
    }

    [Fact]
    public void ClaimsExtensionOf_ASharedExtension_IsTrue_WhichRouteRefuses()
    {
        // Arrange.
        // The case that distinguishes this predicate from Route: a shared extension is
        // refused on sight by Route, but the file is registrable, so it is claimed
        // (small-refinements Requirement 3.2).
        var pipeline = new DiagramDefinition(new DiagramOrigin("azure-devops", "pipeline"), "Azure DevOps pipeline", Extension: ".yml", SharedExtension: true);
        var yml = Write("build.yml", "stages: []\n");

        // Act and assert.
        Assert.True(Router(pipeline).ClaimsExtensionOf(yml));
        Assert.IsType<NotADiagram>(Router(pipeline).Route(yml));
    }

    [Fact]
    public void ClaimsExtensionOf_AnUnclaimedExtension_IsFalse()
    {
        // Arrange, act and assert.
        Assert.False(Router(Mindmap).ClaimsExtensionOf(@"C:\project
otes.txt"));
    }
}
