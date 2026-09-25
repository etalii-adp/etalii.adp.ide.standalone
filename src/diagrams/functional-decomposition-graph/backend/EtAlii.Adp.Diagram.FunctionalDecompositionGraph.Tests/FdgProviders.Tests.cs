using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests;

/// <summary>
/// Task 13: the toolbox, the property grid and the menus answer the client's ids, and every edit
/// they make is one of task 12's commands - on the field-service example, through the real store.
/// </summary>
public sealed class FdgProvidersTests : IDisposable
{
    private readonly string _folder = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.FdgProvidersTests", Guid.NewGuid().ToString("N"));
    private readonly FdgDocumentStore _store = new();
    private readonly HistoryStackStore _historyStacks;
    private readonly FdgContextPropertyProvider _properties;
    private readonly FdgContextActionProvider _actions;
    private readonly byte[] _original;

    public FdgProvidersTests()
    {
        Directory.CreateDirectory(_folder);
        File.Copy(FieldServiceExample.Path, Body);
        _original = File.ReadAllBytes(Body);
        _historyStacks = new HistoryStackStore(new FdgTestDispatcher(_store));
        _properties = new FdgContextPropertyProvider(_historyStacks, _store);
        _actions = new FdgContextActionProvider(_historyStacks, _store);
    }

    private string Body => IoPath.Combine(_folder, "field-service.fdg");

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

    private ContextTarget Target(string elementId) =>
        new(ContextScope.DiagramElement, Body, IsContainer: false, SourceId: default, _folder, ShortGuid.NewShortGuid(), elementId);

    private async Task<IReadOnlyList<string>> RowIdsOf(string elementId) =>
        [.. (await _properties.DescribeAsync(Target(elementId), TestContext.Current.CancellationToken)).Select(row => row.Id)];

    private FdgModel Parse() => FdgParser.Parse(LineDocument.Parse(File.ReadAllText(Body)));

    /// <summary>One element of each of the five types in the example, and one connection.</summary>
    public static TheoryData<string, string> EachTypeAndAConnection => new()
    {
        { "planning", FdgElementTypes.UiElement },
        { "open-task", FdgElementTypes.Action },
        { "planning-day", FdgElementTypes.DataElement },
        { "sync-queue", FdgElementTypes.Function },
        { "note-offline", FdgElementTypes.Comment },
        { "c-open-shows-detail", "connection" },
    };

    /// <summary>
    /// The task's guard: each of the five types and a connection offers a Description - asserted per
    /// type, so a provider that drops it for one names that type.
    /// </summary>
    [Theory]
    [MemberData(nameof(EachTypeAndAConnection))]
    public async Task EveryTypeAndAConnection_OffersADescription(string id, string what)
    {
        // Act.
        var rows = await RowIdsOf(id);

        // Assert.
        Assert.True(rows.Contains(FdgContextPropertyProvider.DescriptionProperty), $"A {what} offers no Description row.");
    }

    [Theory]
    [InlineData("planning")]
    [InlineData("open-task")]
    [InlineData("planning-day")]
    [InlineData("sync-queue")]
    public async Task TheFourNamedTypes_OfferAName_AndNoText(string id)
    {
        // Act.
        var rows = await RowIdsOf(id);

        // Assert.
        Assert.Contains(FdgContextPropertyProvider.NameProperty, rows);
        Assert.DoesNotContain(FdgContextPropertyProvider.TextProperty, rows);
        Assert.DoesNotContain(FdgContextPropertyProvider.HeightProperty, rows);
    }

    /// <summary>The task's guard: a Comment has no name, so the grid offers none - its text instead.</summary>
    [Fact]
    public async Task AComment_OffersNoName_ButItsTextAndItsHeight()
    {
        // Act.
        var rows = await RowIdsOf("note-offline");

        // Assert.
        Assert.DoesNotContain(FdgContextPropertyProvider.NameProperty, rows);
        Assert.Contains(FdgContextPropertyProvider.TextProperty, rows);
        Assert.Contains(FdgContextPropertyProvider.HeightProperty, rows);
    }

    [Fact]
    public async Task AConnection_OffersItsName()
    {
        // Act.
        var rows = await RowIdsOf("c-open-shows-detail");

        // Assert.
        Assert.Equal([FdgContextPropertyProvider.ConnectionNameProperty, FdgContextPropertyProvider.DescriptionProperty], rows);
    }

    public static TheoryData<string, string, string> EverySet => new()
    {
        { "planning", FdgContextPropertyProvider.NameProperty, "Plan" },
        { "note-offline", FdgContextPropertyProvider.TextProperty, "Offline first." },
        { "step-list", FdgContextPropertyProvider.DescriptionProperty, "Every step of the open task." },
        { "planning", FdgContextPropertyProvider.WidthProperty, "220" },
        { "note-offline", FdgContextPropertyProvider.HeightProperty, "140" },
        { "c-sync-upload", FdgContextPropertyProvider.ConnectionNameProperty, "invokes" },
        { "c-open-shows-detail", FdgContextPropertyProvider.DescriptionProperty, "" },
    };

    /// <summary>
    /// The task's guard: a property set through the grid reaches the document, and one undo gives the
    /// original bytes back - including the two sizes a canvas resize sets.
    /// </summary>
    [Theory]
    [MemberData(nameof(EverySet))]
    public async Task APropertySetThroughTheGrid_ReachesTheDocument_AndUndoes(string id, string property, string value)
    {
        // Act.
        var result = await _properties.SetAsync(Target(id), property, value, TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.NotEqual(_original, File.ReadAllBytes(Body));

        // Act: one undo, through the history the set used.
        var undone = await _historyStacks.Get(_folder).UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Equal(_original, File.ReadAllBytes(Body));
    }

    /// <summary>
    /// A canvas resize goes through the context service, which lets a set reach a provider ONLY for a
    /// property it describes as editable - so the width row, and a Comment's height row, are what make
    /// a resize possible at all. Calling the provider directly would skip exactly that check.
    /// </summary>
    [Fact]
    public async Task ACanvasResize_PassesTheServicesOwnCheck_ForAWidth_AndForACommentsHeightOnly()
    {
        // Arrange.
        var resolver = new ContextPropertyResolver([_properties]);

        // Act.
        var width = await resolver.SetAsync(Target("planning"), FdgContextPropertyProvider.WidthProperty, "220", TestContext.Current.CancellationToken);
        var height = await resolver.SetAsync(Target("note-offline"), FdgContextPropertyProvider.HeightProperty, "140", TestContext.Current.CancellationToken);
        var noHeight = await resolver.SetAsync(Target("planning"), FdgContextPropertyProvider.HeightProperty, "140", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(width.IsSuccess, width.Error);
        Assert.True(height.IsSuccess, height.Error);
        Assert.False(noHeight.IsSuccess);
        var model = Parse();
        Assert.Equal(220d, model.Elements.Single(element => element.Id == "planning").Width);
        Assert.Equal(140d, model.Elements.Single(element => element.Id == "note-offline").Height);
    }

    [Theory]
    [InlineData("planning", FdgContextPropertyProvider.WidthProperty, "wide")]
    [InlineData("planning", FdgContextPropertyProvider.WidthProperty, "0")]
    [InlineData("note-offline", FdgContextPropertyProvider.HeightProperty, "-5")]
    public async Task ASizeThatIsNotOne_IsRefused_AndNothingIsWritten(string id, string property, string value)
    {
        // Act.
        var result = await _properties.SetAsync(Target(id), property, value, TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal(_original, File.ReadAllBytes(Body));
    }

    [Fact]
    public async Task ADrop_AddsItsTypeCentredWhereItLanded()
    {
        // Act.
        var result = await _actions.ExecuteAsync(Target(GestureIds.Placement(300, 700)), "fdg.add.action", TestContext.Current.CancellationToken);

        // Assert: the new Action's top-left is the centre less half its size (160 x 48).
        Assert.IsType<ContextExecutionCompleted>(result);
        var added = Assert.Single(Parse().Elements, element => element.Name == "New action");
        Assert.Equal((220d, 676d), (added.X, added.Y));
    }

    [Fact]
    public async Task AFinishedConnectGesture_Connects_AndARefusedOneSaysWhy()
    {
        // Act: Tick step shows nothing yet, so it may show the Step list; Open task already shows one.
        var allowed = await _actions.ExecuteAsync(Target(GestureIds.Relation("tick-step", "step-list")), "fdg.connect.shows", TestContext.Current.CancellationToken);
        var refused = await _actions.ExecuteAsync(Target(GestureIds.Relation("open-task", "task-list")), "fdg.connect.shows", TestContext.Current.CancellationToken);

        // Assert.
        Assert.IsType<ContextExecutionCompleted>(allowed);
        Assert.Contains(Parse().Connections, connection => connection.From == "tick-step" && connection.To == "step-list");
        Assert.Contains("cardinality check", Assert.IsType<ContextExecutionFailed>(refused).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenamingAComment_OpensItsTextInPlace()
    {
        // Act.
        var result = await _actions.ExecuteAsync(Target("note-offline"), FdgContextActionProvider.RenameActionId, TestContext.Current.CancellationToken);

        // Assert.
        var request = Assert.IsType<ContextExecutionRequiresInput>(result).Request;
        Assert.Equal("note-offline", request.InlineLabelElementId);
        Assert.StartsWith("Everything on these screens must work offline.", request.InitialValue, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemovingAnElementWithConnections_AsksFirst_AndTheCommitRemovesThemAll()
    {
        // Act: Task row has its parent link and the Action it offers.
        var asked = await _actions.ExecuteAsync(Target("task-row"), FdgContextActionProvider.RemoveActionId, TestContext.Current.CancellationToken);
        var committed = await _actions.CommitAsync(Target("task-row"), FdgContextActionProvider.RemoveActionId, "", "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains("2 connections", Assert.IsType<ContextExecutionRequiresConfirmation>(asked).Request.Message, StringComparison.Ordinal);
        Assert.Equal(ContextCommitResult.Succeeded, committed);
        Assert.DoesNotContain(Parse().Connections, connection => connection.From == "task-row" || connection.To == "task-row");
    }

    [Fact]
    public async Task APlacementAndAGesture_DiscoverExactlyTheClientsIds()
    {
        // Act.
        var adds = await _actions.DiscoverAsync(Target(GestureIds.Placement(1, 2)), TestContext.Current.CancellationToken);
        var connects = await _actions.DiscoverAsync(Target(GestureIds.Relation("a", "b")), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(
            ["fdg.add.ui-element", "fdg.add.data-element", "fdg.add.action", "fdg.add.function", "fdg.add.comment"],
            adds.SelectMany(group => group.Actions).Select(action => action.Id));
        Assert.Equal(
            ["fdg.connect.ui-child", "fdg.connect.owns-action", "fdg.connect.owns-data", "fdg.connect.owns-function", "fdg.connect.shows"],
            connects.SelectMany(group => group.Actions).Select(action => action.Id));
    }

    [Fact]
    public void TheToolbox_OffersTheFiveTypes_EachDroppingItsAddAction()
    {
        // Act.
        var items = new FdgToolboxProvider().Items;

        // Assert.
        Assert.Equal(
            ["fdg.add.ui-element", "fdg.add.action", "fdg.add.data-element", "fdg.add.function", "fdg.add.comment"],
            items.Select(item => item.DropActionId));
    }
}
