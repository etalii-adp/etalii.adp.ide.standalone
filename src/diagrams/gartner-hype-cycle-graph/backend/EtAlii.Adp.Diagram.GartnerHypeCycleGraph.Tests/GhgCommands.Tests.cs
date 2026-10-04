using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using Xunit;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests;

/// <summary>
/// Task 18: every command edits, every undo gives back the original bytes, and every refusal writes
/// nothing - on the technology-trends example, through the real store on a real file.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bytes, not models.</b> An undo is only right if the file is exactly what it was: a model that
/// compares equal can still hide a reordered key or a lost comment.
/// </para>
/// <para>
/// <b>A refusal is checked twice: on disk AND in the store's cache</b>, where an edit made in place
/// would survive a refusal and be written by the next save.
/// </para>
/// </remarks>
public sealed class GhgCommandsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "EtAlii.Adp.GhgCommandsTests", Guid.NewGuid().ToString("N"));
    private readonly GhgDocumentStore _store = new();
    private readonly GhgTestDispatcher _dispatcher;
    private readonly byte[] _original;

    public GhgCommandsTests()
    {
        Directory.CreateDirectory(_folder);
        File.Copy(GhgModuleFiles.Example, Body);
        _original = File.ReadAllBytes(Body);
        _dispatcher = new GhgTestDispatcher(_store);
    }

    private string Body => Path.Combine(_folder, "technology-trends.ghg");

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

    private static int M(int year, int month = 1) => GhgScale.MonthIndex(year, month);

    public static TheoryData<string> EveryEdit =>
    [
        "add a trend", "remove a trend", "move", "resize from the left", "resize from the right",
        "drag a boundary", "even the phases", "lower the phases", "rename", "set tags",
        "describe a trend", "describe an influence", "influence", "move an attachment", "remove an influence",
        "arrange",
    ];

    private ICommand EditNamed(string name) => name switch
    {
        "add a trend" => new AddGhgTrendCommand(Body, GhgScale.XOf(M(2030)) + 1, GhgScale.TopOf(90) + 10),
        "remove a trend" => new RemoveGhgElementCommand(Body, "steamboats"),
        "move" => new SetGhgPlacementCommand(Body, "railways", GhgScale.XOf(M(1830)), GhgScale.TopOf(40)),
        "resize from the left" => new SetGhgSpanCommand(Body, "steam-engine", "1780-01", null),
        "resize from the right" => new SetGhgSpanCommand(Body, "steam-engine", null, "1950-01"),
        "drag a boundary" => new SetGhgBoundaryCommand(Body, "railways", 0, "1840-01"),
        "even the phases" => new ClearGhgBoundariesCommand(Body, "steam-engine"),
        "lower the phases" => new SetGhgPhasesCommand(Body, "railways", 2),
        "rename" => new RenameGhgElementCommand(Body, "railways", "Railroads"),
        "set tags" => new SetGhgTagsCommand(Body, "railways", "transport, industry, society"),
        "describe a trend" => new SetGhgDescriptionCommand(Body, "iron-smelting", "Darby's coke furnace at Coalbrookdale."),
        "describe an influence" => new SetGhgDescriptionCommand(Body, "steam-engine--railways", ""),
        // Railways never influences the steam engine, so the reverse of an existing influence is allowed.
        "influence" => new AddGhgInfluenceCommand(Body, "railways", null, "steam-engine", null),
        "move an attachment" => new SetGhgAttachmentCommand(Body, "steam-engine--railways", "to", new GhgEnd("trough", "bottom", 0.4)),
        "remove an influence" => new RemoveGhgInfluenceCommand(Body, "steam-engine--railways"),
        "arrange" => new ArrangeGhgCommand(Body),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No such edit."),
    };

    /// <summary>The task's guard: every edit, then its undo, gives the original bytes.</summary>
    [Theory]
    [MemberData(nameof(EveryEdit))]
    public async Task EveryEdit_ThenItsUndo_GivesTheOriginalBytes(string edit)
    {
        var result = await _dispatcher.DispatchAsync(EditNamed(edit), TestContext.Current.CancellationToken);

        // It edited - otherwise the undo proves nothing.
        Assert.True(result.IsSuccess, result.Error);
        Assert.NotEqual(_original, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));

        var undone = await _dispatcher.DispatchAsync(Assert.IsAssignableFrom<ICommand>(result.Inverse), TestContext.Current.CancellationToken);

        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Equal(_original, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));
    }

    public static TheoryData<string, string> EveryRefusal => new()
    {
        { "influence a trend that already influences it", "already influences that one" },
        { "influence itself", "cannot influence itself" },
        { "influence a trend that is not there", "from one trend to another" },
        { "stop before the start", "at least" },
        { "drag a hidden boundary", "between two visible phases" },
        { "drag to a date that is not one", "is not a date" },
        { "remove a trend that is not there", "no longer in this graph" },
        { "remove an influence that is not there", "no longer in this graph" },
    };

    private ICommand RefusalNamed(string name) => name switch
    {
        "influence a trend that already influences it" => new AddGhgInfluenceCommand(Body, "steam-engine", null, "railways", null),
        "influence itself" => new AddGhgInfluenceCommand(Body, "railways", null, "railways", null),
        "influence a trend that is not there" => new AddGhgInfluenceCommand(Body, "railways", null, "nothing-here", null),
        "stop before the start" => new SetGhgSpanCommand(Body, "railways", null, "1820-01"),
        "drag a hidden boundary" => new SetGhgBoundaryCommand(Body, "railways", 3, "1840-01"),
        "drag to a date that is not one" => new SetGhgBoundaryCommand(Body, "railways", 0, "soon"),
        "remove a trend that is not there" => new RemoveGhgElementCommand(Body, "nothing-here"),
        "remove an influence that is not there" => new RemoveGhgInfluenceCommand(Body, "nothing-here"),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No such refusal."),
    };

    /// <summary>
    /// The task's guard: every refusal says why and leaves the bytes unchanged - on disk, and in the
    /// store's cache.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryRefusal))]
    public async Task EveryRefusal_WritesNothing_AndSaysWhy(string refusal, string saying)
    {
        // The cache is loaded, so an in-place edit would have somewhere to hide.
        var cachedBefore = _store.GetOrLoad(Body).Document.Text;

        var result = await _dispatcher.DispatchAsync(RefusalNamed(refusal), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains(saying, result.Error, StringComparison.Ordinal);
        Assert.Equal(_original, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));
        Assert.Equal(cachedBefore, _store.GetOrLoad(Body).Document.Text);
    }

    /// <summary>The task's guard: deleting a trend with two influences restores all three on one undo.</summary>
    [Fact]
    public async Task DeletingATrendWithTwoInfluences_RestoresAllThreeOnUndo()
    {
        // Steamboats: the steam engine made them, and they led to ocean steamships.
        Assert.Equal(2, Touching(Parse(), "steamboats"));

        var removed = await _dispatcher.DispatchAsync(new RemoveGhgElementCommand(Body, "steamboats"), TestContext.Current.CancellationToken);

        Assert.True(removed.IsSuccess, removed.Error);
        var after = Parse();
        Assert.DoesNotContain(after.Trends, trend => trend.Id == "steamboats");
        Assert.Equal(0, Touching(after, "steamboats"));

        var undone = await _dispatcher.DispatchAsync(removed.Inverse!, TestContext.Current.CancellationToken);

        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Equal(_original, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));
        Assert.Equal(2, Touching(Parse(), "steamboats"));
    }

    /// <summary>
    /// The task's named sabotage: a connect that trusts the client. A scripted second A -&gt; B is refused
    /// with a sentence by the backend's own check, while B -&gt; A - one each way, the user's ruling Q3 -
    /// is accepted.
    /// </summary>
    [Fact]
    public async Task AScriptedDuplicate_IsRefused_WhileTheOppositeDirectionIsAccepted()
    {
        Assert.True(GhgRuleSet.AlreadyInfluences(Parse(), "steam-engine", "railways"));

        var duplicate = await _dispatcher.DispatchAsync(
            new AddGhgInfluenceCommand(Body, "steam-engine", new GhgEnd("plateau", "bottom", 0.9), "railways", new GhgEnd("slope", "top", 0.2)),
            TestContext.Current.CancellationToken);

        Assert.False(duplicate.IsSuccess);
        Assert.Equal("This trend already influences that one; a trend influences another once in each direction.", duplicate.Error);
        Assert.Equal(_original, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));

        var opposite = await _dispatcher.DispatchAsync(
            new AddGhgInfluenceCommand(Body, "railways", null, "steam-engine", null),
            TestContext.Current.CancellationToken);

        Assert.True(opposite.IsSuccess, opposite.Error);
        Assert.Contains(Parse().Influences, influence => influence.From == "railways" && influence.To == "steam-engine");
        Assert.Empty(GhgValidator.Validate(Parse()));
    }

    /// <summary>Requirement 4.6: a boundary dragged past its neighbour stops a month short of it.</summary>
    [Fact]
    public async Task ABoundaryDraggedPastItsNeighbour_IsClamped()
    {
        // The steam engine's Trough ends at 1800-01; drag the Peak's end to 1850.
        var result = await _dispatcher.DispatchAsync(new SetGhgBoundaryCommand(Body, "steam-engine", 0, "1850-01"), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error);
        var steam = Parse().Trends.Single(trend => trend.Id == "steam-engine");
        Assert.Equal([M(1799, 12), M(1800), null], steam.DraggedEnds);
    }

    /// <summary>A drag reaches the document through the session and is one undo away, like every other edit.</summary>
    [Fact]
    public async Task AMoveThroughTheSession_IsWritten_AndOneUndoAway()
    {
        using var historyStacks = new HistoryStackStore(_dispatcher);
        var history = historyStacks.Get(_folder);
        await using var session = new GhgSession(Body, _store, new GhgElementMapper(), history);

        var error = await session.MoveElementToAsync("railways", GhgScale.XOf(M(1830)), GhgScale.TopOf(40), TestContext.Current.CancellationToken);

        Assert.Equal("", error);
        var railways = Parse().Trends.Single(trend => trend.Id == "railways");
        Assert.Equal((M(1830), 40), (railways.Start!.Value, railways.Row));
        Assert.Equal(M(1830) + (M(1930) - M(1825, 9)), railways.Stop);

        var undone = await history.UndoAsync(TestContext.Current.CancellationToken);

        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Equal(_original, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A document that could not be read is never written: its emptiness is not the document, and
    /// writing it would replace the only copy on disk.
    /// </summary>
    [Fact]
    public async Task ADocumentThatCouldNotBeRead_IsNeverWritten()
    {
        // Another program holds the file, so the first open cannot read it.
        var store = new GhgDocumentStore();
        GhgDocumentEntry entry;
        using (new FileStream(Body, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            entry = store.GetOrLoad(Body);
        }

        Assert.False(entry.IsUsable);
        Assert.Empty(entry.Model.Trends);

        var edited = await new GhgTestDispatcher(store).DispatchAsync(
            new RenameGhgElementCommand(Body, "railways", "Railroads"),
            TestContext.Current.CancellationToken);
        var saved = store.Save(Body, LineDocument.Parse(GhgDocumentFactory.EmptyDocument("\n")));

        Assert.False(edited.IsSuccess);
        Assert.True(saved.Failed);
        Assert.Equal(_original, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));
    }

    private static int Touching(GhgModel model, string id) =>
        model.Influences.Count(influence => influence.From == id || influence.To == id);

    private GhgModel Parse() => GhgParser.Parse(LineDocument.Parse(File.ReadAllText(Body)));
}
