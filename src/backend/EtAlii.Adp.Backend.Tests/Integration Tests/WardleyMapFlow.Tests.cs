using EtAlii.Adp.Authentication.Wire;
using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Diagram.WardleyMap;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.Hierarchy.Wire;
using EtAlii.Adp.Projects;
using EtAlii.Adp.Projects.Wire;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using HierarchyService = EtAlii.Adp.Hierarchy.Wire.HierarchyService;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here
using Path = EtAlii.Adp.Common.Wire.Path;
using ProjectService = EtAlii.Adp.Projects.Wire.ProjectService;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The whole Wardley arc over the real host: create, open, drag, undo, select, describe, two
/// connections, and an edit made outside ADP (wardley-map Requirements 1, 2.5, 7.2, 10, 11, 15).
/// </summary>
/// <remarks>
/// <para>
/// <b>Here rather than in the module's own test project</b>, which task 25 named. A real-host
/// test needs <c>EtAlii.Adp.Backend.Service</c>, and a module referencing the host inverts the
/// dependency the whole pluggable design rests on - Requirement 12.4's direction. The three
/// modules that already have flow tests (C4, ansible, mindmap) all put them here for the same
/// reason, and the module keeps its own unit tests where they are.
/// </para>
/// <para>
/// These are the seams meeting, which no unit test shows: the router finding the body, the
/// session mapping it, the history owning the edit, the context service pushing the selection,
/// and the file on disk agreeing with all of them.
/// </para>
/// </remarks>
public class WardleyMapFlowTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string DeveloperUsername = "admin";
    private const string DeveloperCredential = "changeme";
    private const string SessionTokenHeader = "session-token";
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StreamStartupGrace = TimeSpan.FromMilliseconds(250);

    private const string Map = """
        title Tea Shop
        anchor Business [0.95, 0.63]
        component Cup of Tea [0.79, 0.61]
        component Kettle [0.43, 0.35]
        Cup of Tea->Kettle

        """;

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _appDataRoot;
    private readonly string _projectFolder;

    public WardleyMapFlowTests(WebApplicationFactory<Program> baseFactory)
    {
        _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.IntegrationTests", Guid.NewGuid().ToString("N"));
        _projectFolder = IoPath.Combine(_appDataRoot, "wardley-project");
        Directory.CreateDirectory(_projectFolder);

        File.WriteAllText(IoPath.Combine(_projectFolder, "tea.adp"), "wardley/map\n");
        File.WriteAllText(IoPath.Combine(_projectFolder, "tea.owm"), Map);

        // A body with no `.adp` beside it: Requirement 2.5's bare-document open.
        File.WriteAllText(IoPath.Combine(_projectFolder, "bare.owm"), "anchor Need [0.9, 0.5]\ncomponent Thing [0.4, 0.4]\n");

        _factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("developer");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IProjectStore>();
                services.AddSingleton<IProjectStore>(new FileProjectStore(_appDataRoot));
                // The problem cache must live and die with this test, not in the real user
                // profile the host's AddProblems registration points at. A host booted without
                // this override leaves a cache file behind naming a temp folder that is deleted
                // moments later, and every later run then walks that dead root at startup: 605
                // such files had accumulated, costing the suite 22,591 warnings and an apparent
                // hang. DiagramToolboxFlowTests fixed this for itself; it never generalised.
                services.RemoveAll<Problems.IProblemStore>();
                services.AddSingleton<Problems.IProblemStore>(provider => new Problems.ProblemStore(
                    _appDataRoot,
                    provider.GetRequiredService<DiagramFileRouter>(),
                    provider.GetRequiredService<Common.DiagramValidators>()));
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        TestFolder.TryDelete(_appDataRoot);
    }

    // ---- opening ------------------------------------------------------------------------------

    [Fact]
    public async Task AMapOpens_AndDeliversItsElementsAndItsEvolutionAxis()
    {
        // Arrange.
        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var client = new DiagramService.DiagramServiceClient(channel);

        // Act.
        var elements = await BaselineAsync(client, headers, projectId, "tea.adp");

        // Assert. The axis goes out as a map-level element, so the client draws the bands it is
        // told about rather than constants it holds (Requirement 8.2).
        Assert.Contains(elements, element => element.Type == WardleyElementTypes.EvolutionAxis);
        Assert.Equal(3, elements.Count(element => element.Type == WardleyElementTypes.Element));
        Assert.Single(elements, element => element.Type == WardleyElementTypes.Link);
    }

    [Fact]
    public async Task AnOwmWithNoAdpSibling_OpensThroughTheRouter()
    {
        // Arrange. Requirement 2.5 - the extension is declared, so the body is openable on its
        // own without a registration being invented for it.
        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var client = new DiagramService.DiagramServiceClient(channel);

        // Act.
        var elements = await BaselineAsync(client, headers, projectId, "bare.owm");

        // Assert.
        Assert.Equal(2, elements.Count(element => element.Type == WardleyElementTypes.Element));
    }

    [Fact]
    public async Task CreatingAMapThroughTheSharedAddAction_WritesBothFiles()
    {
        // Arrange. Requirement 1.6 - the `.adp` carries the MIME line and the `.owm` sibling is
        // created beside it by this module's document factory, through the shared command.
        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();
        var contextClient = new ContextService.ContextServiceClient(channel);

        using var cts = CreateMessageTimeout();
        using var contextCall = contextClient.Watch(
            new WatchContextRequest { ProjectId = projectId, WatchId = watchId },
            headers, cancellationToken: TestContext.Current.CancellationToken);
        var pendingPrompt = ReadUntilPromptAsync(contextCall.ResponseStream, cts.Token);
        await Task.Delay(StreamStartupGrace, TestContext.Current.CancellationToken);

        // No source: the add lands on the connection's current selection, which with nothing
        // selected is the project's root folder - where a user's first "add a diagram" happens.
        var interactionId = ShortGuid.NewShortGuid();
        var executed = await contextClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                InteractionId = interactionId,
                ActionId = AddDiagramContextActionProvider.AddActionId,
            },
            headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(executed.Accepted, executed.Error);

        var prompt = await pendingPrompt;
        var wardley = FirstLeafNamed(prompt.ChoiceDialog.Options, EtAlii.Adp.Diagram.WardleyMap.Diagram.WardleyMap.Origin.Key);
        Assert.NotNull(wardley);

        // Act.
        var submitted = await contextClient.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = interactionId, Value = wardley.Id, Text = "strategy" },
            headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert. Both files, and the `.adp` carrying exactly its MIME line.
        Assert.True(submitted.Completed, submitted.Error);
        Assert.Equal("wardley/map\r\n", File.ReadAllText(IoPath.Combine(_projectFolder, "strategy.adp")));
        Assert.True(File.Exists(IoPath.Combine(_projectFolder, "strategy.owm")), "the .owm sibling was not created");
    }

    // ---- dragging, which is an edit -------------------------------------------------------------

    [Fact]
    public async Task ADrag_RewritesTheFile_AndAnUndoLeavesItByteIdentical()
    {
        // Arrange. Requirement 7.2 - position is meaning here, so a drag goes through the
        // history like every other edit and lands in the `.owm`.
        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();
        var client = new DiagramService.DiagramServiceClient(channel);
        var contextClient = new ContextService.ContextServiceClient(channel);

        // Arrange, continued. The move is refused unless the diagram is open on THIS
        // connection - core will not edit a document a caller is not looking at - so the stream
        // stays open for the length of the test.
        using var cts = CreateMessageTimeout();
        var path = new Path();
        path.Segments.Add("tea.adp");
        using var call = client.Open(
            new OpenDiagramRequest { ProjectId = projectId, WatchId = watchId, Path = path },
            headers, cancellationToken: cts.Token);

        var elements = await ReadAddAsync(call.ResponseStream, cts.Token);
        var kettle = elements.Single(element =>
            element.Type == WardleyElementTypes.Element &&
            WardleyElementPayload.Parser.ParseFrom(element.Payload.Value.Span).Name == "Kettle");

        // Act. The canvas hands over a Point2D; the module flips it back into the document's
        // own [visibility, maturity] order.
        var moved = await client.MoveElementAsync(
            new MoveElementRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Path = path,
                ElementId = kettle.Id.Value,
                Position = new Point2D { X = 0.8d, Y = 0.4d },
            },
            headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert. y = 0.4 is a visibility of 0.6; x = 0.8 is the maturity as written.
        Assert.Equal("", moved.Error);
        var text = File.ReadAllText(IoPath.Combine(_projectFolder, "tea.owm"));
        Assert.Contains("component Kettle [0.6, 0.8]", text, StringComparison.Ordinal);

        // Act, continued.
        var undone = await ExecuteProjectActionAsync(contextClient, projectId, watchId, headers, HistoryContextActionProvider.UndoActionId);

        // Assert. Byte-identical, not merely equivalent: the undo restores the LINE, so
        // `[0.43, 0.35]` does not come back as a differently-rounded pair (Requirement 3.1).
        Assert.True(undone.Accepted, undone.Error);
        Assert.Equal(Map, File.ReadAllText(IoPath.Combine(_projectFolder, "tea.owm")));
    }

    [Fact]
    public async Task ADragByOneConnection_ReachesTheOther()
    {
        // Arrange. Requirement 10.6 - two connections hold ONE document between them, so an
        // edit through either is visible to the other without a reload.
        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var client = new DiagramService.DiagramServiceClient(channel);

        using var cts = CreateMessageTimeout();
        var path = new Path();
        path.Segments.Add("tea.adp");

        // Two connections, each with the map open: the one that watches, and the one that drags.
        var watching = ShortGuid.NewShortGuid();
        var dragging = ShortGuid.NewShortGuid();

        using var watcher = client.Open(
            new OpenDiagramRequest { ProjectId = projectId, WatchId = watching, Path = path },
            headers, cancellationToken: cts.Token);
        using var dragger = client.Open(
            new OpenDiagramRequest { ProjectId = projectId, WatchId = dragging, Path = path },
            headers, cancellationToken: cts.Token);

        var baseline = await ReadAddAsync(watcher.ResponseStream, cts.Token);
        await ReadAddAsync(dragger.ResponseStream, cts.Token);
        var kettle = baseline.Single(element =>
            element.Type == WardleyElementTypes.Element &&
            WardleyElementPayload.Parser.ParseFrom(element.Payload.Value.Span).Name == "Kettle");

        // Act. The drag happens on the second connection; the first never asked for it.
        var pendingUpdate = ReadAddAsync(watcher.ResponseStream, cts.Token);
        await client.MoveElementAsync(
            new MoveElementRequest
            {
                ProjectId = projectId,
                WatchId = dragging,
                Path = path,
                ElementId = kettle.Id.Value,
                Position = new Point2D { X = 0.8d, Y = 0.4d },
            },
            headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert. The watching connection is told, and the element keeps its id across the edit
        // (Requirement 4.1) - a move is a move, not a delete and an add.
        var update = await pendingUpdate;
        var moved = update.Single(element => element.Id.Value == kettle.Id.Value);
        Assert.Equal(0.8d, WardleyElementPayload.Parser.ParseFrom(moved.Payload.Value.Span).Maturity, 6);
    }

    [Fact]
    public async Task AnEditMadeOutsideAdp_IsNotSomethingUndoStepsThrough()
    {
        // Arrange. Requirement 10.7 has two halves, and only one of them can be tested here.
        //
        // The DELTAS half - an external edit reaching an open map - needs core to say that a
        // file's CONTENT changed, and core says no such thing: HierarchyModel handles the
        // watcher's Changed event with "a content change never affects the tree's shape;
        // nothing to update" and raises nothing. Every module's document store therefore has a
        // Reload method that nothing in production calls - mindmap's and C4's included. That is
        // a core gap, reported rather than taken (Requirement 12.5); the module's own half is
        // pinned by WardleySessionTests.DocumentChanged_SendsTheDifferenceToTheConnection.
        //
        // The UNDO half is true today and is what this asserts: an edit ADP did not make never
        // became a command, so there is nothing on the history to step back through and the
        // other editor's work is not silently reverted.
        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();
        var contextClient = new ContextService.ContextServiceClient(channel);

        // Act.
        await File.WriteAllTextAsync(
            IoPath.Combine(_projectFolder, "tea.owm"),
            Map + "component Water [0.38, 0.82]\n",
            TestContext.Current.CancellationToken);

        var undone = await ExecuteProjectActionAsync(contextClient, projectId, watchId, headers, HistoryContextActionProvider.UndoActionId);

        // Assert.
        Assert.False(undone.Accepted);
        Assert.Contains(
            "Water",
            await File.ReadAllTextAsync(IoPath.Combine(_projectFolder, "tea.owm"), TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
    }

    // ---- selection, actions and properties --------------------------------------------------------

    [Fact]
    public async Task SelectingAnElement_PushesItsDetail_ItsActions_AndItsProperties()
    {
        // Arrange. Requirements 11.1-11.4 and 15: one click, and three panels can answer.
        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var contextClient = new ContextService.ContextServiceClient(channel);
        var diagramClient = new DiagramService.DiagramServiceClient(channel);

        var entries = await hierarchyClient.ListEntriesAsync(
            new ListEntriesRequest { ProjectId = projectId, WatchId = watchId },
            headers, cancellationToken: TestContext.Current.CancellationToken);
        var entryId = await NestedEntryLookup.EntryIdOfAsync(hierarchyClient, projectId, watchId, headers, "tea.adp");

        var elements = await BaselineAsync(diagramClient, headers, projectId, "tea.adp");
        var kettle = elements.Single(element =>
            element.Type == WardleyElementTypes.Element &&
            WardleyElementPayload.Parser.ParseFrom(element.Payload.Value.Span).Name == "Kettle");

        using var cts = CreateMessageTimeout();
        using var contextCall = contextClient.Watch(
            new WatchContextRequest { ProjectId = projectId, WatchId = watchId },
            headers, cancellationToken: TestContext.Current.CancellationToken);
        var pendingSelection = ReadUntilSelectionWithActionsAsync(contextCall.ResponseStream, cts.Token);
        await Task.Delay(StreamStartupGrace, TestContext.Current.CancellationToken);

        // Act.
        var selected = await contextClient.SelectAsync(
            new SelectRequest { ProjectId = projectId, WatchId = watchId, Selection = ElementChain(entryId, kettle.Id.Value) },
            headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert. The pushed selection is self-describing: the innermost level carries the
        // element's name and the module's own payload, so a consumer needs no second call
        // (Requirement 11.3).
        Assert.NotNull(entries);
        Assert.Equal("", selected.Error);
        var changed = await pendingSelection;
        var innermost = changed.Levels[^1].Element;
        Assert.Equal("Kettle", innermost.Text);
        Assert.Equal(WardleyElementTypes.Element, innermost.ElementType);
        var payload = innermost.Payload.Unpack<WardleyElementPayload>();
        Assert.Equal("Custom Built", payload.EvolutionStage);

        // Assert, continued. The actions arrive with the selection, described by the backend
        // down to their shortcuts (Requirements 11.4, 11.5).
        var actions = changed.Actions.SelectMany(group => group.Actions).ToArray();
        Assert.Contains(actions, action => action.Id == WardleyContextActionProvider.RenameActionId && action.Shortcut.Key == "F2");
        Assert.Contains(actions, action => action.Id == WardleyContextActionProvider.LinkActionId);

        // Assert, continued. And the property grid can ask, and correct (Requirement 15).
        var described = await contextClient.DescribePropertiesAsync(
            new DescribePropertiesRequest { ProjectId = projectId, WatchId = watchId },
            headers, cancellationToken: TestContext.Current.CancellationToken);

        var maturity = described.Properties.Single(property => property.Id == WardleyContextPropertyProvider.MaturityPropertyId);
        Assert.Equal("0.35", maturity.Value);
        Assert.Equal("", maturity.ReadOnlyReason);

        var stage = described.Properties.Single(property => property.Id == WardleyContextPropertyProvider.StagePropertyId);
        Assert.Contains("Change Maturity instead", stage.ReadOnlyReason, StringComparison.Ordinal);

        var set = await contextClient.SetPropertyAsync(
            new SetPropertyRequest { ProjectId = projectId, WatchId = watchId, PropertyId = WardleyContextPropertyProvider.MaturityPropertyId, Value = "0.42" },
            headers, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("", set.Error);
        Assert.Contains(
            "component Kettle [0.43, 0.42]",
            await File.ReadAllTextAsync(IoPath.Combine(_projectFolder, "tea.owm"), TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SettingAProperty_LeavesTheSelectionSelected()
    {
        // Arrange. Found in the field: committing a checkbox or a name in the property grid
        // made the selection vanish. A commit rewrites the document on disk, the write's
        // watcher events reload it, and every module's selection track re-finds the element in
        // whatever the reload holds - so a reload observed mid-swap reads as "element gone"
        // and clears a selection whose element never went anywhere.
        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var contextClient = new ContextService.ContextServiceClient(channel);
        var diagramClient = new DiagramService.DiagramServiceClient(channel);

        var entryId = await NestedEntryLookup.EntryIdOfAsync(hierarchyClient, projectId, watchId, headers, "tea.adp");
        var elements = await BaselineAsync(diagramClient, headers, projectId, "tea.adp");
        var kettle = elements.Single(element =>
            element.Type == WardleyElementTypes.Element &&
            WardleyElementPayload.Parser.ParseFrom(element.Payload.Value.Span).Name == "Kettle");

        using var cts = CreateMessageTimeout();
        using var contextCall = contextClient.Watch(
            new WatchContextRequest { ProjectId = projectId, WatchId = watchId },
            headers, cancellationToken: TestContext.Current.CancellationToken);
        var pendingSelection = ReadUntilSelectionWithActionsAsync(contextCall.ResponseStream, cts.Token);
        await Task.Delay(StreamStartupGrace, TestContext.Current.CancellationToken);

        var selected = await contextClient.SelectAsync(
            new SelectRequest { ProjectId = projectId, WatchId = watchId, Selection = ElementChain(entryId, kettle.Id.Value) },
            headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("", selected.Error);
        Assert.Equal("Kettle", (await pendingSelection).Levels[^1].Element.Text);

        // Act: the property grid's commit.
        var set = await contextClient.SetPropertyAsync(
            new SetPropertyRequest { ProjectId = projectId, WatchId = watchId, PropertyId = WardleyContextPropertyProvider.MaturityPropertyId, Value = "0.42" },
            headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("", set.Error);

        // Assert: give the write's watcher storm time to land, and read every push it causes.
        // A push that carries no selection is the store clearing it - the defect. Transient
        // pushes are hover previews and are not selection changes.
        var pushes = new List<string>();
        using var settle = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        settle.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            while (await contextCall.ResponseStream.MoveNext(settle.Token))
            {
                var message = contextCall.ResponseStream.Current;
                if (message.MessageCase != ContextMessage.MessageOneofCase.Selection || message.Selection.Transient)
                {
                    continue;
                }

                pushes.Add(message.Selection.Selection is null
                    ? "CLEARED"
                    : $"levels={message.Selection.Levels.Count}");
            }
        }
        catch (OperationCanceledException)
        {
            // The settle window closing is the expected way out.
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.Cancelled)
        {
            // Same window, surfaced through gRPC.
        }

        Assert.DoesNotContain("CLEARED", pushes);
    }

    [Fact]
    public async Task TheToolboxIsDescribedForAnOpenMap()
    {
        // Arrange. Requirement 13 - the palette is data the client renders without
        // understanding, and it is answerable before the panel that shows it exists.
        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var client = new DiagramService.DiagramServiceClient(channel);

        var path = new Path();
        path.Segments.Add("tea.adp");

        // Act.
        var described = await client.DescribeToolboxAsync(
            new DescribeToolboxRequest { ProjectId = projectId, Path = path },
            headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(
            ["Component", "Anchor", "Market", "Ecosystem", "Submap", "Pipeline", "Note", "Annotation"],
            described.Items.Select(item => item.Label));
        Assert.DoesNotContain(described.Items, item => item.DropActionId.Length == 0);
    }

    // ---- plumbing -----------------------------------------------------------------------------

    private static ContextSelection ElementChain(Common.Wire.ShortGuid entryId, string elementId)
    {
        var chain = new ContextSelection
        {
            Source = ContextSelectionSource.Explorer,
            Id = new ContextSource { EntryId = entryId },
            Path = new Path(),
            Child = new ContextSelection
            {
                Source = ContextSelectionSource.DiagramCanvas,
                Id = new ContextSource { ElementId = new ElementId { Value = elementId } },
                Path = new Path(),
                None = new Empty(),
            },
        };

        return chain;
    }

    private static ContextOption? FirstLeafNamed(IEnumerable<ContextOption> options, string id)
    {
        foreach (var option in options)
        {
            if (option.Selectable && option.Id == id)
            {
                return option;
            }

            if (FirstLeafNamed(option.Children, id) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static Task<ExecuteActionResponse> ExecuteProjectActionAsync(
        ContextService.ContextServiceClient contextClient,
        Common.Wire.ShortGuid projectId,
        Common.Wire.ShortGuid watchId,
        Metadata headers,
        string actionId) =>
        contextClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Source = new ContextSource { Project = new Empty() },
                InteractionId = ShortGuid.NewShortGuid(),
                ActionId = actionId,
            },
            headers,
            cancellationToken: TestContext.Current.CancellationToken).ResponseAsync;

    private static async Task<IReadOnlyList<Element>> BaselineAsync(
        DiagramService.DiagramServiceClient client,
        Metadata headers,
        Common.Wire.ShortGuid projectId,
        string fileName)
    {
        using var cts = CreateMessageTimeout();

        var path = new Path();
        path.Segments.Add(fileName);
        using var call = client.Open(
            new OpenDiagramRequest { ProjectId = projectId, WatchId = ShortGuid.NewShortGuid(), Path = path },
            headers, cancellationToken: cts.Token);

        return await ReadAddAsync(call.ResponseStream, cts.Token);
    }

    /// <summary>The elements of the next add on a stream - a baseline, or the deltas of a change.</summary>
    private static async Task<IReadOnlyList<Element>> ReadAddAsync(
        IAsyncStreamReader<Delta> stream,
        CancellationToken cancellationToken)
    {
        try
        {
            while (await stream.MoveNext(cancellationToken))
            {
                if (stream.Current.ActionCase == Delta.ActionOneofCase.Add)
                {
                    return [.. stream.Current.Add.Elements];
                }
            }
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.Cancelled)
        {
            // Reading stopped at the deadline, which is how this helper ends when nothing came.
        }

        return [];
    }

    private static async Task<ContextPrompt> ReadUntilPromptAsync(
        IAsyncStreamReader<ContextMessage> stream,
        CancellationToken cancellationToken)
    {
        while (await stream.MoveNext(cancellationToken))
        {
            if (stream.Current.MessageCase == ContextMessage.MessageOneofCase.Prompt)
            {
                return stream.Current.Prompt;
            }
        }

        throw new InvalidOperationException("The stream ended before a prompt arrived.");
    }

    private static async Task<ContextSelectionChanged> ReadUntilSelectionWithActionsAsync(
        IAsyncStreamReader<ContextMessage> stream,
        CancellationToken cancellationToken)
    {
        while (await stream.MoveNext(cancellationToken))
        {
            if (stream.Current.MessageCase == ContextMessage.MessageOneofCase.Selection &&
                stream.Current.Selection.Selection is not null &&
                stream.Current.Selection.Actions.Count > 0)
            {
                return stream.Current.Selection;
            }
        }

        throw new InvalidOperationException("The stream ended before a selection with actions arrived.");
    }

    private static CancellationTokenSource CreateMessageTimeout()
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(MessageTimeout);
        return cts;
    }

    private GrpcChannel CreateChannel()
    {
        var httpClient = _factory.CreateDefaultClient();
        return GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions { HttpClient = httpClient });
    }

    private static async Task<Metadata> LoginAsync(GrpcChannel channel)
    {
        var authClient = new AuthenticationService.AuthenticationServiceClient(channel);
        var response = await authClient.LoginAsync(
            new LoginRequest { Username = DeveloperUsername, Credential = DeveloperCredential },
            cancellationToken: TestContext.Current.CancellationToken);
        return new Metadata { { SessionTokenHeader, response.Session.Value } };
    }

    private async Task<Common.Wire.ShortGuid> AddProjectAsync(GrpcChannel channel, Metadata headers)
    {
        var projectClient = new ProjectService.ProjectServiceClient(channel);
        var pathMessage = new Path();
        pathMessage.Segments.AddRange(_projectFolder.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));
        var response = await projectClient.AddProjectAsync(
            new AddProjectRequest { Path = pathMessage }, headers,
            cancellationToken: TestContext.Current.CancellationToken);
        return response.Added.Id;
    }
}
