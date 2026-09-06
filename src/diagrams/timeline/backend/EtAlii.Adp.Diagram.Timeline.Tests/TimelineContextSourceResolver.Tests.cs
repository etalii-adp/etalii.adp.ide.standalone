using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Hierarchy;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// What makes a timeline element - or connection - selectable. Worth testing: that it agrees
/// the file is a timeline, that it declines every other type's file so their resolvers can
/// answer, and that it verifies rather than trusts what the client sent.
/// </summary>
public class TimelineContextSourceResolverTests : IDisposable
{
    private readonly string _workspace = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-timeline-context-" + Guid.NewGuid().ToString("N"));

    private readonly TimelineDocumentStore _store = new();
    private readonly TimelineElementMapper _mapper = new();

    public TimelineContextSourceResolverTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        TestFolder.TryDelete(_workspace);

        GC.SuppressFinalize(this);
    }

    private const string Timeline = """
        timeline: 1
        elements:
          - id: aaa
            label: Discovery
            begin: 2026-01-05
            end: 2026-02-13
            row: 0
          - id: bbb
            label: Go
            begin: 2026-02-16
            row: 1
        connections:
          - id: ccc
            from: aaa
            to: bbb
            label: gates
        """;

    /// <summary>
    /// Writes a bare timeline body. No <c>.adp</c>: a distinctive extension routes on sight
    /// (Requirement 1.2), so the selection's parent is the body file itself.
    /// </summary>
    private string Write()
    {
        var body = IoPath.Combine(_workspace, "plan" + Diagram.DocumentExtension);
        File.WriteAllText(body, Timeline);
        _store.Forget(body);
        return body;
    }

    private TimelineContextSourceResolver Resolver() =>
        new(new DiagramFileRouter(new TimelineOnlyCatalog()), _store, _mapper);

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
    public async Task AnElement_Resolves_WithItsLabelAndItsPayload()
    {
        // Arrange & act.
        var path = Write();
        var resolution = await ResolveAsync(FileLevel(path), "aaa");

        // Assert.
        var level = Assert.IsType<ResolvedContextLevel>(resolution).Level;
        Assert.Equal(["Discovery"], level.RelativePath);
        Assert.Equal(ContextScope.DiagramElement, level.Scope);
        Assert.Equal("aaa", level.Target.ElementId);
        Assert.Equal(TimelineElementMapper.PeriodType, level.Detail.Element.ElementType);
        Assert.NotNull(level.Detail.Element.Payload);
    }

    [Fact]
    public async Task AConnection_ResolvesToo()
    {
        // Arrange & act.
        // Both live on the same stream and are selected through the same channel.
        var path = Write();
        var resolution = await ResolveAsync(FileLevel(path), "ccc");

        // Assert.
        var level = Assert.IsType<ResolvedContextLevel>(resolution).Level;
        Assert.Equal(["gates"], level.RelativePath);
        Assert.Equal(TimelineElementMapper.ConnectionType, level.Detail.Element.ElementType);
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
        // module that owns it - the collision the selection resolver's first-claimant fix exists
        // to survive.
        var foreign = IoPath.Combine(_workspace, "map.owm");
        await File.WriteAllTextAsync(foreign, "title something\n", TestContext.Current.CancellationToken);

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
        var honest = await ResolveAsync(FileLevel(path), "aaa", ["Discovery"]);

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
        await File.WriteAllTextAsync(path, Timeline.Replace("label: Discovery", "label: Renamed", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        _store.Reload(path);
        var afterRename = latest;

        // Act: a removal clears the selection.
        await File.WriteAllTextAsync(path, "timeline: 1\nelements:\n  - id: bbb\n    begin: 2026-02-16\n    row: 1\n", TestContext.Current.CancellationToken);
        _store.Reload(path);

        // Assert.
        Assert.Equal(["Renamed"], afterRename);
        Assert.Null(latest);
    }

    [Fact]
    public async Task APlacementId_Resolves_SoAGestureCanNameWhereItLanded()
    {
        // Arrange.
        // The synthetic id a drop or a relation-to-empty-space carries: it names the element
        // about to exist at a position, resolves like any element within the diagram, and lives
        // for exactly one ExecuteAction.
        var path = Write();
        var placement = TimelineNewPlacement.IdFor(1_780_000_000, 4);

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
        var foreign = IoPath.Combine(_workspace, "map.owm");
        await File.WriteAllTextAsync(foreign, "title something\n", TestContext.Current.CancellationToken);

        // Act.
        var resolution = await ResolveAsync(FileLevel(foreign), TimelineNewPlacement.IdFor(0, 0));

        // Assert.
        Assert.IsType<RejectedContextLevel>(resolution);
    }
}

/// <summary>
/// A catalog carrying only this module's definition, so the router routes the fixture files
/// without the process-wide discovery cache being filled by a test.
/// </summary>
internal sealed class TimelineOnlyCatalog : IDiagramDefinitionCatalog
{
    public IReadOnlyList<DiagramDefinition> All => Diagram.Definitions;
}
