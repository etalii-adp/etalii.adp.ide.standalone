using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests;

/// <summary>
/// ghg-triggers-and-notes task 6: every edit of a trigger or note is a command whose undo gives back
/// the original bytes, a refusal writes nothing, and the toolbox, menus, grid and wire answer for both
/// types - on the triggers-and-notes fixture, through the real store on a real file.
/// </summary>
public sealed class GhgTriggersAndNotesEditingTests : IDisposable
{
    private readonly string _folder = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.GhgTriggersAndNotesEditingTests", Guid.NewGuid().ToString("N"));
    private readonly GhgDocumentStore _store = new();
    private readonly GhgTestDispatcher _dispatcher;
    private readonly HistoryStackStore _historyStacks;
    private readonly byte[] _original;

    public GhgTriggersAndNotesEditingTests()
    {
        Directory.CreateDirectory(_folder);
        File.Copy(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "triggers-and-notes.ghg"), Body);
        _original = File.ReadAllBytes(Body);
        _dispatcher = new GhgTestDispatcher(_store);
        _historyStacks = new HistoryStackStore(_dispatcher);
    }

    private string Body => IoPath.Combine(_folder, "triggers-and-notes.ghg");

    public void Dispose()
    {
        _historyStacks.Dispose();
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

    /// <summary>The fixture is drawn in years: x of a year's start.</summary>
    private static double X(int year) => GhgScale.XOf(M(year), GhgTimeUnit.Year);

    private GhgModel Parse() => GhgParser.Parse(GhgBody.Parse(File.ReadAllText(Body)));

    private ContextTarget Target(string elementId) =>
        new(ContextScope.DiagramElement, Body, IsContainer: false, SourceId: default, _folder, ShortGuid.NewShortGuid(), elementId);

    public static TheoryData<string> EveryEdit =>
    [
        "add a trigger", "add a note", "move a trigger", "move a note", "rename a trigger", "edit a note's text",
        "resize a note", "resize a note from the left", "resize a note from the top", "date a trigger", "tag a trigger", "describe a trigger",
        "remove a trigger", "remove a note", "influence from a trigger",
    ];

    private ICommand EditNamed(string name) => name switch
    {
        "add a trigger" => new AddGhgTriggerCommand(Body, X(1970) + 1, GhgScale.TopOf(6) + 10),
        "add a note" => new AddGhgNoteCommand(Body, X(1970) + 1, GhgScale.TopOf(6) + 10),
        "move a trigger" => new SetGhgPlacementCommand(Body, "transistor-invented", X(1949) - (GhgScale.TriggerSize / 2), GhgScale.TopOf(2) + 8),
        "move a note" => new SetGhgPlacementCommand(Body, "note-2", X(1965), GhgScale.TopOf(6)),
        "rename a trigger" => new RenameGhgElementCommand(Body, "transistor-invented", "Point-contact transistor"),
        "edit a note's text" => new RenameGhgElementCommand(Body, "note-2", "Now\non two lines"),
        "resize a note" => new SetGhgNoteSizeCommand(Body, "note-2", "200 x 48"),
        "resize a note from the left" => new SetGhgNoteSizeCommand(Body, "note-2", "160 x 40 at 1950-01"),
        "resize a note from the top" => new SetGhgNoteSizeCommand(Body, "note-2", "120 x 96 at 1960-01 row 4"),
        "date a trigger" => new SetGhgSpanCommand(Body, "transistor-invented", "1947-11", null),
        "tag a trigger" => new SetGhgTagsCommand(Body, "transistor-invented", "electronics, physics"),
        "describe a trigger" => new SetGhgDescriptionCommand(Body, "transistor-invented", ""),
        "remove a trigger" => new RemoveGhgElementCommand(Body, "transistor-invented"),
        "remove a note" => new RemoveGhgElementCommand(Body, "note-1"),
        "influence from a trigger" => new AddGhgInfluenceCommand(Body, "transistor-invented", null, "radio", new GhgEnd("peak", "top", 0.3)),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No such edit."),
    };

    [Theory]
    [MemberData(nameof(EveryEdit))]
    public async Task EveryEdit_ThenItsUndo_GivesTheOriginalBytes(string edit)
    {
        var result = await _dispatcher.DispatchAsync(EditNamed(edit), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotEqual(_original, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));
        Assert.Empty(GhgValidator.Validate(GhgBody.Parse(await File.ReadAllTextAsync(Body, TestContext.Current.CancellationToken))));

        var undone = await _dispatcher.DispatchAsync(Assert.IsAssignableFrom<ICommand>(result.Inverse), TestContext.Current.CancellationToken);

        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Equal(_original, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));
    }

    public static TheoryData<string, string> EveryRefusal => new()
    {
        { "influence into a trigger", "An influence cannot end at a trigger." },
        { "influence a trigger into a trigger", "An influence cannot end at a trigger." },
        { "a second influence from a trigger", "already influences that one" },
        { "a trigger date that is not one", "is not a date" },
        { "a size that is not one", "is not a size" },
        { "rename a trigger to nothing", "needs a name" },
        { "remove a note that is not there", "no longer in this graph" },
    };

    private ICommand RefusalNamed(string name) => name switch
    {
        "influence into a trigger" => new AddGhgInfluenceCommand(Body, "transistors", null, "transistor-invented", null),
        "influence a trigger into a trigger" => new AddGhgInfluenceCommand(Body, "transistor-invented", null, "transistor-invented", null),
        "a second influence from a trigger" => new AddGhgInfluenceCommand(Body, "transistor-invented", null, "transistors", new GhgEnd("slope", "top", 0.5)),
        "a trigger date that is not one" => new SetGhgSpanCommand(Body, "transistor-invented", "soon", null),
        "a size that is not one" => new SetGhgNoteSizeCommand(Body, "note-1", "wide"),
        "rename a trigger to nothing" => new RenameGhgElementCommand(Body, "transistor-invented", " "),
        "remove a note that is not there" => new RemoveGhgElementCommand(Body, "nothing-here"),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No such refusal."),
    };

    [Theory]
    [MemberData(nameof(EveryRefusal))]
    public async Task EveryRefusal_WritesNothing_AndSaysWhy(string refusal, string saying)
    {
        var cachedBefore = _store.GetOrLoad(Body).Document.Text;

        var result = await _dispatcher.DispatchAsync(RefusalNamed(refusal), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains(saying, result.Error, StringComparison.Ordinal);
        Assert.Equal(_original, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));
        Assert.Equal(cachedBefore, _store.GetOrLoad(Body).Document.Text);
    }

    /// <summary>Requirement 3.5: a trigger takes its influences with it, and one undo restores them.</summary>
    [Fact]
    public async Task RemovingATrigger_TakesItsInfluences_AndOneUndoRestoresThem()
    {
        var removed = await _dispatcher.DispatchAsync(new RemoveGhgElementCommand(Body, "transistor-invented"), TestContext.Current.CancellationToken);

        Assert.True(removed.IsSuccess, removed.Error);
        Assert.DoesNotContain(Parse().Influences, influence => influence.From == "transistor-invented");

        var undone = await _dispatcher.DispatchAsync(removed.Inverse!, TestContext.Current.CancellationToken);

        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Contains(Parse().Influences, influence => influence.From == "transistor-invented");
    }

    /// <summary>Requirement 2.4: a trigger's CENTRE lands on a step line and a row's middle, from the top-left the canvas sends.</summary>
    [Fact]
    public async Task AMovedTrigger_LandsWithItsCentreOnAStepLine_AndARowsMiddle()
    {
        // A little right of 1949's line and a little below row 2's middle.
        var result = await _dispatcher.DispatchAsync(
            new SetGhgPlacementCommand(Body, "transistor-invented", X(1949) + 1 - (GhgScale.TriggerSize / 2), GhgScale.TopOf(2) + 8 + 3),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error);
        var trigger = Parse().Triggers.Single();
        Assert.Equal((M(1949), 2), (trigger.Date!.Value, trigger.Row));
    }

    [Fact]
    public async Task ADroppedNote_IsEmptyAndDefaultSized_WithTheDropInsideIt()
    {
        var x = X(1970) + 1;
        var y = GhgScale.TopOf(6) + 10;

        var result = await _dispatcher.DispatchAsync(new AddGhgNoteCommand(Body, x, y), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error);
        var note = Assert.Single(Parse().Notes, candidate => candidate.Text.Length == 0);
        Assert.Equal((M(1970), 6, AddGhgNoteCommandHandler.DefaultWidth, AddGhgNoteCommandHandler.DefaultHeight), (note.At!.Value, note.Row, note.Width!.Value, note.Height!.Value));
    }

    /// <summary>Q2: the date is written in the diagram's unit, shortly after the name and in full for the tooltip.</summary>
    [Theory]
    [InlineData("month", "Dec 1947", "December 1947")]
    [InlineData("year", "1947", "1947")]
    [InlineData("century", "1947", "1947")]
    public void ATriggersDate_IsWrittenInTheDiagramsUnit(string unit, string when, string whenLong)
    {
        var text = File.ReadAllText(Body).Replace("unit: year", $"unit: {unit}", StringComparison.Ordinal);
        var model = GhgParser.Parse(GhgBody.Parse(text));

        var element = Assert.Single(new GhgElementMapper().Visible(model, DiagramViewport.Unbounded), candidate => candidate.Type == GhgElementMapper.TriggerType);
        var payload = GhgTriggerPayload.Parser.ParseFrom(element.Payload.ToArray());

        Assert.Equal((when, whenLong), (payload.When, payload.WhenLong));
        Assert.Equal(("Transistor invented", -GhgScale.TriggerSize / 2, (GhgScale.TrendHeight - GhgScale.TriggerSize) / 2), (payload.Name, payload.SnapX, payload.SnapY));
    }

    [Fact]
    public void TheWire_CarriesTriggersAtTheirCentre_NotesAtTheirCentre_AndAnInfluenceFromATrigger()
    {
        var elements = new GhgElementMapper().Visible(Parse(), DiagramViewport.Unbounded);

        var trigger = Assert.Single(elements, candidate => candidate.Type == GhgElementMapper.TriggerType);
        Assert.Equal((X(1947) + (GhgScale.WidthOf(11, GhgTimeUnit.Year)), GhgScale.TopOf(1) + (GhgScale.TrendHeight / 2)), (trigger.X, trigger.Y));

        var note = Assert.Single(elements, candidate => candidate.Id == "note-2");
        Assert.Equal((X(1960) + 60, GhgScale.TopOf(5) + 20), (note.X, note.Y));
        var notePayload = GhgNotePayload.Parser.ParseFrom(note.Payload.ToArray());
        Assert.Equal(("A one-line remark", 120d, 40d), (notePayload.Text, notePayload.Width, notePayload.Height));

        var influence = Assert.Single(elements, candidate => candidate.Id == "i-12");
        var influencePayload = GhgInfluencePayload.Parser.ParseFrom(influence.Payload.ToArray());
        Assert.Null(influencePayload.SourceAttachment);
        Assert.Equal("transistor-invented", influencePayload.FromElementId);
    }

    [Fact]
    public void TheToolbox_OffersATrendATriggerAndANote_EachDroppingItsAdd()
    {
        Assert.Equal(
            [
                ("Trend", GhgContextActionProvider.AddTrendActionId),
                ("Trigger", GhgContextActionProvider.AddTriggerActionId),
                ("Note", GhgContextActionProvider.AddNoteActionId),
            ],
            new GhgToolboxProvider().Items.Select(item => (item.Label, item.DropActionId)));
    }

    [Fact]
    public async Task ATriggersRows_AreItsNameDateTagsAndDescription_AndANotesItsText()
    {
        var properties = new GhgContextPropertyProvider(_historyStacks, _store);

        var trigger = await properties.DescribeAsync(Target("transistor-invented"), TestContext.Current.CancellationToken);
        Assert.Equal(
            [GhgContextPropertyProvider.NameProperty, GhgContextPropertyProvider.DescriptionProperty, GhgContextPropertyProvider.TagsProperty, GhgContextPropertyProvider.DateProperty],
            trigger.Select(row => row.Id));
        Assert.Equal("1947-12", trigger.Single(row => row.Id == GhgContextPropertyProvider.DateProperty).Value);
        Assert.Contains("electronics", trigger.Single(row => row.Id == GhgContextPropertyProvider.TagsProperty).Choices);

        var note = await properties.DescribeAsync(Target("note-1"), TestContext.Current.CancellationToken);
        Assert.Equal("Dates are illustrative.\n\nSee the readme.", note.Single(row => row.Id == GhgContextPropertyProvider.TextProperty).Value);
        Assert.Equal("160 x 64", note.Single(row => row.Id == GhgContextPropertyProvider.SizeProperty).Value);
    }

    [Fact]
    public async Task ATriggersDate_SetInTheGrid_IsOneUndoAway()
    {
        var properties = new GhgContextPropertyProvider(_historyStacks, _store);

        var result = await properties.SetAsync(Target("transistor-invented"), GhgContextPropertyProvider.DateProperty, "1948-06", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(M(1948, 6), Parse().Triggers.Single().Date);
        Assert.True((await _historyStacks.Get(_folder).UndoAsync(TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(_original, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ATriggerAndANote_OfferRenameAndRemove_AndAPlacementOffersAllThreeAdds()
    {
        var actions = new GhgContextActionProvider(_historyStacks, _store);

        async Task<IReadOnlyList<string>> IdsOf(string id) =>
            [.. (await actions.DiscoverAsync(Target(id), TestContext.Current.CancellationToken)).SelectMany(group => group.Actions).Select(action => action.Id)];

        Assert.Equal([GhgContextActionProvider.RenameActionId, GhgContextActionProvider.RemoveActionId, GhgContextActionProvider.ArrangeActionId], await IdsOf("transistor-invented"));
        Assert.Equal([GhgContextActionProvider.RenameActionId, GhgContextActionProvider.RemoveActionId, GhgContextActionProvider.ArrangeActionId], await IdsOf("note-1"));
        Assert.Equal(
            [GhgContextActionProvider.AddTrendActionId, GhgContextActionProvider.AddTriggerActionId, GhgContextActionProvider.AddNoteActionId, GhgContextActionProvider.ArrangeActionId],
            await IdsOf(GestureIds.Placement(10, 10)));

        var editNote = await actions.ExecuteAsync(Target("note-1"), GhgContextActionProvider.RenameActionId, TestContext.Current.CancellationToken);
        var input = Assert.IsType<ContextExecutionRequiresInput>(editNote);
        Assert.Equal(("Text", "Dates are illustrative.\n\nSee the readme.", "note-1"), (input.Request.FieldLabel, input.Request.InitialValue, input.Request.InlineLabelElementId));
    }

    /// <summary>Requirement 7.5: a graph that could not be read offers nothing that edits, and every new command refuses.</summary>
    [Fact]
    public async Task AGraphThatCouldNotBeRead_RefusesEveryNewCommand()
    {
        var store = new GhgDocumentStore();
        await using (new FileStream(Body, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(store.GetOrLoad(Body).IsUsable);
        }

        var dispatcher = new GhgTestDispatcher(store);
        ICommand[] commands =
        [
            new AddGhgTriggerCommand(Body, 0, 0),
            new AddGhgNoteCommand(Body, 0, 0),
            new SetGhgNoteSizeCommand(Body, "note-1", "10 x 10"),
            new RenameGhgElementCommand(Body, "transistor-invented", "X"),
            new RemoveGhgElementCommand(Body, "note-1"),
        ];
        foreach (var command in commands)
        {
            Assert.False((await dispatcher.DispatchAsync(command, TestContext.Current.CancellationToken)).IsSuccess, command.GetType().Name);
        }

        var groups = await new GhgContextActionProvider(_historyStacks, store).DiscoverAsync(Target("transistor-invented"), TestContext.Current.CancellationToken);
        Assert.Empty(groups);
        Assert.Equal(_original, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));
    }
}
