using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram.Tests;

/// <summary>
/// The commands that write an activity file (agent-activity-diagram Requirements 3, 5, 6, 8 and 9),
/// each run through the project's history against a file on disk, and each judged by the file's
/// text afterwards: what a command must write, and every line it must leave alone.
/// </summary>
public sealed class AadCommandsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("aad-commands-").FullName;
    private readonly ServiceProvider _services = Services();

    private string Body => Path.Combine(_root, "work.aad");

    private IHistoryStack History => _services.GetRequiredService<IHistoryStackStore>().Get(_root);

    private IAadDocumentStore Documents => _services.GetRequiredService<IAadDocumentStore>();

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

    private async Task<string> GivenAsync(string text)
    {
        await File.WriteAllTextAsync(Body, text, TestContext.Current.CancellationToken);
        return text;
    }

    private Task<string> GivenOneDayAsync() => GivenAsync(AadDocumentTests.Fixture("one-day.aad"));

    private Task<CommandResult> RunAsync(ICommand command) => History.ExecuteAsync(command, TestContext.Current.CancellationToken);

    private Task<string> ReadAsync() => File.ReadAllTextAsync(Body, TestContext.Current.CancellationToken);

    private static string[] Lines(string text) => text.Split("\r\n");

    [Fact]
    public async Task ADroppedTool_AddsItsElement_LockedWhereItWasDropped()
    {
        // Arrange: a new file, the header alone.
        await GivenAsync("agent-activity-diagram: 1\r\n");

        // Act.
        var result = await RunAsync(new AddAadElementCommand(Body, "project", 120.4, -40.6));

        // Assert: the tool's initial name, a new id of 25 base 36 characters, and its lock under view.
        Assert.True(result.IsSuccess, result.Error);
        var lines = Lines(await ReadAsync());
        Assert.Equal("projects:", lines[1]);
        Assert.Matches("^  - id: [0-9a-z]{25}$", lines[2]);
        var id = lines[2]["  - id: ".Length..];
        Assert.Equal(["    name: New project", "view:", "  placements:", $"    - element: {id}", "      x: 120", "      y: -41", ""], lines[3..]);
    }

    [Fact]
    public async Task AnAddedTask_GoesLastInItsSpecification_StampedNow()
    {
        // Arrange.
        var before = await GivenOneDayAsync();

        // Act.
        var result = await RunAsync(new AddAadRowCommand(Body, "s-knowledge"));

        // Assert: four new lines after the specification's last task, and nothing else touched.
        Assert.True(result.IsSuccess, result.Error);
        var added = Lines(await ReadAsync()).Except(Lines(before)).ToList();
        Assert.Equal(4, added.Count);
        Assert.Matches("^      - id: [0-9a-z]{25}$", added[0]);
        Assert.Equal(["        title: New task", "        status: pending"], added[1..3]);
        Assert.Matches(@"^        updated: \d{4}-\d\d-\d\dT\d\d:\d\d:\d\d[+-]\d\d:\d\d$", added[3]);
        Assert.Empty(Lines(before).Except(Lines(await ReadAsync())));
        Assert.Equal(3, Documents.GetOrLoad(Body).Model.Elements.Single(element => element.Id == "s-knowledge").Rows.Count);
    }

    [Fact]
    public async Task ARenamedTask_IsStamped_AndARenamedElementIsNot()
    {
        // Arrange.
        var before = await GivenOneDayAsync();

        // Act.
        Assert.True((await RunAsync(new SetAadAttributeCommand(Body, "s-knowledge", "name", "Knowledge base designer"))).IsSuccess);
        Assert.True((await RunAsync(new SetAadAttributeCommand(Body, "t-k2", "title", "Trial two bindings"))).IsSuccess);

        // Assert: three lines differ - the name, the title, and the task's moment.
        var changed = Lines(await ReadAsync()).Except(Lines(before)).ToList();
        Assert.Equal(3, changed.Count);
        Assert.Equal("    name: Knowledge base designer", changed[0]);
        Assert.Equal("        title: Trial two bindings", changed[1]);
        Assert.StartsWith("        updated: ", changed[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStatusChosenByItsLabel_IsWrittenInItsStoredWord()
    {
        // Arrange.
        await GivenOneDayAsync();

        // Act: the property grid gives the label; the menu gives the member's name.
        Assert.True((await RunAsync(new SetAadAttributeCommand(Body, "s-knowledge", "status", "Input Required"))).IsSuccess);
        var byMenu = await RunAsync(new SetAadTaskStatusCommand(Body, "t-k2", "inputRequired"));
        var refused = await RunAsync(new SetAadAttributeCommand(Body, "s-knowledge", "status", "Paused"));

        // Assert.
        Assert.True(byMenu.IsSuccess, byMenu.Error);
        var model = Documents.GetOrLoad(Body).Model;
        Assert.Equal("inputRequired", model.Elements.Single(element => element.Id == "s-knowledge").Status);
        Assert.Equal("inputRequired", model.Elements.Single(element => element.Id == "s-knowledge").Rows.Single(row => row.Id == "t-k2").Status);
        // The specification, the task, and the task the file already had so.
        Assert.Equal(3, Lines(await ReadAsync()).Count(line => line.TrimStart() == "status: input-required"));
        Assert.False(refused.IsSuccess);
        Assert.Equal("'Paused' is not one of the values this can have.", refused.Error);
    }

    [Fact]
    public async Task ADrawnLine_IsTheKeyOnTheElementThatHoldsIt_WhicheverWayItWasDrawn()
    {
        // Arrange: the second project has no specification; s-rider is the first's.
        var before = await GivenOneDayAsync();
        Assert.True((await RunAsync(new DisconnectAadCommand(Body, "project:s-rider"))).IsSuccess);
        Assert.DoesNotContain("    project: p-standalone\r\n    name: Rider warnings cleanup", await ReadAsync(), StringComparison.Ordinal);

        // Act: drawn from the specification to the project, the reverse of how the file reads it.
        var result = await RunAsync(new ConnectAadCommand(Body, "s-rider", "p-adp"));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(before.Replace("    project: p-standalone\r\n    name: Rider warnings cleanup", "    project: p-adp\r\n    name: Rider warnings cleanup", StringComparison.Ordinal), await ReadAsync());
    }

    [Fact]
    public async Task ASecondSpecificationForAnAgent_IsRefused_AndSoIsAPairNoRelationJoins()
    {
        // Arrange.
        var before = await GivenOneDayAsync();

        // Act.
        var second = await RunAsync(new ConnectAadCommand(Body, "s-rider", "a-dev2"));
        var unrelated = await RunAsync(new ConnectAadCommand(Body, "p-adp", "e-fractal"));

        // Assert: each says why, and the file is as it was.
        Assert.False(second.IsSuccess);
        Assert.Contains("\"Developer 2\"", second.Error, StringComparison.Ordinal);
        Assert.Contains("\"Knowledge designer\"", second.Error, StringComparison.Ordinal);
        Assert.Equal("A project cannot be joined to an environment. A project is joined to a specification.", unrelated.Error);
        Assert.Equal(before, await ReadAsync());
    }

    [Fact]
    public async Task ARemovedSpecification_TakesItsTasksItsLockAndItsGroups_AndClearsTheKeyThatNamedIt()
    {
        // Arrange.
        await GivenOneDayAsync();

        // Act.
        var result = await RunAsync(new RemoveAadElementCommand(Body, "s-knowledge"));

        // Assert: nothing names it any more, the agent that worked on it stays, and the reader's
        // part keeps the one key that was not about it.
        Assert.True(result.IsSuccess, result.Error);
        var after = await ReadAsync();
        Assert.DoesNotContain("s-knowledge", after, StringComparison.Ordinal);
        Assert.DoesNotContain("t-k2", after, StringComparison.Ordinal);
        Assert.Contains("  - id: a-dev2\r\n    name: Developer 2\r\n    link: https://claude.ai/code\r\n", after, StringComparison.Ordinal);
        Assert.EndsWith("view:\r\n  showArchived: false\r\n", after, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADrag_MovesTheLockAnElementHas_AndGivesOneToAnElementWithout()
    {
        // Arrange.
        var before = await GivenOneDayAsync();

        // Act.
        Assert.True((await RunAsync(new PinAadElementCommand(Body, "s-knowledge", 300, -120.5))).IsSuccess);
        Assert.True((await RunAsync(new PinAadElementCommand(Body, "l-kd", 520, 40))).IsSuccess);
        var row = await RunAsync(new PinAadElementCommand(Body, "t-k2", 0, 0));

        // Assert: a row is not placed.
        Assert.Equal(
            before.Replace("      x: 240\r\n      y: -80\r\n", "      x: 300\r\n      y: -120\r\n    - element: l-kd\r\n      x: 520\r\n      y: 40\r\n", StringComparison.Ordinal),
            await ReadAsync());
        Assert.False(row.IsSuccess);
    }

    [Fact]
    public async Task Unlocking_RemovesTheLock_AndTheLastOneTakesItsListWithIt()
    {
        // Arrange.
        var before = await GivenOneDayAsync();

        // Act.
        var none = await RunAsync(new UnpinAadElementCommand(Body, "l-kd"));
        var all = await RunAsync(new UnpinAadElementCommand(Body));

        // Assert.
        Assert.Equal("Its position is not locked.", none.Error);
        Assert.True(all.IsSuccess, all.Error);
        Assert.Equal(before.Replace("  placements:\r\n    - element: s-knowledge\r\n      x: 240\r\n      y: -80\r\n", "", StringComparison.Ordinal), await ReadAsync());
        Assert.Empty(Documents.GetOrLoad(Body).Model.Placements);
    }

    [Fact]
    public async Task AGroupsState_IsKeptOnlyWhileItDiffersFromTheGroupsDefault()
    {
        // Arrange: the file keeps Pending unfolded for s-knowledge; Pending comes folded.
        var before = await GivenOneDayAsync();

        // Act: Progressing comes unfolded, so folding it is kept...
        Assert.True((await RunAsync(new SetAadGroupCommand(Body, "s-knowledge", "progressing", Collapsed: true))).IsSuccess);
        var kept = await ReadAsync();

        // ...and putting both back as they come leaves no entry, and no list.
        Assert.True((await RunAsync(new SetAadGroupCommand(Body, "s-knowledge", "progressing", Collapsed: false))).IsSuccess);
        Assert.True((await RunAsync(new SetAadGroupCommand(Body, "s-knowledge", "pending", Collapsed: true))).IsSuccess);

        // Assert.
        Assert.EndsWith("      collapsed: false\r\n    - element: s-knowledge\r\n      group: progressing\r\n      collapsed: true\r\n", kept, StringComparison.Ordinal);
        Assert.Equal(before.Replace("  groups:\r\n    - element: s-knowledge\r\n      group: pending\r\n      collapsed: false\r\n", "", StringComparison.Ordinal), await ReadAsync());
        Assert.Equal(["finished", "pending"], Documents.GetOrLoad(Body).Model.Collapsed["s-knowledge"].Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheArchivedSwitch_IsTheFirstKeyOfTheReadersPart_AndShowsTheArchivedSpecification()
    {
        // Arrange: a reader's part without the switch.
        await GivenAsync((await GivenOneDayAsync()).Replace("  showArchived: false\r\n", "", StringComparison.Ordinal));
        Documents.Reload(Body);
        var mapper = new AadElementMapper();
        Assert.DoesNotContain(mapper.Visible(Documents.GetOrLoad(Body).Model, DiagramViewport.Unbounded), element => element.Id == "s-rider");

        // Act.
        var result = await RunAsync(new SetAadShowArchivedCommand(Body, Show: true));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains("view:\r\n  showArchived: true\r\n  placements:\r\n", await ReadAsync(), StringComparison.Ordinal);
        Assert.Contains(mapper.Visible(Documents.GetOrLoad(Body).Model, DiagramViewport.Unbounded), element => element.Id == "s-rider");
    }

    [Fact]
    public async Task AnEdit_IsAppliedToWhatAnAgentWroteSinceTheFileWasRead_AndUndoneOnlyWhileNobodyWroteAfterIt()
    {
        // Arrange: the diagram has read the file; then an agent renames a project behind its back.
        var before = await GivenOneDayAsync();
        Documents.GetOrLoad(Body);
        var agents = before.Replace("    name: etalii.adp\r\n", "    name: etalii.adp (languages)\r\n", StringComparison.Ordinal);
        await File.WriteAllTextAsync(Body, agents, TestContext.Current.CancellationToken);

        // Act: an edit made in the diagram, which has not seen the agent's write.
        var result = await RunAsync(new SetAadAttributeCommand(Body, "a-dev2", "name", "Developer two"));

        // Assert: both changes are in the file (Requirement 8.6).
        Assert.True(result.IsSuccess, result.Error);
        var both = agents.Replace("    name: Developer 2\r\n", "    name: Developer two\r\n", StringComparison.Ordinal);
        Assert.Equal(both, await ReadAsync());

        // Undo restores what the edit was applied to: the agent's text, not the diagram's older reading.
        Assert.True((await History.UndoAsync(TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(agents, await ReadAsync());

        // And once an agent has written after an edit, that edit is no longer undone over the agent's write.
        Assert.True((await RunAsync(new SetAadAttributeCommand(Body, "a-dev2", "name", "Developer two"))).IsSuccess);
        var later = both.Replace("    name: Fractal\r\n", "    name: Fractal 2\r\n", StringComparison.Ordinal);
        await File.WriteAllTextAsync(Body, later, TestContext.Current.CancellationToken);
        var undone = await History.UndoAsync(TestContext.Current.CancellationToken);
        Assert.False(undone.IsSuccess);
        Assert.Equal(later, await ReadAsync());
    }
}
