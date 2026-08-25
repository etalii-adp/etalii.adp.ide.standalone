using EtAlii.Adp.Backend;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

/// <summary>
/// Every command through the real history: apply it, undo it, assert the map is exactly what
/// it was - ids, order and all - then redo it (Requirement 6.2). The history, dispatcher and
/// store are the real ones, wired by the same extension the host calls.
/// </summary>
public class CommandsTests : IDisposable
{
    private readonly string _root;
    private readonly string _bodyPath;
    private readonly ServiceProvider _services;
    private readonly IHistoryStack _history;
    private readonly IMindmapDocumentStore _documents;

    public CommandsTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _bodyPath = IoPath.Combine(_root, "architecture.mm");
        File.Copy("Fixtures/architecture.mm", _bodyPath);

        _services = new ServiceCollection().AddCommands().AddMindmapCommands().BuildServiceProvider();
        _history = _services.GetRequiredService<IHistoryStackStore>().Get(_root);
        _documents = _services.GetRequiredService<IMindmapDocumentStore>();
    }

    public void Dispose()
    {
        _services.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private MindmapDocument Document => _documents.GetOrLoad(_bodyPath);

    private string OnDisk => File.ReadAllText(_bodyPath);

    private async Task<CommandResult> Run(ICommand command) =>
        await _history.ExecuteAsync(command, TestContext.Current.CancellationToken);

    private async Task Undo()
    {
        var result = await _history.UndoAsync(TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Error);
    }

    private async Task Redo()
    {
        var result = await _history.RedoAsync(TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Error);
    }

    // ---- add child ---------------------------------------------------------------------------

    [Fact]
    public async Task AddChild_AppendsLast_AndUndoRemovesExactlyThatNode()
    {
        // Arrange.
        var before = Document.ToText();

        var result = await Run(new AddChildNodeCommand(_bodyPath, "ID_411002937", "ID_new", "Logging"));

        // Act and assert, step by step.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Logging", Document.Find("ID_411002937")!.Children[^1].Text);
        Assert.Contains("Logging", OnDisk, StringComparison.Ordinal);

        await Undo();
        Assert.Equal(before, Document.ToText());
        Assert.Equal(before, OnDisk);

        await Redo();
        Assert.Equal("ID_new", Document.Find("ID_411002937")!.Children[^1].Id);
    }

    // ---- add sibling ---------------------------------------------------------------------------

    [Fact]
    public async Task AddSibling_InsertsAfter_AndRoundTripsThroughUndo()
    {
        // Arrange.
        var before = Document.ToText();

        await Run(new AddSiblingNodeCommand(_bodyPath, "ID_88117420", "ID_new", "Projects"));

        // Act and assert, step by step.
        Assert.Equal(["Context service", "Projects", "Commands & history", "Hierarchy"], Document.Find("ID_411002937")!.Children.Select(child => child.Text));

        await Undo();
        Assert.Equal(before, Document.ToText());
    }

    [Fact]
    public async Task AddSibling_OfTheRoot_IsRejectedWithoutAnException()
    {
        // Act.
        var result = await Run(new AddSiblingNodeCommand(_bodyPath, Document.Root.Id, "ID_new", "x"));

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.False(_history.CanUndo);
    }

    // ---- move ----------------------------------------------------------------------------------

    [Fact]
    public async Task Move_RestoresBothParentAndIndexOnUndo()
    {
        // Arrange.
        // Hierarchy is the third child of Backend. Move it to be the first child of Client.
        var before = Document.ToText();

        await Run(new MoveNodeCommand(_bodyPath, "ID_88117425", "ID_411002938", 0));

        // Act and assert, step by step.
        Assert.Equal("ID_88117425", Document.Find("ID_411002938")!.Children[0].Id);
        Assert.Equal(2, Document.Find("ID_88117425")!.Children.Count); // the subtree came along

        await Undo();
        Assert.Equal(before, Document.ToText());
        Assert.Equal(2, Document.Find("ID_88117425")!.IndexInParent); // back at its old index, not just its old parent
    }

    [Fact]
    public async Task Move_IntoItsOwnSubtree_IsRejected()
    {
        // Act.
        var result = await Run(new MoveNodeCommand(_bodyPath, "ID_411002937", "ID_88117422", 0));

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("own branch", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Move_TheRoot_IsRejected()
    {
        // Act.
        var result = await Run(new MoveNodeCommand(_bodyPath, Document.Root.Id, "ID_411002937", 0));

        // Assert.
        Assert.False(result.IsSuccess);
    }

    // ---- remove and restore ----------------------------------------------------------------

    [Fact]
    public async Task Remove_UndoPutsTheWholeSubtreeBackByteForByte()
    {
        // Arrange.
        // The deepest test of all (Requirement 7.4): ids, text, notes, links and order survive.
        var before = Document.ToText();

        // Arrange, continued.
        await Run(new RemoveNodeCommand(_bodyPath, "ID_411002937")); // Backend, with 8 descendants, notes and links

        // Arrange, continued.
        Assert.Null(Document.Find("ID_411002937"));
        Assert.Null(Document.Find("ID_88117420"));

        // Act.
        await Undo();

        // Assert.
        Assert.Equal(before, Document.ToText());
        Assert.Equal(before, OnDisk);
        Assert.Equal("../../../../backend/EtAlii.Adp.Backend/Context/ContextServiceImpl.cs", Document.Find("ID_88117420")!.Link);
    }

    [Fact]
    public async Task Remove_RedoRemovesItAgain()
    {
        // Arrange.
        await Run(new RemoveNodeCommand(_bodyPath, "ID_88117425"));
        await Undo();

        // Act.
        await Redo();

        // Assert.
        Assert.Null(Document.Find("ID_88117425"));
    }

    [Fact]
    public async Task Remove_TheRoot_IsRejected()
    {
        // Act.
        var result = await Run(new RemoveNodeCommand(_bodyPath, Document.Root.Id));

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.NotNull(Document.Root);
    }

    // ---- text, notes, link -----------------------------------------------------------------

    [Fact]
    public async Task SetText_UndoRestoresThePreviousText()
    {
        // Arrange.
        await Run(new SetNodeTextCommand(_bodyPath, "ID_88117422", "renamed"));
        Assert.Equal("renamed", Document.Find("ID_88117422")!.Text);

        // Act.
        await Undo();

        // Assert.
        Assert.Equal("ICommand", Document.Find("ID_88117422")!.Text);
    }

    [Fact]
    public async Task SetText_ToEmpty_IsPersisted()
    {
        // Act.
        var result = await Run(new SetNodeTextCommand(_bodyPath, "ID_88117422", ""));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("", MindmapDocument.Parse(OnDisk).Find("ID_88117422")!.Text);
    }

    [Fact]
    public async Task SetNotes_UndoRemovesANoteThatWasNotThere()
    {
        // Arrange.
        var before = Document.ToText();

        // Arrange, continued.
        await Run(new SetNodeNotesCommand(_bodyPath, "ID_88117422", "a note"));
        Assert.Equal("a note", Document.Find("ID_88117422")!.Notes);

        // Act.
        await Undo();

        // Assert.
        Assert.Equal(before, Document.ToText());
    }

    [Fact]
    public async Task SetLink_UndoRestoresNoLink()
    {
        // Arrange.
        await Run(new SetNodeLinkCommand(_bodyPath, "ID_88117422", "../x.cs"));
        Assert.Equal("../x.cs", Document.Find("ID_88117422")!.Link);

        // Act.
        await Undo();

        // Assert.
        Assert.Null(Document.Find("ID_88117422")!.Link);
    }

    [Fact]
    public async Task SetLink_ToNull_UnlinksAndUndoRelinks()
    {
        // Arrange.
        await Run(new SetNodeLinkCommand(_bodyPath, "ID_88117420", null));
        Assert.Null(Document.Find("ID_88117420")!.Link);

        // Act.
        await Undo();

        // Assert.
        Assert.Equal("../../../../backend/EtAlii.Adp.Backend/Context/ContextServiceImpl.cs", Document.Find("ID_88117420")!.Link);
    }

    // ---- the rules every handler shares ------------------------------------------------

    [Fact]
    public async Task AnyCommand_OnANodeThatIsGone_IsRejectedWithAMessage()
    {
        // Act.
        var result = await Run(new SetNodeTextCommand(_bodyPath, "ID_does_not_exist", "x"));

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal("The node no longer exists.", result.Error);
        Assert.False(_history.CanUndo);
    }

    [Fact]
    public async Task AnyCommand_OnAMapThatIsNotAMap_IsRejectedNamingTheFile()
    {
        // Arrange.
        var broken = IoPath.Combine(_root, "broken.mm");
        File.WriteAllText(broken, "<html/>");

        // Act.
        var result = await Run(new SetNodeTextCommand(broken, "x", "y"));

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("broken.mm", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnyCommand_OnAMapWithNoBodyYet_OpensAnEmptyMapAndWritesItOnSave()
    {
        // Arrange.
        // Requirement 2.5: a registration with no body is recoverable, not broken.
        var fresh = IoPath.Combine(_root, "fresh.mm");
        var root = _documents.GetOrLoad(fresh).Root;
        Assert.False(File.Exists(fresh), "opening must not write");

        // Act.
        var result = await Run(new AddChildNodeCommand(fresh, root.Id, "ID_c", "first"));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.True(File.Exists(fresh));
        Assert.Equal("fresh", MindmapDocument.Parse(File.ReadAllText(fresh)).Root.Text);
    }

    [Fact]
    public void Save_NeverLeavesAScratchFileBehind_AndPersistsAssignedIds()
    {
        // Arrange and act.
        _ = Document;
        _documents.Save(_bodyPath, MindmapStructureChanged.Nothing);

        // Assert.
        Assert.Empty(Directory.GetFiles(_root, "~adp-*"));
        // The first save is the one allowed difference from the corpus: the two id-less nodes
        // now carry ids (Requirement 3.4), and nothing else changed.
        var saved = MindmapDocument.Parse(OnDisk, assignMissingIds: false);
        Assert.All(saved.Nodes, node => Assert.NotEqual("", node.Id));
        Assert.Equal(Document.ToText(), OnDisk);
    }
}
