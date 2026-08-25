using EtAlii.Adp.Backend.Context;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

public class MindmapContextSourceResolverTests : IDisposable
{
    private readonly MindmapTestProject _project = new();

    public void Dispose() => _project.Dispose();

    private ValueTask<ContextLevelResolution> ResolveAsync(string nodeId, IReadOnlyList<string>? clientPath = null, ContextResolvedLevel? parent = null) =>
        _project.Resolver.ResolveAsync(
            _project.WatchId,
            _project.Root,
            ContextSelectionSource.DiagramCanvas,
            MindmapTestProject.Element(nodeId),
            clientPath ?? [],
            parent ?? _project.FileLevel(),
            TestContext.Current.CancellationToken);

    [Fact]
    public void CanResolve_OnlyElementIds()
    {
        Assert.True(_project.Resolver.CanResolve(MindmapTestProject.Element("x")));
        Assert.False(_project.Resolver.CanResolve(new ContextSource { EntryId = ShortGuid.NewShortGuid() }));
    }

    [Fact]
    public async Task Resolve_ANodeInTheDiagram_GivesItsPathScopeTargetAndDetail()
    {
        var level = Assert.IsType<ResolvedContextLevel>(await ResolveAsync("ID_88117420")).Level;

        Assert.Equal(["ADP architecture", "Backend", "Context service"], level.RelativePath);
        Assert.Equal(ContextScope.DiagramElement, level.Scope);
        Assert.Equal(_project.BodyPath, level.Target.ResolvedFullPath);
        Assert.Equal("ID_88117420", level.Target.ElementId);
        Assert.Equal(_project.WatchId, level.Target.WatchId);
        Assert.Equal(_project.Root, level.Target.RootPath);

        var detail = level.Detail.Element;
        Assert.Equal("Context service", detail.Text);
        Assert.False(detail.HasChildren);
        Assert.True(detail.Linked);
        Assert.False(detail.Folded);
    }

    [Fact]
    public async Task Resolve_SeedsFoldStateFromTheFile()
    {
        // Requirement 9.3: Hierarchy is FOLDED="true" in the corpus.
        var level = Assert.IsType<ResolvedContextLevel>(await ResolveAsync("ID_88117425")).Level;

        Assert.True(level.Detail.Element.Folded);
        Assert.True(level.Target.IsContainer);
    }

    [Fact]
    public async Task Resolve_AnUnknownNode_IsRejected()
    {
        Assert.IsType<RejectedContextLevel>(await ResolveAsync("ID_nope"));
    }

    [Fact]
    public async Task Resolve_WithoutAFileAboveIt_IsRejected()
    {
        // Requirement 10.4: a node is only ever verified against the diagram it nests under.
        var resolution = await _project.Resolver.ResolveAsync(
            _project.WatchId, _project.Root, ContextSelectionSource.DiagramCanvas,
            MindmapTestProject.Element("ID_88117420"), [], parent: null, TestContext.Current.CancellationToken);

        Assert.IsType<RejectedContextLevel>(resolution);
    }

    [Fact]
    public async Task Resolve_UnderAFileThatIsNotAMindmap_IsRejected()
    {
        var other = IoPath.Combine(_project.Root, "docs", "notes.txt");
        File.WriteAllText(other, "hello");
        var parent = _project.FileLevel() with { Target = _project.FileLevel().Target with { ResolvedFullPath = other } };

        Assert.IsType<RejectedContextLevel>(await ResolveAsync("ID_88117420", parent: parent));
    }

    [Fact]
    public async Task Resolve_AClientPathThatDoesNotMatch_IsRejected()
    {
        Assert.IsType<RejectedContextLevel>(await ResolveAsync("ID_88117420", ["wrong", "path"]));
    }

    [Fact]
    public async Task Resolve_AMatchingClientPath_IsAccepted()
    {
        Assert.IsType<ResolvedContextLevel>(await ResolveAsync("ID_88117420", ["ADP architecture", "Backend", "Context service"]));
    }

    [Fact]
    public async Task NestingOf_ANode_IsNotNestable()
    {
        var result = await ResolveAsync("ID_88117420");
        var level = Assert.IsType<ResolvedContextLevel>(result).Level;

        Assert.Equal(ContextNesting.NotNestable, _project.Resolver.NestingOf(level));
    }

    [Fact]
    public async Task Track_ReportsTheNewPath_WhenAnAncestorIsRenamed()
    {
        var level = Assert.IsType<ResolvedContextLevel>(await ResolveAsync("ID_88117420")).Level;
        IReadOnlyList<string>? reported = ["unchanged"];
        using var track = _project.Resolver.Track(_project.WatchId, _project.Root, level, path => reported = path);

        await _project.History.ExecuteAsync(new SetNodeTextCommand(_project.BodyPath, "ID_411002937", "Server"), TestContext.Current.CancellationToken);

        Assert.Equal(["ADP architecture", "Server", "Context service"], reported);
    }

    [Fact]
    public async Task Track_ReportsNull_WhenTheNodeIsRemoved()
    {
        // Requirement 10.7: the selection clears without the client asking again.
        var level = Assert.IsType<ResolvedContextLevel>(await ResolveAsync("ID_88117420")).Level;
        IReadOnlyList<string>? reported = ["unchanged"];
        using var track = _project.Resolver.Track(_project.WatchId, _project.Root, level, path => reported = path);

        await _project.History.ExecuteAsync(new RemoveNodeCommand(_project.BodyPath, "ID_88117420"), TestContext.Current.CancellationToken);

        Assert.Null(reported);
    }

    [Fact]
    public async Task Track_StaysQuiet_ForAChangeThatDoesNotMoveTheNode()
    {
        var level = Assert.IsType<ResolvedContextLevel>(await ResolveAsync("ID_88117420")).Level;
        var calls = 0;
        using var track = _project.Resolver.Track(_project.WatchId, _project.Root, level, _ => calls++);

        await _project.History.ExecuteAsync(new SetNodeTextCommand(_project.BodyPath, "ID_88117422", "elsewhere"), TestContext.Current.CancellationToken);

        Assert.Equal(0, calls);
    }
}
