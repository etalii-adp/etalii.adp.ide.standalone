using EtAlii.Adp.Common;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.TestSupport;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Routing's registration reads against the world's write side. RenameEntryCommandHandler
/// rewrites a registration's <c>body:</c> header in place, so a route - the problems watcher
/// routes on every change event - and that write can genuinely overlap; the read must not be
/// refused, per the sharing discipline SharedDocumentReader carries. Windows enforces
/// sharing, so this guard bites there.
/// </summary>
public class DiagramFileRouterSharedReadTests : IDisposable
{
    private static readonly DiagramDefinition Mindmap = new(new DiagramOrigin("freeplane", "mindmap"), "Mind map", Extension: ".mm");

    private readonly string _root;

    public DiagramFileRouterSharedReadTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    [Fact]
    public void Route_ReadsARegistrationAnEditorIsStillWriting()
    {
        // Arrange: the handle the rename's rewrite holds - write access, sharing only reads,
        // the mode File.WriteAllText opens with.
        var adp = IoPath.Combine(_root, "domain.adp");
        File.WriteAllText(adp, "freeplane/mindmap\nbody: notes.mm\n");
        File.WriteAllText(IoPath.Combine(_root, "notes.mm"), "<map/>");
        using var editor = new FileStream(adp, FileMode.Open, FileAccess.Write, FileShare.Read);

        // Act.
        var routed = Assert.IsType<DiagramRouted>(
            new DiagramFileRouter(new TestDiagramDefinitionCatalog([Mindmap])).Route(adp, _root));

        // Assert: routed by its first line, body resolved through its header - both read
        // through the held-open handle.
        Assert.Equal(Mindmap.Origin, routed.Definition.Origin);
        Assert.Equal(IoPath.Combine(_root, "notes.mm"), routed.BodyPath);
    }
}
