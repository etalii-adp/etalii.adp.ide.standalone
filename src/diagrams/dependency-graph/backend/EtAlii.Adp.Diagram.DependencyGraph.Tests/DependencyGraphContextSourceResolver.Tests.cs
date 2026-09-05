using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Backend.Hierarchy;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// What makes a node - or a dependency - selectable. Worth testing: that it agrees the file is a
/// dependency graph, that it declines every other type's file so their resolvers can answer, and
/// that it verifies rather than trusts what the client sent.
/// </summary>
public class DependencyGraphContextSourceResolverTests : IDisposable
{
    private readonly string _workspace = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-dgr-context-" + Guid.NewGuid().ToString("N"));

    private readonly DependencyGraphDocumentStore _store = new();
    private readonly DependencyGraphElementMapper _mapper = new();

    public DependencyGraphContextSourceResolverTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        TestFolder.TryDelete(_workspace);

        GC.SuppressFinalize(this);
    }

    private const string Graph = """
        dependencies: 1
        elements:
          - id: aaa
            label: API gateway
            x: 240
            row: 0
          - id: bbb
            label: Identity service
            x: 480
            row: 1
        relations:
          - id: ccc
            from: aaa
            to: bbb
            label: verifies tokens with
        """;

    /// <summary>
    /// Writes a bare graph body. No <c>.adp</c>: a distinctive extension routes on sight, so the
    /// selection's parent is the body file itself.
    /// </summary>
    private string Write()
    {
        var body = IoPath.Combine(_workspace, "services" + Diagram.DocumentExtension);
        File.WriteAllText(body, Graph);
        _store.Forget(body);
        return body;
    }

    private DependencyGraphContextSourceResolver Resolver() =>
        new(new DiagramFileRouter(new DependencyGraphOnlyCatalog()), _store, _mapper);

    private static ContextResolvedLevel FileLevel(string path) =>
        new(
            ContextSelectionSource.Explorer,
            new ContextSource(),
            [IoPath.GetFileName(path)],
            ContextScope.Hierarchy,
            new ContextTarget(ContextScope.Hierarchy, path, IsContainer: false, SourceId: default, IoPath.GetDirectoryName(path)!, ShortGuid.NewShortGuid()),
            new ContextLevelDetail(),
            null!);

    private async Task<ContextLevelResolution> ResolveAsync(
        ContextResolvedLevel? parent,
        string elementId,
        IReadOnlyList<string>? clientPath = null)
    {
        var id = new ContextSource { ElementId = new ElementId { Value = elementId } };
        return await Resolver().ResolveAsync(
            ShortGuid.NewShortGuid(),
            _workspace,
            ContextSelectionSource.DiagramCanvas,
            id,
            clientPath ?? [],
            parent,
            CancellationToken.None);
    }

    [Fact]
    public void ItOnlyClaimsElementIds()
    {
        // Assert.
        Assert.True(Resolver().CanResolve(new ContextSource { ElementId = new ElementId { Value = "aaa" } }));
        Assert.False(Resolver().CanResolve(new ContextSource()));
    }

    [Fact]
    public async Task ANode_Resolves_WithItsLabelAndItsPayload()
    {
        // Arrange & act.
        var path = Write();
        var resolution = await ResolveAsync(FileLevel(path), "aaa");

        // Assert.
        var level = Assert.IsType<ResolvedContextLevel>(resolution).Level;
        Assert.Equal(["API gateway"], level.RelativePath);
        Assert.Equal(ContextScope.DiagramElement, level.Scope);
        Assert.Equal("aaa", level.Target.ElementId);
        Assert.Equal(DependencyGraphElementMapper.NodeType, level.Detail.Element.ElementType);
        Assert.NotNull(level.Detail.Element.Payload);
    }

    [Fact]
    public async Task ADependency_ResolvesToo()
    {
        // Arrange & act.
        // Both live on the same stream and are selected through the same channel.
        var path = Write();
        var resolution = await ResolveAsync(FileLevel(path), "ccc");

        // Assert.
        var level = Assert.IsType<ResolvedContextLevel>(resolution).Level;
        Assert.Equal(["verifies tokens with"], level.RelativePath);
        Assert.Equal(DependencyGraphElementMapper.RelationType, level.Detail.Element.ElementType);
    }

    [Fact]
    public async Task AnUnlabelledDependency_IsNamedByItsDirection()
    {
        // Arrange.
        var path = IoPath.Combine(_workspace, "bare" + Diagram.DocumentExtension);
        await File.WriteAllTextAsync(path, "dependencies: 1\nelements:\n  - id: aaa\n    x: 0\n  - id: bbb\n    x: 100\nrelations:\n  - id: ccc\n    from: aaa\n    to: bbb\n", TestContext.Current.CancellationToken);
        _store.Forget(path);

        // Act.
        var resolution = await ResolveAsync(FileLevel(path), "ccc");

        // Assert.
        // The arrow in the fallback name is the direction, not decoration: a selection that read
        // "bbb → aaa" would describe the opposite dependency.
        var level = Assert.IsType<ResolvedContextLevel>(resolution).Level;
        Assert.Equal(["aaa → bbb"], level.RelativePath);
    }

    [Fact]
    public async Task AnAbsentId_IsRejected()
    {
        // Arrange & act.
        var path = Write();
        var resolution = await ResolveAsync(FileLevel(path), "ghost");

        // Assert.
        Assert.IsType<RejectedContextLevel>(resolution);
    }

    [Fact]
    public async Task AnotherTypesFile_IsDeclined_SoItsOwnResolverCanAnswer()
    {
        // Arrange.
        // Every module's resolver claims every element-id shape; ownership is decided by the
        // enclosing file. Claiming a foreign file here would take the selection away from the
        // module that owns it - and a timeline sitting beside a graph is the likeliest such file,
        // since the two share every id shape this module uses.
        var foreign = IoPath.Combine(_workspace, "plan.tml");
        await File.WriteAllTextAsync(foreign, "timeline: 1\nelements: []\n", TestContext.Current.CancellationToken);

        // Act.
        var resolution = await ResolveAsync(FileLevel(foreign), "aaa");

        // Assert.
        Assert.IsType<RejectedContextLevel>(resolution);
    }

    [Fact]
    public async Task NoParentLevel_IsRejected()
    {
        // Arrange & act.
        Write();
        var resolution = await ResolveAsync(parent: null, "aaa");

        // Assert.
        Assert.IsType<RejectedContextLevel>(resolution);
    }

    [Fact]
    public async Task TheClientsPath_IsCheckedNeverTrusted()
    {
        // Arrange & act.
        var path = Write();
        var lying = await ResolveAsync(FileLevel(path), "aaa", ["SomethingElse"]);
        var honest = await ResolveAsync(FileLevel(path), "aaa", ["API gateway"]);

        // Assert.
        Assert.IsType<RejectedContextLevel>(lying);
        Assert.IsType<ResolvedContextLevel>(honest);
    }

    [Fact]
    public async Task Track_ReportsARename_AndAClearOnRemoval()
    {
        // Arrange.
        var path = Write();
        var level = Assert.IsType<ResolvedContextLevel>(await ResolveAsync(FileLevel(path), "aaa")).Level;
        var resolver = Resolver();
        IReadOnlyList<string>? latest = ["untouched"];
        using var tracking = resolver.Track(ShortGuid.NewShortGuid(), _workspace, level, changed => latest = changed);

        // Act: a rename keeps the id while changing everything shown.
        await File.WriteAllTextAsync(path, Graph.Replace("label: API gateway", "label: Renamed", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        _store.Reload(path);
        var afterRename = latest;

        // Act: a removal clears the selection.
        await File.WriteAllTextAsync(path, "dependencies: 1\nelements:\n  - id: bbb\n    x: 480\n    row: 1\n", TestContext.Current.CancellationToken);
        _store.Reload(path);

        // Assert.
        Assert.Equal(["Renamed"], afterRename);
        Assert.Null(latest);
    }

    [Fact]
    public async Task APlacementId_Resolves_SoAGestureCanNameWhereItLanded()
    {
        // Arrange.
        // The synthetic id a drop or a relation-to-empty-space carries: it names the node about
        // to exist at a position, resolves like any element within the diagram, and lives for
        // exactly one ExecuteAction.
        var path = Write();
        var placement = DependencyGraphNewPlacement.IdFor(-180.5, 4);

        // Act.
        var resolution = await ResolveAsync(FileLevel(path), placement);

        // Assert.
        var level = Assert.IsType<ResolvedContextLevel>(resolution).Level;
        Assert.Equal(placement, level.Target.ElementId);
        Assert.Equal(ContextScope.DiagramElement, level.Scope);
    }

    [Fact]
    public async Task APlacementId_IsStillRefusedOnAForeignFile()
    {
        // Arrange.
        // The file check comes before the placement check: a placement in somebody else's
        // diagram is not this module's to resolve.
        var foreign = IoPath.Combine(_workspace, "plan.tml");
        await File.WriteAllTextAsync(foreign, "timeline: 1\nelements: []\n", TestContext.Current.CancellationToken);

        // Act.
        var resolution = await ResolveAsync(FileLevel(foreign), DependencyGraphNewPlacement.IdFor(0, 0));

        // Assert.
        Assert.IsType<RejectedContextLevel>(resolution);
    }
}

/// <summary>
/// A catalog carrying only this module's definition, so the router routes the fixture files
/// without the process-wide discovery cache being filled by a test.
/// </summary>
internal sealed class DependencyGraphOnlyCatalog : IDiagramDefinitionCatalog
{
    public IReadOnlyList<DiagramDefinition> All => Diagram.Definitions;
}
