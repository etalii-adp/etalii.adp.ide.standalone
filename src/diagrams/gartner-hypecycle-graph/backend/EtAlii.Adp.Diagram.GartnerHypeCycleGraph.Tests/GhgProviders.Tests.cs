using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests;

/// <summary>
/// Task 19: the toolbox, the property grid and the menus answer the client's ids, and every edit they
/// make is one of task 18's commands - on the technology-trends example, through the real store.
/// </summary>
public sealed class GhgProvidersTests : IDisposable
{
    private readonly string _folder = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.GhgProvidersTests", Guid.NewGuid().ToString("N"));
    private readonly GhgDocumentStore _store = new();
    private readonly HistoryStackStore _historyStacks;
    private readonly GhgContextPropertyProvider _properties;
    private readonly GhgContextActionProvider _actions;
    private readonly byte[] _original;

    public GhgProvidersTests()
    {
        Directory.CreateDirectory(_folder);
        File.Copy(GhgModuleFiles.Example, Body);
        _original = File.ReadAllBytes(Body);
        _historyStacks = new HistoryStackStore(new GhgTestDispatcher(_store));
        _properties = new GhgContextPropertyProvider(_historyStacks, _store);
        _actions = new GhgContextActionProvider(_historyStacks, _store);
    }

    private string Body => IoPath.Combine(_folder, "technology-trends.ghg");

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

    private ContextTarget Target(string elementId) =>
        new(ContextScope.DiagramElement, Body, IsContainer: false, SourceId: default, _folder, ShortGuid.NewShortGuid(), elementId);

    private async Task<IReadOnlyList<ContextPropertyDefinition>> RowsOf(string elementId, GhgContextPropertyProvider? provider = null) =>
        await (provider ?? _properties).DescribeAsync(Target(elementId), TestContext.Current.CancellationToken);

    private GhgModel Parse() => GhgParser.Parse(LineDocument.Parse(File.ReadAllText(Body)));

    /// <summary>The toolbox is data: one Trend item whose drop runs the add action.</summary>
    [Fact]
    public void TheToolbox_OffersATrend_ThatDropsTheAddAction()
    {
        var item = Assert.Single(new GhgToolboxProvider().Items);

        Assert.Equal("Trend", item.Label);
        Assert.Equal(GhgContextActionProvider.AddTrendActionId, item.DropActionId);
    }

    /// <summary>Requirement 4.1: a drop starts the trend at the month under it, showing all four phases.</summary>
    [Fact]
    public async Task ADropMidMonth_StartsAtThatMonth_WithFourPhases()
    {
        // Two and a half units into 2030-03, on row 90's middle.
        var x = GhgScale.XOf(M(2030, 3)) + 2.5;
        var y = GhgScale.TopOf(90) + (GhgScale.TrendHeight / 2);

        var result = await _actions.ExecuteAsync(Target(GestureIds.Placement(x, y)), GhgContextActionProvider.AddTrendActionId, TestContext.Current.CancellationToken);

        Assert.IsType<ContextExecutionCompleted>(result);
        var added = Assert.Single(Parse().Trends, trend => trend.Name == "New trend");
        Assert.Equal((M(2030, 3), 90, 4), (added.Start!.Value, added.Row, added.Phases));
    }

    /// <summary>
    /// Requirement 5.2: the Phases row is an ordered slider of exactly the four stops - the case a
    /// provider that offers it as a CHOICE fails.
    /// </summary>
    [Fact]
    public async Task ThePhasesRow_IsASlider_OfExactlyTheFourStops()
    {
        var phases = Assert.Single(await RowsOf("steam-engine"), row => row.Id == GhgContextPropertyProvider.PhasesProperty);

        Assert.Equal(ContextPropertyEditor.Slider, phases.Editor);
        Assert.Equal(["Peak", "Peak and Trough", "Peak, Trough and Slope", "All four"], phases.Candidates);
        Assert.Equal("All four", phases.Value);
    }

    /// <summary>
    /// The Tags row is a TAGS editor looking typed text up among every tag the graph uses, each
    /// once - the case a provider sending a plain LINE, or only the trend's own tags, fails.
    /// </summary>
    [Fact]
    public async Task TheTagsRow_IsATagEditor_OverEveryTagTheGraphUses()
    {
        var tags = Assert.Single(await RowsOf("steam-engine"), row => row.Id == GhgContextPropertyProvider.TagsProperty);

        Assert.Equal(ContextPropertyEditor.Tags, tags.Editor);
        Assert.Equal("energy, industry", tags.Value);
        var used = Parse().Trends.SelectMany(trend => trend.Tags).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        Assert.Equal(used, tags.Choices.Order(StringComparer.Ordinal));
        Assert.Contains("transport", tags.Choices);
    }

    [Fact]
    public async Task ATrend_OffersItsRows_AndOneBoundaryRowPerDrawnBoundary()
    {
        var rows = (await RowsOf("steam-engine")).Select(row => row.Id).ToList();

        Assert.Equal(
        [
            GhgContextPropertyProvider.NameProperty, GhgContextPropertyProvider.DescriptionProperty, GhgContextPropertyProvider.TagsProperty,
            GhgContextPropertyProvider.StartProperty, GhgContextPropertyProvider.StopProperty, GhgContextPropertyProvider.PhasesProperty,
            .. GhgContextPropertyProvider.BoundaryProperties,
        ], rows);
        Assert.All(await RowsOf("steam-engine"), row => Assert.True(row.IsEditable, row.Id));
    }

    /// <summary>Requirement 6.5: an influence's From and To are shown, not edited.</summary>
    [Fact]
    public async Task AnInfluencesFromAndTo_AreReadOnly()
    {
        var rows = await RowsOf("steam-engine--railways");

        var from = Assert.Single(rows, row => row.Id == GhgContextPropertyProvider.FromProperty);
        var to = Assert.Single(rows, row => row.Id == GhgContextPropertyProvider.ToProperty);
        Assert.False(from.IsEditable);
        Assert.False(to.IsEditable);
        Assert.StartsWith("Steam engine · ", from.Value, StringComparison.Ordinal);
        Assert.StartsWith("Railways · ", to.Value, StringComparison.Ordinal);

        // And the context service refuses a set on them for exactly that reason.
        var resolver = new ContextPropertyResolver([_properties]);
        var set = await resolver.SetAsync(Target("steam-engine--railways"), GhgContextPropertyProvider.FromProperty, "coal", TestContext.Current.CancellationToken);
        Assert.False(set.IsSuccess);
        Assert.Equal(_original, File.ReadAllBytes(Body));
    }

    /// <summary>
    /// Requirement 11.4: read-only offers no action and no writable row. The backend's read-only case
    /// is a document that could not be read - held by another program at its first open.
    /// </summary>
    [Fact]
    public async Task ADocumentThatCouldNotBeRead_OffersNoActionAndNoWritableRow()
    {
        var store = new GhgDocumentStore();
        using (new FileStream(Body, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(store.GetOrLoad(Body).IsUsable);
        }

        using var historyStacks = new HistoryStackStore(new GhgTestDispatcher(store));
        var actions = new GhgContextActionProvider(historyStacks, store);
        var properties = new GhgContextPropertyProvider(historyStacks, store);

        Assert.Empty(await actions.DiscoverAsync(Target("railways"), TestContext.Current.CancellationToken));
        Assert.Empty(await actions.DiscoverAsync(Target(GestureIds.Placement(1, 2)), TestContext.Current.CancellationToken));
        Assert.All(await RowsOf("railways", properties), row => Assert.False(row.IsEditable, row.Id));
        Assert.IsType<ContextExecutionFailed>(await actions.ExecuteAsync(Target(GestureIds.Placement(1, 2)), GhgContextActionProvider.AddTrendActionId, TestContext.Current.CancellationToken));
        Assert.Equal(_original, File.ReadAllBytes(Body));
    }

    public static TheoryData<string, string, string> EverySet => new()
    {
        { "railways", GhgContextPropertyProvider.NameProperty, "Railroads" },
        { "railways", GhgContextPropertyProvider.DescriptionProperty, "From Stockton and Darlington onwards." },
        { "railways", GhgContextPropertyProvider.TagsProperty, "transport" },
        { "railways", GhgContextPropertyProvider.StartProperty, "1830-01" },
        { "railways", GhgContextPropertyProvider.StopProperty, "1950-06" },
        { "railways", GhgContextPropertyProvider.PhasesProperty, "Peak and Trough" },
        { "steam-engine", GhgContextPropertyProvider.BoundaryProperties[2], "1850-01" },
        { "steam-engine--railways", GhgContextPropertyProvider.DescriptionProperty, "" },
        { "steam-engine--railways", GhgContextPropertyProvider.ToAttachmentProperty, "slope/top/0.25" },
    };

    /// <summary>
    /// The task's guard: a set through the context service - which lets a set reach a provider only
    /// for a row it describes as editable - reaches the document, and one undo gives the bytes back.
    /// </summary>
    [Theory]
    [MemberData(nameof(EverySet))]
    public async Task APropertySetThroughTheService_ReachesTheDocument_AndUndoes(string id, string property, string value)
    {
        var resolver = new ContextPropertyResolver([_properties]);

        var result = await resolver.SetAsync(Target(id), property, value, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotEqual(_original, File.ReadAllBytes(Body));

        var undone = await _historyStacks.Get(_folder).UndoAsync(TestContext.Current.CancellationToken);

        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Equal(_original, File.ReadAllBytes(Body));
    }

    /// <summary>
    /// Requirement 6.2: a finished connect gesture carries both attachments in its id, and the command
    /// behind it refuses what the rules refuse.
    /// </summary>
    [Fact]
    public async Task AFinishedConnectGesture_ConnectsWithItsAttachments_AndARefusedOneSaysWhy()
    {
        var gesture = GhgGestures.Relation("railways", new GhgEnd("plateau", "bottom", 0.3), "coal", new GhgEnd("slope", "top", 0.6));

        var allowed = await _actions.ExecuteAsync(Target(gesture), GhgContextActionProvider.ConnectActionId, TestContext.Current.CancellationToken);
        var refused = await _actions.ExecuteAsync(
            Target(GestureIds.Relation("steam-engine", "railways")), GhgContextActionProvider.ConnectActionId, TestContext.Current.CancellationToken);

        Assert.IsType<ContextExecutionCompleted>(allowed);
        var drawn = Assert.Single(Parse().Influences, influence => influence.From == "railways" && influence.To == "coal");
        Assert.Equal((new GhgEnd("plateau", "bottom", 0.3), new GhgEnd("slope", "top", 0.6)), (drawn.FromEnd, drawn.ToEnd));
        Assert.Contains("already influences", Assert.IsType<ContextExecutionFailed>(refused).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APlacementAndAGesture_DiscoverExactlyTheClientsIds()
    {
        var adds = await _actions.DiscoverAsync(Target(GestureIds.Placement(1, 2)), TestContext.Current.CancellationToken);
        var connects = await _actions.DiscoverAsync(Target(GestureIds.Relation("a", "b")), TestContext.Current.CancellationToken);

        Assert.Equal(["ghg.add.trend"], adds.SelectMany(group => group.Actions).Select(action => action.Id));
        Assert.Equal(["ghg.connect.influence"], connects.SelectMany(group => group.Actions).Select(action => action.Id));
    }

    [Fact]
    public async Task EvenPhases_IsOfferedOnlyWhenABoundaryIsDragged()
    {
        var dragged = await _actions.DiscoverAsync(Target("steam-engine"), TestContext.Current.CancellationToken);
        var even = await _actions.DiscoverAsync(Target("railways"), TestContext.Current.CancellationToken);

        Assert.Contains(GhgContextActionProvider.EvenPhasesActionId, dragged.SelectMany(group => group.Actions).Select(action => action.Id));
        Assert.DoesNotContain(GhgContextActionProvider.EvenPhasesActionId, even.SelectMany(group => group.Actions).Select(action => action.Id));
    }

    [Fact]
    public async Task RemovingATrendWithInfluences_AsksFirst_AndTheCommitRemovesThemAll()
    {
        var asked = await _actions.ExecuteAsync(Target("steamboats"), GhgContextActionProvider.RemoveActionId, TestContext.Current.CancellationToken);
        var committed = await _actions.CommitAsync(Target("steamboats"), GhgContextActionProvider.RemoveActionId, "", "", TestContext.Current.CancellationToken);

        Assert.Contains("2 influences", Assert.IsType<ContextExecutionRequiresConfirmation>(asked).Request.Message, StringComparison.Ordinal);
        Assert.Equal(ContextCommitResult.Succeeded, committed);
        Assert.DoesNotContain(Parse().Influences, influence => influence.From == "steamboats" || influence.To == "steamboats");
    }
}
