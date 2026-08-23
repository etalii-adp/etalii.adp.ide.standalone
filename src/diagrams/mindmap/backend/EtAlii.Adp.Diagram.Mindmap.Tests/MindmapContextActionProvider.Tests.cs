using EtAlii.Adp.Backend.Context;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

public class MindmapContextActionProviderTests : IDisposable
{
    private readonly MindmapTestProject _project = new();

    public void Dispose() => _project.Dispose();

    private async Task<IReadOnlyList<ContextActionDefinition>> Discover(string nodeId) =>
        (await _project.Provider.DiscoverAsync(_project.NodeTarget(nodeId), TestContext.Current.CancellationToken))
            .SelectMany(group => group.Actions).ToList();

    private ValueTask<ContextExecutionResult> Execute(string nodeId, string actionId) =>
        _project.Provider.ExecuteAsync(_project.NodeTarget(nodeId), actionId, TestContext.Current.CancellationToken);

    private ValueTask<ContextCommitResult> Commit(string nodeId, string actionId, string value) =>
        _project.Provider.CommitAsync(_project.NodeTarget(nodeId), actionId, value, "", TestContext.Current.CancellationToken);

    // ---- what is offered -----------------------------------------------------------------

    [Fact]
    public async Task Discover_ALeaf_OffersEverythingButFoldAndUnlink()
    {
        var ids = (await Discover("ID_88117422")).Select(action => action.Id).ToList();

        Assert.Equal(
            [MindmapContextActionProvider.AddChildActionId, MindmapContextActionProvider.AddSiblingActionId,
             MindmapContextActionProvider.RenameActionId, MindmapContextActionProvider.DeleteActionId,
             MindmapContextActionProvider.EditNotesActionId, MindmapContextActionProvider.LinkActionId],
            ids);
    }

    [Fact]
    public async Task Discover_TheRoot_OffersNoSiblingAndNoDelete()
    {
        var ids = (await Discover(_project.Document.Root.Id)).Select(action => action.Id).ToList();

        Assert.DoesNotContain(MindmapContextActionProvider.AddSiblingActionId, ids);
        Assert.DoesNotContain(MindmapContextActionProvider.DeleteActionId, ids);
        Assert.Contains(MindmapContextActionProvider.AddChildActionId, ids);
    }

    [Fact]
    public async Task Discover_ALinkedNode_OffersUnlink()
    {
        var ids = (await Discover("ID_88117420")).Select(action => action.Id).ToList();

        Assert.Contains(MindmapContextActionProvider.UnlinkActionId, ids);
    }

    [Fact]
    public async Task Discover_ANodeWithChildren_OffersCollapseOrExpand_NamedByItsCurrentState()
    {
        var fold = (await Discover("ID_411002937")).Single(action => action.Id == MindmapContextActionProvider.ToggleFoldActionId);
        Assert.Equal("Collapse", fold.Label);

        var folded = (await Discover("ID_88117425")).Single(action => action.Id == MindmapContextActionProvider.ToggleFoldActionId);
        Assert.Equal("Expand", folded.Label); // FOLDED="true" in the file seeds it
    }

    [Fact]
    public async Task Discover_CarriesFreeplanesShortcutsAsData()
    {
        // Requirement 8.4: the client holds no key table; these are the only source.
        var actions = await Discover("ID_88117422");

        Assert.Equal("Insert", actions.Single(a => a.Id == MindmapContextActionProvider.AddChildActionId).Shortcut!.Key);
        Assert.Equal("Enter", actions.Single(a => a.Id == MindmapContextActionProvider.AddSiblingActionId).Shortcut!.Key);
        Assert.Equal("F2", actions.Single(a => a.Id == MindmapContextActionProvider.RenameActionId).Shortcut!.Key);
        Assert.Equal("Delete", actions.Single(a => a.Id == MindmapContextActionProvider.DeleteActionId).Shortcut!.Key);
    }

    [Fact]
    public async Task Discover_AnUnknownNode_OffersNothing()
    {
        Assert.Empty(await Discover("ID_nope"));
    }

    // ---- what executing does -----------------------------------------------------------

    [Fact]
    public async Task Execute_Rename_AsksWithTheCurrentText()
    {
        var result = Assert.IsType<ContextExecutionResult.RequiresInput>(await Execute("ID_88117422", MindmapContextActionProvider.RenameActionId));

        Assert.Equal("ICommand", result.Request.InitialValue);
    }

    [Fact]
    public async Task Execute_DeleteOnALeaf_RemovesItAtOnce_AndItIsUndoable()
    {
        var result = await Execute("ID_88117422", MindmapContextActionProvider.DeleteActionId);

        Assert.IsType<ContextExecutionResult.Completed>(result);
        Assert.Null(_project.Document.Find("ID_88117422"));
        Assert.True(_project.History.CanUndo);
    }

    [Fact]
    public async Task Execute_DeleteOnABranch_AsksFirst_NamingWhatGoes()
    {
        var result = Assert.IsType<ContextExecutionResult.RequiresConfirmation>(await Execute("ID_411002937", MindmapContextActionProvider.DeleteActionId));

        Assert.Contains("9 nodes", result.Request.Message, StringComparison.Ordinal);
        Assert.True(result.Request.Danger);
        Assert.NotNull(_project.Document.Find("ID_411002937"));
    }

    [Fact]
    public async Task Execute_ToggleFold_ChangesViewStateOnly_AndRecordsNothing()
    {
        // Requirements 9.4, 9.6: no command, no history entry, no write.
        var before = File.ReadAllText(_project.BodyPath);

        await Execute("ID_411002937", MindmapContextActionProvider.ToggleFoldActionId);

        Assert.True(_project.Views.Find(_project.WatchId, _project.BodyPath)!.IsFolded("ID_411002937"));
        Assert.False(_project.History.CanUndo);
        Assert.Equal(before, File.ReadAllText(_project.BodyPath));
    }

    [Fact]
    public async Task Execute_ToggleFold_IsPerConnection()
    {
        await Execute("ID_411002937", MindmapContextActionProvider.ToggleFoldActionId);

        var other = _project.Views.For(ShortGuid.NewShortGuid(), _project.BodyPath, _project.Document);

        Assert.False(other.IsFolded("ID_411002937"));
    }

    [Fact]
    public async Task Execute_Unlink_DispatchesACommand_SoItIsUndoable()
    {
        var result = await Execute("ID_88117420", MindmapContextActionProvider.UnlinkActionId);

        Assert.IsType<ContextExecutionResult.Completed>(result);
        Assert.Null(_project.Document.Find("ID_88117420")!.Link);

        await _project.History.UndoAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(_project.Document.Find("ID_88117420")!.Link);
    }

    [Fact]
    public async Task Execute_Link_OffersTheProjectsFilesToPickFrom()
    {
        File.WriteAllText(IoPath.Combine(_project.Root, "README.md"), "");

        var result = Assert.IsType<ContextExecutionResult.RequiresChoice>(await Execute("ID_88117422", MindmapContextActionProvider.LinkActionId));

        Assert.Contains(result.Request.Options, option => option.Id == "README.md" && option.Selectable);
        var docs = Assert.Single(result.Request.Options, option => option.Id == "docs");
        Assert.Contains(docs.Children!, option => option.Id == "docs/architecture.mm");
    }

    // ---- what committing does -----------------------------------------------------------

    [Fact]
    public async Task Commit_AddChild_AddsAnUndoableNode()
    {
        var result = await Commit("ID_88117422", MindmapContextActionProvider.AddChildActionId, "a child");

        Assert.True(result.Completed, result.Error);
        Assert.Equal("a child", _project.Document.Find("ID_88117422")!.Children.Single().Text);
        Assert.True(_project.History.CanUndo);
    }

    [Fact]
    public async Task Commit_Rename_ToEmpty_IsAccepted()
    {
        var result = await Commit("ID_88117422", MindmapContextActionProvider.RenameActionId, "");

        Assert.True(result.Completed, result.Error);
        Assert.Equal("", _project.Document.Find("ID_88117422")!.Text);
    }

    [Fact]
    public async Task Commit_Link_StoresItMapRelative()
    {
        // Picked as a project-relative id; stored as Freeplane would (Requirement 12.1).
        var result = await Commit("ID_88117422", MindmapContextActionProvider.LinkActionId, "docs/architecture.mm");

        Assert.True(result.Completed, result.Error);
        Assert.Equal("architecture.mm", _project.Document.Find("ID_88117422")!.Link);
    }

    [Fact]
    public async Task Commit_OnANodeThatIsGone_Fails()
    {
        var result = await Commit("ID_nope", MindmapContextActionProvider.RenameActionId, "x");

        Assert.False(result.Completed);
        Assert.Equal("The node no longer exists.", result.Error);
    }
}
