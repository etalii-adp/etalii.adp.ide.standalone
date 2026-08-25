using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Backend.Hierarchy;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

public class HierarchyContextSourceResolverTests : IDisposable
{
    private readonly string _root;
    private readonly HierarchyModelStore _store = new(idleTimeout: TimeSpan.FromMinutes(5));
    private readonly HierarchyContextSourceResolver _resolver;
    private readonly ShortGuid _watchId = ShortGuid.NewShortGuid();

    public HierarchyContextSourceResolverTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _resolver = new HierarchyContextSourceResolver(_store, new DiagramFileRouter(new EmptyCatalog()));
    }

    public void Dispose()
    {
        _store.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string CreateFolder(params string[] segments)
    {
        var path = IoPath.Combine(new[] { _root }.Concat(segments).ToArray());
        Directory.CreateDirectory(path);
        return path;
    }

    private string CreateFile(params string[] segments)
    {
        var path = IoPath.Combine(new[] { _root }.Concat(segments).ToArray());
        File.WriteAllText(path, "");
        return path;
    }

    private HierarchyModel Model() => _store.GetOrCreate(_watchId, _root);

    /// <summary>Lists folders from the root down so the model knows every entry along the way.</summary>
    private ShortGuid IdOf(params string[] segments)
    {
        var model = Model();
        ShortGuid? folderId = null;
        EntryNode? node = null;
        foreach (var segment in segments)
        {
            node = model.ListChildren(folderId).Single(n => n.Name == segment);
            folderId = node.Id;
        }

        return node!.Id;
    }

    private static ContextSource Source(ShortGuid id) => new() { EntryId = id };

    private ValueTask<ContextLevelResolution> ResolveAsync(ShortGuid id, IReadOnlyList<string>? clientPath = null, ContextResolvedLevel? parent = null) =>
        _resolver.ResolveAsync(_watchId, _root, ContextSelectionSource.Explorer, Source(id), clientPath ?? [], parent, TestContext.Current.CancellationToken);

    [Fact]
    public async Task ResolveAsync_NestedFile_YieldsProjectRelativeSegmentsAndDetail()
    {
        CreateFolder("docs");
        CreateFile("docs", "design.mm");
        var id = IdOf("docs", "design.mm");

        var result = await ResolveAsync(id);

        var level = Assert.IsType<ResolvedContextLevel>(result).Level;
        Assert.Equal(new[] { "docs", "design.mm" }, level.RelativePath);
        Assert.Equal(EntryKind.File, level.Detail.Entry.Kind);
        Assert.True(level.Detail.Entry.Available);
        Assert.Equal(ContextScope.Hierarchy, level.Scope);
        Assert.False(level.Target.IsContainer);
        Assert.Equal(IoPath.Combine(_root, "docs", "design.mm"), level.Target.ResolvedFullPath);
    }

    [Fact]
    public async Task ResolveAsync_MatchingClientPath_IsAccepted()
    {
        CreateFile("a.txt");

        var result = await ResolveAsync(IdOf("a.txt"), ["a.txt"]);

        Assert.IsType<ResolvedContextLevel>(result);
    }

    [Fact]
    public async Task ResolveAsync_MismatchingClientPath_IsRejected()
    {
        CreateFile("a.txt");

        var result = await ResolveAsync(IdOf("a.txt"), ["b.txt"]);

        Assert.IsType<RejectedContextLevel>(result);
    }

    [Fact]
    public async Task ResolveAsync_UnknownId_IsRejected()
    {
        var result = await ResolveAsync(ShortGuid.NewShortGuid());

        Assert.IsType<RejectedContextLevel>(result);
    }

    [Fact]
    public async Task ResolveAsync_IdFromAnotherConnection_IsRejected()
    {
        CreateFile("a.txt");
        var otherConnection = _store.GetOrCreate(ShortGuid.NewShortGuid(), _root);
        var foreignId = otherConnection.ListChildren(null).Single().Id;

        var result = await ResolveAsync(foreignId);

        Assert.IsType<RejectedContextLevel>(result);
    }

    [Fact]
    public async Task NestingOf_FolderContains_FileDoesNot()
    {
        CreateFolder("docs");
        CreateFile("a.txt");
        var folder = Assert.IsType<ResolvedContextLevel>(await ResolveAsync(IdOf("docs"))).Level;
        var file = Assert.IsType<ResolvedContextLevel>(await ResolveAsync(IdOf("a.txt"))).Level;

        Assert.Equal(ContextNesting.Contained, _resolver.NestingOf(folder));
        Assert.Equal(ContextNesting.NotNestable, _resolver.NestingOf(file));
    }

    [Fact]
    public async Task ResolveAsync_ChildInsideParent_YieldsParentRelativePath()
    {
        CreateFolder("docs");
        CreateFile("docs", "design.mm");
        var parent = Assert.IsType<ResolvedContextLevel>(await ResolveAsync(IdOf("docs"))).Level;

        var result = await ResolveAsync(IdOf("docs", "design.mm"), parent: parent);

        var level = Assert.IsType<ResolvedContextLevel>(result).Level;
        Assert.Equal(new[] { "design.mm" }, level.RelativePath);
    }

    [Fact]
    public async Task ResolveAsync_ChildNotInsideParent_IsRejected()
    {
        CreateFolder("docs");
        CreateFolder("src");
        CreateFile("src", "a.cs");
        var parent = Assert.IsType<ResolvedContextLevel>(await ResolveAsync(IdOf("docs"))).Level;

        var result = await ResolveAsync(IdOf("src", "a.cs"), parent: parent);

        Assert.IsType<RejectedContextLevel>(result);
    }

    [Fact]
    public async Task Track_RenameOfTheEntry_ReportsTheNewPath()
    {
        var file = CreateFile("a.txt");
        var level = Assert.IsType<ResolvedContextLevel>(await ResolveAsync(IdOf("a.txt"))).Level;
        var reported = new List<IReadOnlyList<string>?>();
        using var track = _resolver.Track(_watchId, _root, level, reported.Add);

        var renamed = IoPath.Combine(_root, "b.txt");
        File.Move(file, renamed);
        Model().OnWatcherEvent(WatcherChangeTypes.Renamed, file, renamed);

        Assert.Equal(new[] { "b.txt" }, Assert.Single(reported));
    }

    [Fact]
    public async Task Track_RenameOfAnAncestorFolder_ReportsTheNewPath()
    {
        var folder = CreateFolder("docs");
        CreateFile("docs", "design.mm");
        var level = Assert.IsType<ResolvedContextLevel>(await ResolveAsync(IdOf("docs", "design.mm"))).Level;
        var reported = new List<IReadOnlyList<string>?>();
        using var track = _resolver.Track(_watchId, _root, level, reported.Add);

        var renamed = IoPath.Combine(_root, "documents");
        Directory.Move(folder, renamed);
        Model().OnWatcherEvent(WatcherChangeTypes.Renamed, folder, renamed);

        Assert.Equal(new[] { "documents", "design.mm" }, Assert.Single(reported));
    }

    [Fact]
    public async Task Track_DeleteOfTheEntry_ReportsNull()
    {
        var file = CreateFile("a.txt");
        var level = Assert.IsType<ResolvedContextLevel>(await ResolveAsync(IdOf("a.txt"))).Level;
        var reported = new List<IReadOnlyList<string>?>();
        using var track = _resolver.Track(_watchId, _root, level, reported.Add);

        File.Delete(file);
        Model().OnWatcherEvent(WatcherChangeTypes.Deleted, file, null);

        Assert.Null(Assert.Single(reported));
    }

    [Fact]
    public async Task Track_AfterDispose_ReportsNothing()
    {
        var file = CreateFile("a.txt");
        var level = Assert.IsType<ResolvedContextLevel>(await ResolveAsync(IdOf("a.txt"))).Level;
        var reported = new List<IReadOnlyList<string>?>();
        var track = _resolver.Track(_watchId, _root, level, reported.Add);
        track.Dispose();

        File.Delete(file);
        Model().OnWatcherEvent(WatcherChangeTypes.Deleted, file, null);

        Assert.Empty(reported);
    }

    [Fact]
    public async Task Track_ARemovalEvent_WhileTheTrackedFileAlreadyVanishedFromDisk_ReportsNullInsteadOfThrowing()
    {
        // The crash the diagram-workspace-tabs manual pass found: a multi-file delete (a git
        // checkout, say) raises one watcher event per file. The first event re-resolves every
        // tracked selection - including one whose file is already gone from disk while its
        // model entry is not - and the containment check's link probe threw on the vanished
        // path, on the watcher's thread, killing the whole backend process.
        var sibling = CreateFile("a.txt");
        var tracked = CreateFile("b.txt");
        var level = Assert.IsType<ResolvedContextLevel>(await ResolveAsync(IdOf("b.txt"))).Level;
        var reported = new List<IReadOnlyList<string>?>();
        using var track = _resolver.Track(_watchId, _root, level, reported.Add);

        // Both files go from disk at once; only the sibling's event has been processed so far,
        // so the tracked entry still exists in the model while its file does not.
        File.Delete(tracked);
        File.Delete(sibling);
        Model().OnWatcherEvent(WatcherChangeTypes.Deleted, sibling, null);

        // The tracked entry could not be re-resolved (its file is gone), which reports the
        // selection as vanished - never an exception out of the event.
        Assert.Null(Assert.Single(reported));
    }

    // ---- the diagram type on the detail (diagram-workspace-tabs Requirement 1) -----------
    //
    // The workspace opens tabs from the pushed selection, so the detail must say which files
    // are diagrams - through the router, never a client-side file-type table.

    private static readonly Diagram.DiagramDefinition Mindmap =
        new(new Diagram.DiagramOrigin("freeplane", "mindmap"), "Mind map", ".mm");

    private static readonly Diagram.DiagramDefinition RivalMindmap =
        new(new Diagram.DiagramOrigin("xmind", "mindmap"), "Rival map", ".mm");

    /// <summary>The resolver over a catalog that knows the given definitions, unlike the class's empty default.</summary>
    private HierarchyContextSourceResolver ResolverKnowing(params Diagram.DiagramDefinition[] definitions) =>
        new(_store, new DiagramFileRouter(new TestDiagramDefinitionCatalog(definitions)));

    private async Task<string> DiagramMimeOfAsync(HierarchyContextSourceResolver resolver, params string[] segments)
    {
        var result = await resolver.ResolveAsync(
            _watchId, _root, ContextSelectionSource.Explorer, Source(IdOf(segments)), [], null, TestContext.Current.CancellationToken);
        return Assert.IsType<ResolvedContextLevel>(result).Level.Detail.Entry.DiagramMimeType;
    }

    [Fact]
    public async Task ResolveAsync_ARegisteredDiagram_CarriesItsMimeType()
    {
        File.WriteAllText(IoPath.Combine(_root, "domain.adp"), "freeplane/mindmap\n");

        Assert.Equal("freeplane/mindmap", await DiagramMimeOfAsync(ResolverKnowing(Mindmap), "domain.adp"));
    }

    [Fact]
    public async Task ResolveAsync_ABareBodyWithADeclaredExtension_CarriesItsMimeType()
    {
        // A map created in Freeplane and dropped into the folder (mindmap-diagram Requirement 2.7).
        CreateFile("dropped.mm");

        Assert.Equal("freeplane/mindmap", await DiagramMimeOfAsync(ResolverKnowing(Mindmap), "dropped.mm"));
    }

    [Fact]
    public async Task ResolveAsync_ARegistrationNamingAnUnknownType_CarriesTheNamedType()
    {
        // The client can then open a tab that says the type is unavailable, rather than
        // treating the file as plain (Requirements 1.4, 4.4).
        File.WriteAllText(IoPath.Combine(_root, "future.adp"), "vendor/unheard-of\n");

        Assert.Equal("vendor/unheard-of", await DiagramMimeOfAsync(ResolverKnowing(Mindmap), "future.adp"));
    }

    [Fact]
    public async Task ResolveAsync_ABareBodyWithAnAmbiguousExtension_CarriesNoType()
    {
        // Two types claim .mm: the router refuses to guess, so the file is not openable.
        CreateFile("contested.mm");

        Assert.Equal("", await DiagramMimeOfAsync(ResolverKnowing(Mindmap, RivalMindmap), "contested.mm"));
    }

    [Fact]
    public async Task ResolveAsync_APlainFileAndAFolder_CarryNoType()
    {
        CreateFile("readme.txt");
        CreateFolder("docs");
        var resolver = ResolverKnowing(Mindmap);

        Assert.Equal("", await DiagramMimeOfAsync(resolver, "readme.txt"));
        Assert.Equal("", await DiagramMimeOfAsync(resolver, "docs"));
    }

}
