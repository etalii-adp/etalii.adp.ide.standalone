using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Context;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// The actions, walked along the real discover-then-execute-then-commit path rather than
/// asserted against a cached model.
/// </summary>
public class DependencyGraphContextActionProviderTests : IDisposable
{
    private readonly string _workspace = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-dgr-actions-" + Guid.NewGuid().ToString("N"));

    private readonly DependencyGraphDocumentStore _store = new();
    private readonly DependencyGraphContextActionProvider _actions;
    private readonly HistoryStackStore _historyStacks;

    public DependencyGraphContextActionProviderTests()
    {
        Directory.CreateDirectory(_workspace);
        _historyStacks = new HistoryStackStore(new DependencyGraphTestDispatcher(_store));
        _actions = new DependencyGraphContextActionProvider(_historyStacks, _store);
    }

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
            row: 2
        relations:
          - id: ccc
            from: aaa
            to: bbb
            label: verifies tokens with
        """;

    private string Write(string content = Graph)
    {
        var path = IoPath.Combine(_workspace, "services.dgr");
        File.WriteAllText(path, content);
        _store.Forget(path);
        return path;
    }

    private readonly ShortGuid _watchId = ShortGuid.NewShortGuid();

    private ContextTarget Target(string path, string elementId) =>
        new(ContextScope.DiagramElement, path, IsContainer: false, SourceId: default, _workspace, _watchId, elementId);

    private async Task<IReadOnlyList<string>> ActionIdsFor(string path, string elementId)
    {
        var groups = await _actions.DiscoverAsync(Target(path, elementId), CancellationToken.None);
        return groups.SelectMany(group => group.Actions).Select(action => action.Id).ToList();
    }

    [Fact]
    public async Task ANode_OffersItsEditsAndItsAdditions()
    {
        // Arrange & act.
        var path = Write();
        var ids = await ActionIdsFor(path, "aaa");

        // Assert.
        // Two groups: what changes this node, then what adds the next one. Three actions where
        // the timeline had five - give-it-an-end and remove-its-end went with the dates, and
        // nothing replaces them.
        Assert.Equal(
            [
                DependencyGraphContextActionProvider.RenameActionId,
                DependencyGraphContextActionProvider.RemoveActionId,
                DependencyGraphContextActionProvider.AddAfterActionId,
                DependencyGraphContextActionProvider.AddBelowActionId,
            ],
            ids);
    }

    [Fact]
    public async Task ADependency_OffersRelabelAndDisconnect()
    {
        // Arrange & act.
        var path = Write();
        var ids = await ActionIdsFor(path, "ccc");

        // Assert.
        Assert.Equal(
            [DependencyGraphContextActionProvider.RelabelActionId, DependencyGraphContextActionProvider.DisconnectActionId],
            ids);
    }

    [Fact]
    public async Task EmptyCanvas_OffersOneAdd_AndNoMoment()
    {
        // Arrange & act.
        var path = Write();
        var ids = await ActionIdsFor(path, DependencyGraphNewPlacement.IdFor(700, 3));

        // Assert.
        var only = Assert.Single(ids);
        Assert.Equal(DependencyGraphContextActionProvider.AddElementActionId, only);
    }

    [Fact]
    public async Task AnotherTypesFile_OffersNothing()
    {
        // Arrange.
        // A provider consulted for every element in its scope answers with nothing rather than
        // parsing another notation's file.
        var foreign = IoPath.Combine(_workspace, "plan.tml");
        await File.WriteAllTextAsync(foreign, "timeline: 1\nelements: []\n", TestContext.Current.CancellationToken);

        // Act & assert.
        Assert.Empty(await ActionIdsFor(foreign, "aaa"));
    }

    [Fact]
    public async Task AddingOnAPlacement_LandsWhereItWasDropped_WithNothingAsked()
    {
        // Arrange.
        var path = Write();

        // Act.
        var result = await _actions.ExecuteAsync(
            Target(path, DependencyGraphNewPlacement.IdFor(-180.5, 4)),
            DependencyGraphContextActionProvider.AddElementActionId,
            CancellationToken.None);

        // Assert.
        Assert.IsType<ContextExecutionCompleted>(result);
        var added = _store.GetOrLoad(path).Model.Elements.Single(element => element.Id is not ("aaa" or "bbb"));
        Assert.Equal(-180.5d, added.X);
        Assert.Equal(4, added.Row);
    }

    [Fact]
    public async Task AddingFromTheMenu_AsksForALabelRatherThanACoordinate()
    {
        // Arrange & act.
        // The timeline asked for a begin here because a begin was the one thing it could not
        // invent. A coordinate it can - so asking the user to type a canvas number would be
        // asking them to do the canvas's job.
        var path = Write();
        var result = await _actions.ExecuteAsync(
            Target(path, "aaa"), DependencyGraphContextActionProvider.AddElementActionId, CancellationToken.None);

        // Assert.
        var input = Assert.IsType<ContextExecutionRequiresInput>(result);
        Assert.Equal("Label", input.Request.FieldLabel);
    }

    [Fact]
    public async Task CommittingTheMenuAdd_PlacesItBesideTheAnchorWithTheTypedLabel()
    {
        // Arrange.
        var path = Write();

        // Act.
        var result = await _actions.CommitAsync(
            Target(path, "aaa"), DependencyGraphContextActionProvider.AddElementActionId, "Search service", "", CancellationToken.None);

        // Assert.
        Assert.True(result.Completed, result.Error);
        var added = _store.GetOrLoad(path).Model.Elements.Single(element => element.Label == "Search service");
        Assert.Equal(240d + DependencyGraphContextActionProvider.XStep, added.X);
        Assert.Equal(0, added.Row);
    }

    [Fact]
    public async Task OnlyTheTwoLabelPrompts_AreMarkedForInlineEditing()
    {
        // Arrange.
        // All three prompts asked together on purpose: a marker on the right action proves
        // nothing if a neighbour has quietly acquired one. Rename and relabel ask for text that
        // IS on screen; add-node asks for the label of an element that does not exist yet, so an
        // editor drawn in place of it would have nothing to sit on.
        //
        // Add-after and add-below are absent because they ask for nothing at all - they dispatch
        // directly, inventing the coordinate rather than asking for it, so there is no prompt on
        // which a marker could appear.
        var path = Write();

        // Act.
        var rename = await _actions.ExecuteAsync(Target(path, "aaa"), DependencyGraphContextActionProvider.RenameActionId, CancellationToken.None);
        var relabel = await _actions.ExecuteAsync(Target(path, "ccc"), DependencyGraphContextActionProvider.RelabelActionId, CancellationToken.None);
        var addNode = await _actions.ExecuteAsync(Target(path, ""), DependencyGraphContextActionProvider.AddElementActionId, CancellationToken.None);

        // Assert.
        Assert.Equal("aaa", Assert.IsType<ContextExecutionRequiresInput>(rename).Request.InlineLabelElementId);
        Assert.Equal("ccc", Assert.IsType<ContextExecutionRequiresInput>(relabel).Request.InlineLabelElementId);
        Assert.Equal("", Assert.IsType<ContextExecutionRequiresInput>(addNode).Request.InlineLabelElementId);
    }

    [Fact]
    public async Task Tab_AddsANodeAStepToTheRight_ThatTheSelectedOneDependsOn()
    {
        // Arrange.
        var path = Write();

        // Act.
        var result = await _actions.ExecuteAsync(
            Target(path, "aaa"), DependencyGraphContextActionProvider.AddAfterActionId, CancellationToken.None);

        // Assert.
        // No date arithmetic anywhere: a plain step along x, and the same row.
        Assert.IsType<ContextExecutionCompleted>(result);
        var model = _store.GetOrLoad(path).Model;
        var added = model.Elements.Single(element => element.Id is not ("aaa" or "bbb"));
        Assert.Equal(240d + DependencyGraphContextActionProvider.XStep, added.X);
        Assert.Equal(0, added.Row);
        Assert.Contains(model.Relations, relation => relation.From == "aaa" && relation.To == added.Id);
    }

    [Fact]
    public async Task Enter_AddsANodeOneRowBelow_AtTheSameCoordinate()
    {
        // Arrange.
        var path = Write();

        // Act.
        var result = await _actions.ExecuteAsync(
            Target(path, "aaa"), DependencyGraphContextActionProvider.AddBelowActionId, CancellationToken.None);

        // Assert.
        Assert.IsType<ContextExecutionCompleted>(result);
        var model = _store.GetOrLoad(path).Model;
        var added = model.Elements.Single(element => element.Id is not ("aaa" or "bbb"));
        Assert.Equal(240d, added.X);
        Assert.Equal(1, added.Row);
        Assert.Contains(model.Relations, relation => relation.From == "aaa" && relation.To == added.Id);
    }

    [Fact]
    public async Task ARelationGestureBetweenTwoNodes_ConnectsThemInTheGesturesDirection()
    {
        // Arrange.
        var path = Write();
        var gesture = DependencyGraphRelationGesture.IdFor("bbb", "aaa");

        // Act.
        var result = await _actions.ExecuteAsync(
            Target(path, gesture), DependencyGraphContextActionProvider.ConnectActionId, CancellationToken.None);

        // Assert.
        // Dragged from bbb to aaa means bbb depends on aaa, and nothing along the path may
        // normalise that.
        Assert.IsType<ContextExecutionCompleted>(result);
        var added = _store.GetOrLoad(path).Model.Relations.Single(relation => relation.Id != "ccc");
        Assert.Equal("bbb", added.From);
        Assert.Equal("aaa", added.To);
    }

    [Fact]
    public async Task ARelationGestureOntoEmptyCanvas_CreatesTheDependencyAndRelatesIt_AsOneUndo()
    {
        // Arrange.
        var path = Write();
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        var gesture = DependencyGraphRelationGesture.IdFor("aaa", DependencyGraphNewPlacement.IdFor(900, 5));

        // Act.
        var result = await _actions.ExecuteAsync(
            Target(path, gesture), DependencyGraphContextActionProvider.ConnectActionId, CancellationToken.None);
        var model = _store.GetOrLoad(path).Model;
        await _historyStacks.Get(_workspace).UndoAsync(CancellationToken.None);

        // Assert.
        Assert.IsType<ContextExecutionCompleted>(result);
        var created = model.Elements.Single(element => element.Id is not ("aaa" or "bbb"));
        Assert.Equal(900d, created.X);
        Assert.Equal(5, created.Row);
        Assert.Contains(model.Relations, relation => relation.From == "aaa" && relation.To == created.Id);
        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ARelationGestureFromEmptyCanvas_MakesTheNewNodeTheDependent()
    {
        // Arrange.
        // Dragged into an existing node: what was created is the thing that depends on it.
        var path = Write();
        var gesture = DependencyGraphRelationGesture.IdFor(DependencyGraphNewPlacement.IdFor(-300, 1), "aaa");

        // Act.
        var result = await _actions.ExecuteAsync(
            Target(path, gesture), DependencyGraphContextActionProvider.ConnectActionId, CancellationToken.None);

        // Assert.
        Assert.IsType<ContextExecutionCompleted>(result);
        var model = _store.GetOrLoad(path).Model;
        var created = model.Elements.Single(element => element.Id is not ("aaa" or "bbb"));
        Assert.Equal(-300d, created.X);
        Assert.Contains(model.Relations, relation => relation.From == created.Id && relation.To == "aaa");
    }

    [Fact]
    public async Task ARelationGestureFromAVanishedNode_FailsWithAReason()
    {
        // Arrange & act.
        var path = Write();
        var gesture = DependencyGraphRelationGesture.IdFor("ghost", DependencyGraphNewPlacement.IdFor(0, 0));

        // Act.
        var result = await _actions.ExecuteAsync(
            Target(path, gesture), DependencyGraphContextActionProvider.ConnectActionId, CancellationToken.None);

        // Assert.
        var failed = Assert.IsType<ContextExecutionFailed>(result);
        Assert.NotEmpty(failed.Message);
    }

    [Fact]
    public async Task RemovingAnUnconnectedNode_HappensAtOnce_WithNoConfirmation()
    {
        // Arrange.
        // The trap this pins: a Completed execution never reaches the commit leg, so an action
        // that answered Completed without dispatching would silently do nothing.
        var path = Write("dependencies: 1\r\nelements:\r\n  - id: lonely\r\n    label: Alone\r\n    x: 0\r\n    row: 0\r\n");

        // Act.
        var result = await _actions.ExecuteAsync(
            Target(path, "lonely"), DependencyGraphContextActionProvider.RemoveActionId, CancellationToken.None);

        // Assert.
        Assert.IsType<ContextExecutionCompleted>(result);
        Assert.Empty(_store.GetOrLoad(path).Model.Elements);
    }

    [Fact]
    public async Task RemovingAConnectedNode_ConfirmsAndSaysHowManyDependenciesGoWithIt()
    {
        // Arrange & act.
        var path = Write();
        var result = await _actions.ExecuteAsync(
            Target(path, "aaa"), DependencyGraphContextActionProvider.RemoveActionId, CancellationToken.None);

        // Assert.
        var confirmation = Assert.IsType<ContextExecutionRequiresConfirmation>(result);
        Assert.Contains("1 dependency", confirmation.Request.Message, StringComparison.Ordinal);
        Assert.NotEmpty(_store.GetOrLoad(path).Model.Elements);
    }

    [Fact]
    public async Task RenamingAsksThenCommits_AndIsOneUndoAway()
    {
        // Arrange.
        var path = Write();
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        // Act.
        var asked = await _actions.ExecuteAsync(
            Target(path, "aaa"), DependencyGraphContextActionProvider.RenameActionId, CancellationToken.None);
        var committed = await _actions.CommitAsync(
            Target(path, "aaa"), DependencyGraphContextActionProvider.RenameActionId, "Edge router", "", CancellationToken.None);
        var renamed = _store.GetOrLoad(path).Model.Elements.Single(element => element.Id == "aaa").Label;
        await _historyStacks.Get(_workspace).UndoAsync(CancellationToken.None);

        // Assert.
        Assert.IsType<ContextExecutionRequiresInput>(asked);
        Assert.True(committed.Completed, committed.Error);
        Assert.Equal("Edge router", renamed);
        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisconnectingRemovesTheDependency_AndIsOneUndoAway()
    {
        // Arrange.
        var path = Write();
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        // Act.
        var result = await _actions.ExecuteAsync(
            Target(path, "ccc"), DependencyGraphContextActionProvider.DisconnectActionId, CancellationToken.None);
        var afterDisconnect = _store.GetOrLoad(path).Model.Relations.Count;
        await _historyStacks.Get(_workspace).UndoAsync(CancellationToken.None);

        // Assert.
        Assert.IsType<ContextExecutionCompleted>(result);
        Assert.Equal(0, afterDisconnect);
        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ANodeOnlyActionCommittedAgainstADependency_SaysItDoesNotApply()
    {
        // Arrange & act.
        // What kind of thing is selected decides which actions apply, rather than a handler's
        // guess at what went wrong.
        var path = Write();
        var result = await _actions.CommitAsync(
            Target(path, "ccc"), DependencyGraphContextActionProvider.RenameActionId, "Nope", "", CancellationToken.None);

        // Assert.
        Assert.False(result.Completed);
        Assert.Contains("does not apply", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void NoActionIsAboutTime()
    {
        // Assert.
        // The deletion, pinned where it would come back. The timeline offered "Give it an end…"
        // and "Remove its end"; a fork that kept either would be offering a menu entry for a
        // field that does not exist.
        var actionIds = typeof(DependencyGraphContextActionProvider)
            .GetFields()
            .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

        Assert.NotEmpty(actionIds);
        foreach (var actionId in actionIds)
        {
            var words = actionId.Split('.')[^1].Split('-');
            foreach (var word in new[] { "end", "begin", "moment", "date", "time", "duration" })
            {
                Assert.DoesNotContain(word, words, StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
