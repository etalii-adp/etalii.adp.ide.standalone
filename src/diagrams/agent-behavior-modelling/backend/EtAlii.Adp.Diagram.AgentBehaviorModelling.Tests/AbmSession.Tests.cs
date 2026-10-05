using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Xunit;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests;

/// <summary>A session follows both files: the Markdown through the store, the registration's positions through the history.</summary>
public sealed class AbmSessionTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "EtAlii.Adp.AbmSessionTests", Guid.NewGuid().ToString("N"));
    private readonly AbmDocumentStore _store = new();

    public AbmSessionTests()
    {
        Directory.CreateDirectory(_folder);
        File.Copy(AbmExamples.BodyOf("bug-fixer"), Body);
        File.WriteAllText(Registration, "etalii/agent-behavior-modelling\r\n");
    }

    private string Body => Path.Combine(_folder, "agent.md");

    private string Registration => Path.Combine(_folder, "agent.adp");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temp folder left behind is not a test failure.
        }
    }

    private AbmModel Model() => _store.GetOrLoad(Body).Model;

    private IReadOnlyDictionary<string, RegistrationPosition> Drawn() => AbmLayout.Arrange(Model(), RegistrationLayout.Read(Registration));

    [Fact]
    public async Task ADragDown_MovesTheWholeRowAndEverythingBeneathIt_PushedAsADelta_AndUndone()
    {
        // Arrange.
        var history = new HistoryStack(new AbmTestDispatcher(_store));
        await using var session = new AbmSession(Body, Registration, _store, new AbmElementMapper(), history);
        session.Baseline();
        var markdown = await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken);
        var computed = AbmLayout.Compute(Model());
        var pushed = new List<DiagramDelta>();
        session.Changed += (_, args) => pushed.AddRange(args.Deltas);

        // Act: 1.2 dropped 100 lower, and a little across but not past a neighbour.
        var refusal = await session.MoveElementToAsync("1.2", computed["1.2"].X + 20, computed["1.2"].Y + 100, TestContext.Current.CancellationToken);

        // Assert: every child of the root, with its subtree, is 100 lower and where it was across; the root stays.
        Assert.Equal("", refusal);
        var drawn = Drawn();
        Assert.Equal(computed["1"], drawn["1"]);
        foreach (var node in Model().Nodes.Where(node => node.Id != "1"))
        {
            Assert.Equal(new RegistrationPosition(computed[node.Id].X, computed[node.Id].Y + 100), drawn[node.Id]);
        }

        // Assert: the Markdown did not change by a byte, and the move was pushed.
        Assert.Equal(markdown, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));
        var moved = Assert.IsType<DiagramAddDelta>(Assert.Single(pushed));
        Assert.Equal(computed["1.4"].Y + 100 + (AbmLayout.NodeHeight / 2), Assert.Single(moved.Elements, element => element.Id == "1.4").Y);

        // Act: undo.
        await history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(RegistrationLayout.Read(Registration));
    }

    [Fact]
    public async Task ADragAcross_PastASibling_SwapsThemInTheMarkdown_TheirRowsFollow_AndOneUndoPutsBothFilesBack()
    {
        // Arrange: the first child's own children are dragged 40 lower first.
        var history = new HistoryStack(new AbmTestDispatcher(_store));
        await using var session = new AbmSession(Body, Registration, _store, new AbmElementMapper(), history);
        session.Baseline();
        var computed = AbmLayout.Compute(Model());
        Assert.Equal("", await session.MoveElementToAsync("1.1.1", computed["1.1.1"].X, computed["1.1.1"].Y + 40, TestContext.Current.CancellationToken));
        var markdown = await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken);
        var layout = await File.ReadAllBytesAsync(Registration, TestContext.Current.CancellationToken);

        // Act: "Understand the report" dropped just right of "Write a test"'s middle.
        var refusal = await session.MoveElementToAsync("1.1", computed["1.2"].X + 10, computed["1.1"].Y, TestContext.Current.CancellationToken);

        // Assert: the two swapped places in the Markdown, so in the tree.
        Assert.Equal("", refusal);
        var model = Model();
        Assert.Equal(("action", "fallback"), (model.NodeOf("1.1")!.Kind, model.NodeOf("1.2")!.Kind));
        var text = await File.ReadAllTextAsync(Body, TestContext.Current.CancellationToken);
        Assert.True(text.IndexOf("Write a test", StringComparison.Ordinal) < text.IndexOf("Understand the report", StringComparison.Ordinal));

        // Assert: the dragged node's children kept the height they were given, under their new ids.
        var drawn = Drawn();
        Assert.Equal(drawn["1.2"].Y + AbmLayout.NodeHeight + AbmLayout.VerticalGap + 40, drawn["1.2.1"].Y);
        Assert.Equal(drawn["1.2.1"].Y, drawn["1.2.2"].Y);
        Assert.True(drawn["1.1"].X < drawn["1.2"].X);

        // Act: one undo.
        await history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert: both files exactly as before the drop.
        Assert.Equal(markdown, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));
        Assert.Equal(layout, await File.ReadAllBytesAsync(Registration, TestContext.Current.CancellationToken));
        Assert.Equal("fallback", Model().NodeOf("1.1")!.Kind);

        // Act: redo.
        await history.RedoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("action", Model().NodeOf("1.1")!.Kind);
    }

    [Fact]
    public async Task ADropWhereItWas_WritesNothing()
    {
        // Arrange.
        var history = new HistoryStack(new AbmTestDispatcher(_store));
        await using var session = new AbmSession(Body, Registration, _store, new AbmElementMapper(), history);
        var computed = AbmLayout.Compute(Model());

        // Act: a little across, not past a neighbour, and at its own height.
        var refusal = await session.MoveElementToAsync("1.3", computed["1.3"].X - 30, computed["1.3"].Y, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("", refusal);
        Assert.False(history.CanUndo);
        Assert.Empty(RegistrationLayout.Read(Registration));
    }

    [Fact]
    public async Task ARowDraggedAboveItsParent_StopsJustBelowIt()
    {
        // Arrange.
        var history = new HistoryStack(new AbmTestDispatcher(_store));
        await using var session = new AbmSession(Body, Registration, _store, new AbmElementMapper(), history);
        var computed = AbmLayout.Compute(Model());

        // Act.
        Assert.Equal("", await session.MoveElementToAsync("1.2", computed["1.2"].X, computed["1"].Y - 500, TestContext.Current.CancellationToken));

        // Assert.
        Assert.Equal(computed["1"].Y + AbmLayout.NodeHeight + AbmLayout.MinimumGap, Drawn()["1.2"].Y);
    }

    [Fact]
    public async Task AnEdit_IsPushedToTheSession()
    {
        // Arrange.
        var history = new HistoryStack(new AbmTestDispatcher(_store));
        await using var session = new AbmSession(Body, Registration, _store, new AbmElementMapper(), history);
        session.Baseline();
        var pushed = new List<DiagramDelta>();
        session.Changed += (_, args) => pushed.AddRange(args.Deltas);

        // Act.
        var result = await history.ExecuteAsync(new RenameAbmNodeCommand(Body, "1.1", "Read the report"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains(pushed.OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements), element => element.Id == "1.1");
    }

    [Fact]
    public async Task ADragWithoutARegistration_IsRefused()
    {
        // Arrange.
        var history = new HistoryStack(new AbmTestDispatcher(_store));
        await using var session = new AbmSession(Body, null, _store, new AbmElementMapper(), history);

        // Act.
        var refusal = await session.MoveElementToAsync("1", 0, 0, TestContext.Current.CancellationToken);

        // Assert.
        Assert.NotEqual("", refusal);
    }
}
