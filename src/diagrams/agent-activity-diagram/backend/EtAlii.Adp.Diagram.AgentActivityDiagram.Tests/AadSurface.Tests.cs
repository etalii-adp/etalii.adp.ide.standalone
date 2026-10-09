using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram.Tests;

/// <summary>
/// What the shell is offered for an activity file (agent-activity-diagram Requirements 3.7, 9.1,
/// 9.4 and 9.6): the palette, an element's menu, a gesture's action, the property grid's rows and
/// the findings.
/// </summary>
public sealed class AadSurfaceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("aad-surface-").FullName;
    private readonly ServiceProvider _services = Services();

    private string Body => System.IO.Path.Combine(_root, "work.aad");

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection().AddCommands();
        services.AddAgentActivityDiagram();
        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private Task GivenOneDayAsync() => File.WriteAllTextAsync(Body, AadDocumentTests.Fixture("one-day.aad"), TestContext.Current.CancellationToken);

    private ContextTarget Target(string elementId) => new(
        ContextScope.DiagramElement, Body, IsContainer: false, SourceId: default, _root, default, elementId, Diagram.AgentActivity.Origin);

    private AadContextActionProvider Actions => new(_services.GetRequiredService<IHistoryStackStore>(), _services.GetRequiredService<IAadDocumentStore>());

    private async Task<List<ContextActionDefinition>> MenuAsync(string elementId) =>
        [.. (await Actions.DiscoverAsync(Target(elementId), TestContext.Current.CancellationToken)).SelectMany(group => group.Actions)];

    [Fact]
    public void ThePalette_HasOneToolPerKindOfElement_EachDroppingItsOwnAdd()
    {
        Assert.Equal(
            ["project:add-project", "specification:add-specification", "agent:add-agent", "location:add-location", "environment:add-environment"],
            new AadToolboxProvider().Items.Select(item => $"{item.Id}:{item.DropActionId}"));
    }

    [Fact]
    public async Task AnElementsMenu_IsTheDefinitions_LessWhatThisHostDoesElsewhere()
    {
        // Arrange.
        await GivenOneDayAsync();

        // Act: a locked specification, an element the layout places, and a task.
        var locked = await MenuAsync("s-knowledge");
        var free = await MenuAsync("a-dev2");
        var task = await MenuAsync("t-k2");

        // Assert: no Lock (a drag locks) and no Open link (its symbol opens it); Unlock only where there is a lock.
        Assert.Equal(["editLabel", "create-child", "unpin", "delete"], locked.Select(action => action.Id));
        Assert.True(locked.Single(action => action.Id == "unpin").Available);
        Assert.False(free.Single(action => action.Id == "unpin").Available);
        Assert.Equal("Its position is not locked: the layout places it.", free.Single(action => action.Id == "unpin").UnavailableReason);

        // A task is not placed, and its status is chosen from a submenu of the definition's statuses, in its order.
        Assert.Equal(["editLabel", "setTaskStatus", "delete"], task.Select(action => action.Id));
        Assert.Equal(
            ["setTaskStatus:progressing=Progressing", "setTaskStatus:pending=Pending", "setTaskStatus:inputRequired=Input Required", "setTaskStatus:finished=Finished"],
            task.Single(action => action.Id == "setTaskStatus").Children!.SelectMany(group => group.Actions).Select(action => $"{action.Id}={action.Label}"));
    }

    [Fact]
    public async Task AGesturesTarget_DiscoversWhatTheGestureExecutes_AndExecutingItWritesTheFile()
    {
        // Arrange.
        await GivenOneDayAsync();
        var token = TestContext.Current.CancellationToken;

        // Assert: each kind of gesture target offers its own actions and nothing else.
        Assert.Equal(["add-project", "add-specification", "add-agent", "add-location", "add-environment", "unpinAll"], (await MenuAsync(GestureIds.Placement(10, 20))).Select(action => action.Id));
        Assert.Equal(["connect"], (await MenuAsync("rel:s-rider->a-dev2")).Select(action => action.Id));
        Assert.Equal(["show-archived", "hide-archived"], (await MenuAsync(AadElementMapper.ViewId)).Select(action => action.Id));
        Assert.Equal(["collapse-group", "expand-group"], (await MenuAsync(AadGroupTarget.Of("s-knowledge", "finished"))).Select(action => action.Id));

        // Act: a fold, the switch, and a line the rules refuse.
        var folded = await Actions.ExecuteAsync(Target(AadGroupTarget.Of("s-knowledge", "progressing")), "collapse-group", token);
        var shown = await Actions.ExecuteAsync(Target(AadElementMapper.ViewId), "show-archived", token);
        var refused = await Actions.ExecuteAsync(Target("rel:s-rider->a-dev2"), "connect", token);

        // Assert.
        Assert.IsType<ContextExecutionCompleted>(folded);
        Assert.IsType<ContextExecutionCompleted>(shown);
        var model = _services.GetRequiredService<IAadDocumentStore>().GetOrLoad(Body).Model;
        Assert.Contains("progressing", model.Collapsed["s-knowledge"]);
        Assert.True(model.ShowArchived);
        Assert.Contains("already joined", Assert.IsType<ContextExecutionFailed>(refused).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARename_IsAskedInPlaceForAnElement_AndInADialogForARow_AndAnEmptyNameIsRefused()
    {
        // Arrange.
        await GivenOneDayAsync();
        var token = TestContext.Current.CancellationToken;

        // Act.
        var element = Assert.IsType<ContextExecutionRequiresInput>(await Actions.ExecuteAsync(Target("l-kd"), "editLabel", token)).Request;
        var row = Assert.IsType<ContextExecutionRequiresInput>(await Actions.ExecuteAsync(Target("pr-150"), "editLabel", token)).Request;
        var empty = await Actions.ValidateAsync(Target("l-kd"), "editLabel", "  ", token);
        var committed = await Actions.CommitAsync(Target("l-kd"), "editLabel", "features/knowledge", "", token);

        // Assert: a location is named by its branch, and that is what a rename writes.
        Assert.Equal(("features/knowledge-designer", "l-kd"), (element.InitialValue, element.InlineLabelElementId));
        Assert.Equal(("150: Knowledge designer, tasks 2 and 6", ""), (row.InitialValue, row.InlineLabelElementId));
        Assert.False(empty.Valid);
        Assert.True(committed.Completed, committed.Error);
        Assert.Contains("    branch: features/knowledge\r\n", await File.ReadAllTextAsync(Body, token), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePropertyGrid_ListsATypesAttributes_AStatusAsAChoiceAndARelationByName()
    {
        // Arrange.
        await GivenOneDayAsync();
        var grid = new AadContextPropertyProvider(_services.GetRequiredService<IHistoryStackStore>(), _services.GetRequiredService<IAadDocumentStore>());

        // Act.
        var rows = await grid.DescribeAsync(Target("s-knowledge"), TestContext.Current.CancellationToken);
        var set = await grid.SetAsync(Target("s-knowledge"), "status", "Finished", TestContext.Current.CancellationToken);

        // Assert: in the definition's groups and order.
        Assert.Equal(
            ["name=Knowledge designer", "status=Progressing", "project=etalii.adp.ide.standalone", "link=.spec-workflow/specs/knowledge-designer/requirements.md"],
            rows.Select(row => $"{row.Id}={row.Value}"));
        Assert.Equal(["title", "status", "updated", "link"], (await grid.DescribeAsync(Target("t-k2"), TestContext.Current.CancellationToken)).Select(row => row.Id));
        var status = rows.Single(row => row.Id == "status");
        Assert.Equal(ContextPropertyEditor.Choice, status.Editor);
        Assert.Equal(["Pending", "Progressing", "Input Required", "Finished", "Archived"], status.Candidates);
        Assert.False(rows.Single(row => row.Id == "project").IsEditable);
        Assert.True(set.IsSuccess, set.Error);
        Assert.Contains("    status: finished\r\n    tasks:", await File.ReadAllTextAsync(Body, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public void AFileThatBreaksARule_StillReads_AndSaysWhereItBreaksIt()
    {
        // Arrange: an agent with no specification and no location, and a project nothing belongs to.
        var body = AadBody.Parse("agent-activity-diagram: 1\r\nprojects:\r\n  - id: p\r\n    name: Alone\r\nagents:\r\n  - id: a\r\n    name: Idle\r\n    specification: s-gone\r\n");

        // Act.
        var problems = AadValidator.Validate(body);

        // Assert: nothing is an error - a file an agent wrote opens whatever it says.
        Assert.DoesNotContain(problems.Where(problem => problem.RuleId.StartsWith("aad.", StringComparison.Ordinal)), problem => problem.Severity == DiagramProblemSeverity.Error);
        Assert.Contains(problems, problem => problem.RuleId == "aad.project-without-specification" && problem.Message.Contains("Alone", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem is { RuleId: "aad.agent-without-location", Location: DiagramProblemLineLocation { Number: 6 } });
        // The reference to a specification the file does not have is the reader's finding, on its line.
        Assert.Contains(problems, problem => problem.Message.Contains("s-gone", StringComparison.Ordinal) && problem.Location is DiagramProblemLineLocation { Number: 8 });
    }
}
