using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
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
        // Act.
        var ids = (await Discover("ID_88117422")).Select(action => action.Id).ToList();

        // Assert.
        Assert.Equal(
            [MindmapContextActionProvider.AddChildActionId, MindmapContextActionProvider.AddSiblingActionId,
             MindmapContextActionProvider.RenameActionId, MindmapContextActionProvider.DeleteActionId,
             MindmapContextActionProvider.EditNotesActionId, MindmapContextActionProvider.LinkActionId],
            ids);
    }

    [Fact]
    public async Task Discover_TheRoot_OffersNoSiblingAndNoDelete()
    {
        // Act.
        var ids = (await Discover(_project.Document.Root.Id)).Select(action => action.Id).ToList();

        // Assert.
        Assert.DoesNotContain(MindmapContextActionProvider.AddSiblingActionId, ids);
        Assert.DoesNotContain(MindmapContextActionProvider.DeleteActionId, ids);
        Assert.Contains(MindmapContextActionProvider.AddChildActionId, ids);
    }

    [Fact]
    public async Task Discover_ALinkedNode_OffersUnlink()
    {
        // Act.
        var ids = (await Discover("ID_88117420")).Select(action => action.Id).ToList();

        // Assert.
        Assert.Contains(MindmapContextActionProvider.UnlinkActionId, ids);
    }

    [Fact]
    public async Task Discover_ANodeWithChildren_OffersCollapseOrExpand_NamedByItsCurrentState()
    {
        // Act and assert, step by step.
        var fold = (await Discover("ID_411002937")).Single(action => action.Id == MindmapContextActionProvider.ToggleFoldActionId);
        Assert.Equal("Collapse", fold.Label);

        var folded = (await Discover("ID_88117425")).Single(action => action.Id == MindmapContextActionProvider.ToggleFoldActionId);
        Assert.Equal("Expand", folded.Label); // FOLDED="true" in the file seeds it
    }

    [Fact]
    public async Task Discover_CarriesFreeplanesShortcutsAsData()
    {
        // Act.
        // Requirement 8.4: the client holds no key table; these are the only source.
        var actions = await Discover("ID_88117422");

        // Assert.
        Assert.Equal("Insert", actions.Single(a => a.Id == MindmapContextActionProvider.AddChildActionId).Shortcut!.Key);
        Assert.Equal("Enter", actions.Single(a => a.Id == MindmapContextActionProvider.AddSiblingActionId).Shortcut!.Key);
        Assert.Equal("F2", actions.Single(a => a.Id == MindmapContextActionProvider.RenameActionId).Shortcut!.Key);
        Assert.Equal("Delete", actions.Single(a => a.Id == MindmapContextActionProvider.DeleteActionId).Shortcut!.Key);
    }

    [Fact]
    public async Task Discover_AnUnknownNode_OffersNothing()
    {
        // Arrange, act and assert.
        Assert.Empty(await Discover("ID_nope"));
    }

    [Fact]
    public async Task Discover_AnotherModulesDocument_OffersNothing_InsteadOfParsingIt()
    {
        // Arrange.
        // Providers are consulted for every diagram element, including other modules'. This
        // one used to parse the foreign file as XML and throw MindmapFormatException on every
        // action lookup over a .tml timeline.
        var foreignPath = IoPath.Combine(_project.Root, "docs", "roadmap.tml");
        await File.WriteAllTextAsync(foreignPath, "timeline: 1\r\nelements:\r\n  - id: aaa\r\n    label: First\r\n    begin: 2026-01-01\r\n    row: 0\r\n", TestContext.Current.CancellationToken);
        var target = new ContextTarget(
            ContextScope.DiagramElement, foreignPath, IsContainer: false, SourceId: default, _project.Root, _project.WatchId, "aaa");

        // Act.
        var groups = await _project.Provider.DiscoverAsync(target, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(groups);
    }

    // ---- what executing does -----------------------------------------------------------

    [Fact]
    public async Task Execute_Rename_AsksWithTheCurrentText()
    {
        // Act.
        var result = Assert.IsType<ContextExecutionRequiresInput>(await Execute("ID_88117422", MindmapContextActionProvider.RenameActionId));

        // Assert.
        Assert.Equal("ICommand", result.Request.InitialValue);
    }

    [Fact]
    public async Task Execute_EveryPromptEditingALabelSaysSo_AndNotesStillDoesNot()
    {
        // Arrange.
        // THIS TEST USED TO CLAIM THE OPPOSITE, and the premise it gave was true when it was
        // written: "add child and add sibling ask for text that does not exist yet - there is
        // no label to replace, because there is no node". The node now exists by the time the
        // prompt is raised, because the add runs at execute time under a name read off its
        // siblings - so all three of these ARE editing a label on screen, and only notes is
        // still a value about the node rather than its label (inline-rename Requirements 3.1,
        // 2.5). The contrast the test was written for survives; the line it falls on moved.
        const string nodeId = "ID_88117422";

        // Act.
        var rename = Assert.IsType<ContextExecutionRequiresInput>(await Execute(nodeId, MindmapContextActionProvider.RenameActionId));
        var addChild = Assert.IsType<ContextExecutionRequiresInput>(await Execute(nodeId, MindmapContextActionProvider.AddChildActionId));
        var addSibling = Assert.IsType<ContextExecutionRequiresInput>(await Execute(nodeId, MindmapContextActionProvider.AddSiblingActionId));
        var editNotes = Assert.IsType<ContextExecutionRequiresInput>(await Execute(nodeId, MindmapContextActionProvider.EditNotesActionId));

        // Assert.
        Assert.Equal(nodeId, rename.Request.InlineLabelElementId);
        Assert.Equal("", editNotes.Request.InlineLabelElementId);

        // The two adds name the NEW node, never the one that was selected - naming the selected
        // one would open the editor over the parent and rename it on commit.
        Assert.NotEqual("", addChild.Request.InlineLabelElementId);
        Assert.NotEqual(nodeId, addChild.Request.InlineLabelElementId);
        Assert.NotEqual("", addSibling.Request.InlineLabelElementId);
        Assert.NotEqual(nodeId, addSibling.Request.InlineLabelElementId);

        // And both commit as a rename. Left as the invoked action, the commit adds a SECOND
        // node instead of naming the first - which is the whole reason CommitActionId exists.
        Assert.Equal(MindmapContextActionProvider.RenameActionId, addChild.Request.CommitActionId);
        Assert.Equal(MindmapContextActionProvider.RenameActionId, addSibling.Request.CommitActionId);
        Assert.Equal("", rename.Request.CommitActionId);
    }

    [Fact]
    public async Task Execute_AddChild_CreatesTheNodeAtOnce_NamedFromItsSiblings()
    {
        // Arrange. The fixture's root has children; the new one joins them.
        var root = _project.Document.Root;
        var before = root.Children.Count;

        // Act.
        var result = Assert.IsType<ContextExecutionRequiresInput>(
            await Execute(root.Id, MindmapContextActionProvider.AddChildActionId));

        // Assert. The node is in the document BEFORE anybody answers the prompt - that is what
        // makes the prompt an inline editor over something visible rather than a dialog.
        Assert.Equal(before + 1, root.Children.Count);
        var added = _project.Document.Find(result.Request.InlineLabelElementId);
        Assert.NotNull(added);

        // Named, not blank, and the prompt opens on that same name so the editor is prefilled
        // with what the node actually says.
        Assert.False(string.IsNullOrWhiteSpace(added.Text));
        Assert.Equal(added.Text, result.Request.InitialValue);

        // Undoable as one step: the add is a command like any other, so a user who did not
        // want it presses undo rather than deleting the node they were just given.
        Assert.True(_project.History.CanUndo);
    }

    [Fact]
    public async Task Execute_AddChildTwiceWithoutRenaming_DoesNotProduceTwoNodesWithOneName()
    {
        // The uniqueness pass, end to end: accept both defaults and the second must still be
        // distinguishable from the first, in the tree and to anything looking one up by text.
        var root = _project.Document.Root;

        var first = Assert.IsType<ContextExecutionRequiresInput>(await Execute(root.Id, MindmapContextActionProvider.AddChildActionId));
        var second = Assert.IsType<ContextExecutionRequiresInput>(await Execute(root.Id, MindmapContextActionProvider.AddChildActionId));

        Assert.NotEqual(
            _project.Document.Find(first.Request.InlineLabelElementId)!.Text,
            _project.Document.Find(second.Request.InlineLabelElementId)!.Text);
    }

    [Fact]
    public async Task Execute_DeleteOnALeaf_RemovesItAtOnce_AndItIsUndoable()
    {
        // Act.
        var result = await Execute("ID_88117422", MindmapContextActionProvider.DeleteActionId);

        // Assert.
        Assert.IsType<ContextExecutionCompleted>(result);
        Assert.Null(_project.Document.Find("ID_88117422"));
        Assert.True(_project.History.CanUndo);
    }

    [Fact]
    public async Task Execute_DeleteOnABranch_AsksFirst_NamingWhatGoes()
    {
        // Act.
        var result = Assert.IsType<ContextExecutionRequiresConfirmation>(await Execute("ID_411002937", MindmapContextActionProvider.DeleteActionId));

        // Assert.
        Assert.Contains("9 nodes", result.Request.Message, StringComparison.Ordinal);
        Assert.True(result.Request.Danger);
        Assert.NotNull(_project.Document.Find("ID_411002937"));
    }

    [Fact]
    public async Task Execute_ToggleFold_ChangesViewStateOnly_AndRecordsNothing()
    {
        // Arrange.
        // Requirements 9.4, 9.6: no command, no history entry, no write.
        var before = await File.ReadAllTextAsync(_project.BodyPath, TestContext.Current.CancellationToken);

        // Act.
        await Execute("ID_411002937", MindmapContextActionProvider.ToggleFoldActionId);

        // Assert.
        Assert.True(_project.Views.Find(_project.WatchId, _project.BodyPath)!.IsFolded("ID_411002937"));
        Assert.False(_project.History.CanUndo);
        Assert.Equal(before, await File.ReadAllTextAsync(_project.BodyPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Execute_ToggleFold_IsPerConnection()
    {
        // Arrange.
        await Execute("ID_411002937", MindmapContextActionProvider.ToggleFoldActionId);

        // Act.
        var other = _project.Views.For(ShortGuid.NewShortGuid(), _project.BodyPath, _project.Document);

        // Assert.
        Assert.False(other.IsFolded("ID_411002937"));
    }

    [Fact]
    public async Task Execute_Unlink_DispatchesACommand_SoItIsUndoable()
    {
        // Arrange.
        var result = await Execute("ID_88117420", MindmapContextActionProvider.UnlinkActionId);

        // Act and assert, step by step.
        Assert.IsType<ContextExecutionCompleted>(result);
        Assert.Null(_project.Document.Find("ID_88117420")!.Link);

        await _project.History.UndoAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(_project.Document.Find("ID_88117420")!.Link);
    }

    [Fact]
    public async Task Execute_Link_OffersTheProjectsFilesToPickFrom()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_project.Root, "README.md"), "", TestContext.Current.CancellationToken);

        // Act.
        var result = Assert.IsType<ContextExecutionRequiresChoice>(await Execute("ID_88117422", MindmapContextActionProvider.LinkActionId));

        // Assert.
        Assert.Contains(result.Request.Options, option => option.Id == "README.md" && option.Selectable);
        var docs = Assert.Single(result.Request.Options, option => option.Id == "docs");
        Assert.Contains(docs.Children!, option => option.Id == "docs/architecture.mm");
    }

    // ---- what committing does -----------------------------------------------------------

    [Fact]
    public async Task Commit_AddChild_AddsAnUndoableNode()
    {
        // Act.
        var result = await Commit("ID_88117422", MindmapContextActionProvider.AddChildActionId, "a child");

        // Assert.
        Assert.True(result.Completed, result.Error);
        Assert.Equal("a child", _project.Document.Find("ID_88117422")!.Children.Single().Text);
        Assert.True(_project.History.CanUndo);
    }

    [Fact]
    public async Task Commit_Rename_ToEmpty_IsAccepted()
    {
        // Act.
        var result = await Commit("ID_88117422", MindmapContextActionProvider.RenameActionId, "");

        // Assert.
        Assert.True(result.Completed, result.Error);
        Assert.Equal("", _project.Document.Find("ID_88117422")!.Text);
    }

    [Fact]
    public async Task Commit_Link_StoresItMapRelative()
    {
        // Act.
        // Picked as a project-relative id; stored as Freeplane would (Requirement 12.1).
        var result = await Commit("ID_88117422", MindmapContextActionProvider.LinkActionId, "docs/architecture.mm");

        // Assert.
        Assert.True(result.Completed, result.Error);
        Assert.Equal("architecture.mm", _project.Document.Find("ID_88117422")!.Link);
    }

    [Fact]
    public async Task Commit_OnANodeThatIsGone_Fails()
    {
        // Act.
        var result = await Commit("ID_nope", MindmapContextActionProvider.RenameActionId, "x");

        // Assert.
        Assert.False(result.Completed);
        Assert.Equal("The node no longer exists.", result.Error);
    }
}
