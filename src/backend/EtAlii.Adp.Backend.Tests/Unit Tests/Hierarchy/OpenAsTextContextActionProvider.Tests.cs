using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Context;
using EtAlii.Adp.Editor;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.TestSupport;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// "Open as text" and "Open with…" (modular-text-editors Requirements 5.2, 4.4): offered
/// exactly where the diagram family claimed the double-click, dispatching to the editor
/// family's resolver, with the choice dialog reserved for a legitimately shared extension.
/// </summary>
public class OpenAsTextContextActionProviderTests : IDisposable
{
    private readonly string _root;

    public OpenAsTextContextActionProviderTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    private static readonly Common.DiagramDefinition Mindmap =
        new(new Common.DiagramOrigin("freeplane", "mindmap"), "Mind map", Extension: ".mm");

    private static readonly EditorDefinition Plain = new("plain", "Plain Text", IsFallback: true);
    private static readonly EditorDefinition RawMm = new("raw-mm", "Raw mind map text", Extensions: [".mm"]);
    private static readonly EditorDefinition FancyMm =
        new("fancy-mm", "Structured mind map text", Extensions: [".mm"], IsDefaultForSharedExtension: true);

    private sealed class StubEditorCatalog(params EditorDefinition[] definitions) : IEditorDefinitionCatalog
    {
        public IReadOnlyList<EditorDefinition> All { get; } = definitions;
    }

    private OpenAsTextContextActionProvider Provider(params EditorDefinition[] editors) =>
        new(
            new DiagramFileRouter(new TestDiagramDefinitionCatalog([Mindmap])),
            new EditorResolver(new StubEditorCatalog(editors)));

    private ContextTarget FileTarget(string fileName, string content = "root\n")
    {
        var fullPath = IoPath.Combine(_root, fileName);
        File.WriteAllText(fullPath, content);
        return new ContextTarget(ContextScope.Hierarchy, fullPath, IsContainer: false, ShortGuid.NewShortGuid(), RootPath: _root);
    }

    private static IReadOnlyList<ContextActionDefinition> ActionsOf(IReadOnlyList<ContextActionGroupDefinition> groups) =>
        [.. groups.SelectMany(group => group.Actions)];

    [Fact]
    public async Task Discover_ADiagramClaimedFile_OffersOpenAsText()
    {
        // Arrange and act (Requirement 5.2).
        var groups = await Provider(Plain).DiscoverAsync(FileTarget("map.mm"), TestContext.Current.CancellationToken);

        // Assert: available, resolved by the fallback since nothing claims .mm among editors.
        var action = Assert.Single(ActionsOf(groups));
        Assert.Equal(OpenAsTextContextActionProvider.OpenAsTextActionId, action.Id);
        Assert.True(action.Available);
    }

    [Fact]
    public async Task Discover_AFileNoDiagramClaims_OffersNothing()
    {
        // Arrange and act: double-click already opens such a file as text (Requirement 5.4),
        // so the action would promise nothing the gesture does not deliver.
        var groups = await Provider(Plain).DiscoverAsync(FileTarget("notes.txt"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(groups);
    }

    [Fact]
    public async Task Discover_AFolder_OffersNothing()
    {
        // Arrange.
        var folder = IoPath.Combine(_root, "docs");
        Directory.CreateDirectory(folder);
        var target = new ContextTarget(ContextScope.Hierarchy, folder, IsContainer: true, ShortGuid.NewShortGuid(), RootPath: _root);

        // Act and assert.
        Assert.Empty(await Provider(Plain).DiscoverAsync(target, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Discover_ASharedExtensionWithADefault_AlsoOffersOpenWith()
    {
        // Arrange and act (Requirement 4.4's legitimate case).
        var groups = await Provider(Plain, RawMm, FancyMm).DiscoverAsync(FileTarget("map.mm"), TestContext.Current.CancellationToken);

        // Assert: both actions, both available - the default answers the plain gesture, the
        // dialog keeps every claimant reachable.
        var actions = ActionsOf(groups);
        Assert.Equal(2, actions.Count);
        Assert.All(actions, action => Assert.True(action.Available));
        Assert.Contains(actions, action => action.Id == OpenAsTextContextActionProvider.OpenWithActionId);
    }

    [Fact]
    public async Task Discover_AnUndefaultedConflict_ReportsItInsteadOfChoosing()
    {
        // Arrange and act (Requirement 4.3: the unintended case stays a deployment error).
        var rival = RawMm with { Id = "rival-mm" };
        var groups = await Provider(Plain, RawMm, rival).DiscoverAsync(FileTarget("map.mm"), TestContext.Current.CancellationToken);

        // Assert: Open as text is shown but unavailable with the reason, and no dialog
        // pretends the conflict is a choice the user should settle.
        var action = Assert.Single(ActionsOf(groups));
        Assert.Equal(OpenAsTextContextActionProvider.OpenAsTextActionId, action.Id);
        Assert.False(action.Available);
        Assert.Contains("none is the default", action.UnavailableReason);
    }

    [Fact]
    public async Task Execute_OpenAsText_Completes()
    {
        // Arrange and act: the tab is client state; Completed is the whole backend half.
        var result = await Provider(Plain).ExecuteAsync(
            FileTarget("map.mm"), OpenAsTextContextActionProvider.OpenAsTextActionId, TestContext.Current.CancellationToken);

        // Assert.
        Assert.IsType<ContextExecutionCompleted>(result);
    }

    [Fact]
    public async Task Execute_OpenWith_ListsEveryClaimant()
    {
        // Arrange and act.
        var result = await Provider(Plain, RawMm, FancyMm).ExecuteAsync(
            FileTarget("map.mm"), OpenAsTextContextActionProvider.OpenWithActionId, TestContext.Current.CancellationToken);

        // Assert: both claimants, selectable, by id - what the client submits back.
        var choice = Assert.IsType<ContextExecutionRequiresChoice>(result);
        Assert.Equal(["fancy-mm", "raw-mm"], choice.Request.Options.Select(option => option.Id));
        Assert.All(choice.Request.Options, option => Assert.True(option.Selectable));
    }

    [Fact]
    public async Task Commit_OpenWithAClaimant_Succeeds()
    {
        // Arrange and act.
        var result = await Provider(Plain, RawMm, FancyMm).CommitAsync(
            FileTarget("map.mm"), OpenAsTextContextActionProvider.OpenWithActionId, "raw-mm", "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.Completed);
    }

    [Fact]
    public async Task Commit_OpenWithANonClaimant_IsRefused()
    {
        // Arrange and act: plain is deployed but never claimed .mm - a stale dialog, or a
        // client built against a different set of modules.
        var result = await Provider(Plain, RawMm, FancyMm).CommitAsync(
            FileTarget("map.mm"), OpenAsTextContextActionProvider.OpenWithActionId, "plain", "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.Completed);
        Assert.Contains("not available", result.Error);
    }
}
