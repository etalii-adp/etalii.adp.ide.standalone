using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests.Parity;

/// <summary>
/// Guards for the module's own defects B11 to B14 (dependency-graph-disl-fbl, task 2). Each was first written as the
/// right behaviour and seen to fail against today's code for the defect's reason (the failures are in the task's
/// implementation log); each pinned today's answer, with the right one written beside it, until the task that
/// repairs it turns it: task 8 for B11, B13 and B14. Task 4 turned B12: every command now undoes through the host's
/// restore command, which writes back the text as it was before the edit.
/// </summary>
public sealed class DependencyGraphDefectGuardsTests : IDisposable
{
    private readonly string _workspace = IoPath.Combine(IoPath.GetTempPath(), "adp-dgr-guards-" + Guid.NewGuid().ToString("N"));
    private readonly DependencyGraphDocumentStore _store = new();
    private readonly HistoryStackStore _historyStacks;

    public DependencyGraphDefectGuardsTests()
    {
        Directory.CreateDirectory(_workspace);
        _historyStacks = new HistoryStackStore(new DependencyGraphTestDispatcher(_store));
    }

    public void Dispose() => TestFolder.TryDelete(_workspace);

    [Fact]
    public void B11_RemovingTheSecondHolderOfAnId_TakesTheFirstHoldersDependencies_UntilTask8()
    {
        // Arrange.
        var document = LineDocument.Parse(Text("inline/b11-holders-and-an-anonymous-node"));
        var model = DependencyGraphParser.Parse(document);

        // Act: the third node is the second holder of 'aaa'.
        DependencyGraphWriter.RemoveElement(document, model, model.Elements[2]);

        // Assert, today's answer: every dependency whose end is written 'aaa' goes. The right answer keeps both,
        // because 'aaa' resolves to the first holder.
        Assert.DoesNotContain("of-the-first", document.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("of-the-second", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task B12_UndoingARelabel_RestoresTheCommentItTook()
    {
        // Arrange.
        var path = IoPath.Combine(_workspace, "commented.dgr");
        const string before = "dependencies: 1\nelements:\n  - id: aaa\n    label: A\n    x: 0\n    row: 0\n  - id: bbb\n    label: B\n    x: 240\n    row: 1\nrelations:\n  - id: uses\n    from: aaa\n    to: bbb\n    label: uses # why it does\n";
        await File.WriteAllTextAsync(path, before, TestContext.Current.CancellationToken);
        var history = _historyStacks.Get(_workspace);

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
        var history = _historyStacks.Get(_workspace);
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
    public void B13_TheFirstNodeOfAGraphWithNoElementsKey_Throws_UntilTask8()
    {
        // Arrange.
        var document = LineDocument.Parse(Text("inline/no-elements-key"));
        var model = DependencyGraphParser.Parse(document);

        // Act and assert, today's answer: the insertion point is -1 and the writer inserts there. The right answer is an
        // 'elements' key with the node under it.
        Assert.Throws<ArgumentOutOfRangeException>(() => DependencyGraphWriter.InsertElement(document, model, "newid", "New", 1, 2));
    }

    [Fact]
    public void B14_AKeyOnTheDashLineWithAWideGap_IsWrittenAgain_UntilTask8()
    {
        // Arrange.
        var document = LineDocument.Parse(Text("inline/b14-wide-dash-gap-and-nested-key"));
        var model = DependencyGraphParser.Parse(document);

        // Act.
        DependencyGraphWriter.SetLabel(document, model.Elements[0], "Renamed");

        // Assert, today's answer: the key is not found after the wide gap, a second 'label' is written, and the body no
        // longer reads. The right answer is one 'label', changed in place, in a body that still reads.
        Assert.StartsWith("dependencies: 1\nelements:\n  -   label: A\n      label: Renamed\n      id: aaa\n", document.Text, StringComparison.Ordinal);
        Assert.False(DependencyGraphParser.Outcome(LineDocument.Parse(document.Text)).IsUsable);
    }

    private static string Text(string name) => DependencyGraphCorpus.Inline.Single(document => document.Name == name).Text;
}
