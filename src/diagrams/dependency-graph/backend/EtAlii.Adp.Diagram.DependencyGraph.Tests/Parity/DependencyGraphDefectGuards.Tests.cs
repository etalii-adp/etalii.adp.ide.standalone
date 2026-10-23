using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests.Parity;

/// <summary>
/// Guards for the module's own defects B11 to B14 (dependency-graph-disl-fbl, task 2). Each was first written as the
/// right behaviour and seen to fail against today's code for the defect's reason (the failures are in the task's
/// implementation log); each pinned today's answer, with the right one written beside it, until the task that
/// repaired it turned it. Task 4 turned B12: every command undoes through the host's restore command, which writes
/// back the text as it was before the edit. Task 8 turned B11, B13 and B14: every command writes through the binding,
/// and each guard now runs the module's own command.
/// </summary>
public sealed class DependencyGraphDefectGuardsTests : IDisposable
{
    private readonly TestFolder _testFolder = TestFolder.Create("adp-dgr-guards");
    private readonly string _workspace;
    private readonly DependencyGraphDocumentStore _store = new(new FileWriter());
    private readonly IHistoryStackStore _historyStacks;

    public DependencyGraphDefectGuardsTests()
    {
        _workspace = _testFolder.Path;
        _historyStacks = TestHistory.For(s => { s.AddDependencyGraph(); s.AddSingleton<IDependencyGraphDocumentStore>(_store); });
    }

    public void Dispose() => _testFolder.Dispose();

    [Fact]
    public async Task B11_RemovingTheFirstHolderOfAnId_TakesOnlyTheDependenciesThatResolveToIt()
    {
        // Arrange.
        var path = await WriteAsync("b11.dgr", Text("inline/b11-holders-and-an-anonymous-node"));

        // Act: 'aaa' names the first holder, so removing it takes the dependencies on 'aaa'; the dependency with no
        // 'from', which names nothing, stays.
        var result = await _historyStacks.Get(ProjectRoot.From(_workspace)).ExecuteAsync(new RemoveDependencyGraphElementCommand(path, "aaa"), TestContext.Current.CancellationToken);
        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.DoesNotContain("of-the-first", text, StringComparison.Ordinal);
        Assert.DoesNotContain("of-the-second", text, StringComparison.Ordinal);
        Assert.Contains("  - id: no-from\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void B11_RemovingTheSecondHolderOfAnId_KeepsTheFirstHoldersDependencies()
    {
        // Arrange: the third node is the second holder of 'aaa', which the module addresses by its place; it is reached
        // through its body here, as a lookup by the written id finds the first holder.
        var body = DependencyGraphBody.Parse(Text("inline/b11-holders-and-an-anonymous-node"));
        var second = body.Model.Elements.Where(element => element.Type == "Node").ElementAt(2);

        // Act.
        var outcome = body.Change(new ModelChange.Remove(second.Id));

        // Assert: 'aaa' resolves to the first holder, so both dependencies stay.
        Assert.True(outcome.WasApplied, outcome.Refusal);
        Assert.Contains("  - id: of-the-first\n", body.Text, StringComparison.Ordinal);
        Assert.Contains("  - id: of-the-second\n", body.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void B11_RemovingANodeWithNoId_KeepsTheDependencyWithAnEndNotWritten()
    {
        // Arrange: the fourth node has no id; the module knows it by none, so it is reached through its body here.
        var body = DependencyGraphBody.Parse(Text("inline/b11-holders-and-an-anonymous-node"));
        var anonymous = body.Model.Elements.Single(element => element.Type == "Node" && DependencyGraphReading.TextOf(element, "storedId") == "");

        // Act.
        var outcome = body.Change(new ModelChange.Remove(anonymous.Id));

        // Assert: a dependency with no 'from' names nothing, so it does not go with a node that has no id.
        Assert.True(outcome.WasApplied, outcome.Refusal);
        Assert.Contains("  - id: no-from\n", body.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task B12_UndoingARelabel_RestoresTheCommentItTook()
    {
        // Arrange.
        var path = IoPath.Combine(_workspace, "commented.dgr");
        const string before = "dependencies: 1\nelements:\n  - id: aaa\n    label: A\n    x: 0\n    row: 0\n  - id: bbb\n    label: B\n    x: 240\n    row: 1\nrelations:\n  - id: uses\n    from: aaa\n    to: bbb\n    label: uses # why it does\n";
        await File.WriteAllTextAsync(path, before, TestContext.Current.CancellationToken);
        var history = _historyStacks.Get(ProjectRoot.From(_workspace));

        // Act.
        await history.ExecuteAsync(new RelabelDependencyGraphRelationCommand(path, "uses", "calls"), TestContext.Current.CancellationToken);
        await history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert: the bytes as they were, the comment the relabel took included.
        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    public static TheoryData<string> EditsThatCreateTheRelationsKey => new("connect", "add connected");

    [Theory]
    [MemberData(nameof(EditsThatCreateTheRelationsKey))]
    public async Task UndoingAnEditThatCreatedTheRelationsKey_TakesTheKeyOutAgain(string edit)
    {
        // Arrange.
        var path = IoPath.Combine(_workspace, "unrelated.dgr");
        const string before = "dependencies: 1\nelements:\n  - id: aaa\n    label: A\n    x: 0\n    row: 0\n  - id: bbb\n    label: B\n    x: 240\n    row: 1\n";
        await File.WriteAllTextAsync(path, before, TestContext.Current.CancellationToken);
        var history = _historyStacks.Get(ProjectRoot.From(_workspace));
        IDocumentEditCommand command = edit == "connect"
            ? new ConnectDependencyGraphElementsCommand(path, "uses", "aaa", "bbb", "")
            : new AddConnectedDependencyGraphElementCommand(path, "aaa", "ccc", "uses", 480, 2);

        // Act.
        await history.ExecuteAsync(command, TestContext.Current.CancellationToken);
        var edited = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        await history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert: the edit wrote the key, and the undo took it out with the dependency, byte for byte.
        Assert.Contains("\nrelations:\n", edited, StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task B13_TheFirstNodeOfAGraphWithNoElementsKey_CreatesTheKey()
    {
        // Arrange.
        var path = await WriteAsync("b13.dgr", Text("inline/no-elements-key"));

        // Act.
        var result = await _historyStacks.Get(ProjectRoot.From(_workspace)).ExecuteAsync(new AddDependencyGraphElementCommand(path, "newid", "New", 1, 2), TestContext.Current.CancellationToken);
        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        // Assert: an 'elements' key with the node under it, in a body that reads.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains("elements:\n  - id: newid\n", text, StringComparison.Ordinal);
        Assert.Equal(["newid"], DependencyGraphBody.Parse(text).Disl.Elements.Select(element => element.Id));
    }

    [Fact]
    public async Task B14_AKeyOnTheDashLineWithAWideGap_IsChangedInPlace()
    {
        // Arrange.
        var path = await WriteAsync("b14.dgr", Text("inline/b14-wide-dash-gap-and-nested-key"));

        // Act.
        var result = await _historyStacks.Get(ProjectRoot.From(_workspace)).ExecuteAsync(new RenameDependencyGraphElementCommand(path, "aaa", "Renamed"), TestContext.Current.CancellationToken);
        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        // Assert: one 'label', changed in place, in a body that still reads.
        Assert.True(result.IsSuccess, result.Error);
        Assert.StartsWith("dependencies: 1\nelements:\n  -   label: Renamed\n      id: aaa\n", text, StringComparison.Ordinal);
        Assert.False(DependencyGraphBody.Parse(text).IsUnreadable);
    }

    private async Task<string> WriteAsync(string name, string text)
    {
        var path = IoPath.Combine(_workspace, name);
        await File.WriteAllTextAsync(path, text, TestContext.Current.CancellationToken);
        return path;
    }

    private static string Text(string name) => DependencyGraphCorpus.Inline.Single(document => document.Name == name).Text;
}
