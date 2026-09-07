using EtAlii.Adp.Authentication.Wire;
using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Diagram.Timeline;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Hierarchy.Wire;
using EtAlii.Adp.History;
using EtAlii.Adp.Projects;
using EtAlii.Adp.Projects.Wire;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here
using Path = EtAlii.Adp.Common.Wire.Path;
using ProjectService = EtAlii.Adp.Projects.Wire.ProjectService;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The timeline arc over the real host: a bare <c>.tml</c> that routes on sight, opens, streams
/// its elements and connections, and edits like any other type - with every module registered,
/// so the seams are exercised in company rather than alone.
/// </summary>
/// <remarks>
/// The halves worth proving here rather than in the module's own tests: that a distinctive
/// extension routes with no registration step (Requirement 1.2), that a timeline element is
/// selectable while every other module's resolver is also registered, and that a drag and an
/// undo travel the whole way - canvas coordinates in, spliced lines out, byte identity back.
/// </remarks>
// Known flake, not a defect in this test's subject: a watcher-versus-write race under
// preemption spikes. Observed once under parallel-gate load (the client and backend gate halves
// running at once) - a race between the file watch firing and a write flushing. Signature: passes
// 3/3 in isolation, the full suite re-ran 4907/0, and it fails only under that concurrent load.
//
// The mechanism is sparse preemption spikes, not load level: dragCost's fix (develop 38e1ffb2)
// probed 0/4/8/16 CPU burners and found timing flat under steady load - the failure is one
// unlucky window at the wrong moment, which is exactly what a watcher-versus-write race needs, a
// slow machine being neither necessary nor sufficient. See that closed case for the mechanism
// rather than re-deriving it here.
//
// Task 5 of backend-project-decomposition relocated AdpFileWriter and SharedDocumentReader into
// EtAlii.Adp.Common, and TimelineDocumentStore uses both - so this test sits in that landing's
// blast radius, and a reader checking the history will find the move. The move is timing-neutral:
// which assembly a type compiles into cannot change when a FileSystemWatcher fires or when a write
// flushes, so the relocation is not the cause. Do not re-suspect it on that basis.
//
// Ownership is unknown - not backend-consistency AC2 (that requirement is about fire-and-forget
// tasks faulting unobserved and says nothing about watchers). Left un-skipped on purpose: it
// passes on re-run, and quarantining a test that mostly works removes coverage to silence a
// symptom. It becomes a real defect to chase - not contention - the moment it fails
// deterministically or survives isolation; until then a red run here is a re-run, not an afternoon.
public class TimelineFlowTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string DeveloperUsername = "admin";
    private const string DeveloperCredential = "changeme";
    private const string SessionTokenHeader = "session-token";
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(20);

    private const string Plan = """
        timeline: 1
        # A comment no edit may disturb.
        elements:
          - id: discovery
            label: Discovery
            begin: 2026-01-05
            end: 2026-02-13
            row: 0
          - id: go
            label: Go
            begin: 2026-02-16
            row: 1
        connections:
          - id: gate
            from: discovery
            to: go
            label: gates
        """;

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _appDataRoot;
    private readonly string _projectFolder;

    public TimelineFlowTests(WebApplicationFactory<Program> baseFactory)
    {
        _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.IntegrationTests", Guid.NewGuid().ToString("N"));
        _projectFolder = IoPath.Combine(_appDataRoot, "sample-project");
        Directory.CreateDirectory(_projectFolder);

        // A bare timeline - no .adp anywhere near it - and a file no type claims.
        File.WriteAllText(IoPath.Combine(_projectFolder, "plan.tml"), Plan);
        File.WriteAllText(IoPath.Combine(_projectFolder, "notes.txt"), "not a diagram\n");

        _factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("developer");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IProjectStore>();
                services.AddSingleton<IProjectStore>(new FileProjectStore(_appDataRoot));
                services.RemoveAll<Problems.IProblemStore>();
                services.AddSingleton<Problems.IProblemStore>(provider => new Problems.ProblemStore(
                    _appDataRoot,
                    provider.GetRequiredService<Hierarchy.DiagramFileRouter>(),
                    provider.GetRequiredService<Common.DiagramValidators>()));
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        TestFolder.TryDelete(_appDataRoot);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ABareTml_RoutesOnSight_AndAnswersWithTheTimelinePalette()
    {
        // Arrange: Requirement 1.2 - .tml is this type's own extension, owned outright, so a
        // bare body routes with no registration step and no Add-on-a-file path.
        using var channel = CreateChannel();
        var diagramClient = new DiagramService.DiagramServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);

        // Act.
        var timeline = await diagramClient.DescribeToolboxAsync(
            new DescribeToolboxRequest { ProjectId = projectId, Path = PathOf("plan.tml") },
            headers, cancellationToken: TestContext.Current.CancellationToken);
        var plain = await diagramClient.DescribeToolboxAsync(
            new DescribeToolboxRequest { ProjectId = projectId, Path = PathOf("notes.txt") },
            headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(["Element", "Moment"], timeline.Items.Select(item => item.Label));
        Assert.Empty(plain.Items);
    }

    [Fact]
    public async Task OpeningATimeline_StreamsItsElementsAndConnections()
    {
        // Arrange.
        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);

        // Act.
        var elements = await BaselineAsync(channel, headers, projectId, "plan.tml");

        // Assert.
        Assert.Contains(elements, element => element.Id.Value == "discovery" && element.Type == "generic/timeline+period");
        Assert.Contains(elements, element => element.Id.Value == "go" && element.Type == "generic/timeline+moment");
        Assert.Contains(elements, element => element.Id.Value == "gate" && element.Type == "generic/timeline+connection");
    }

    [Fact]
    public async Task ADrag_RewritesOnlyTheLinesItTouches_AndIsOneUndoAway()
    {
        // Arrange: the drag carries the module's own coordinates - seconds and row-height units -
        // and the diagram must be open on the SAME connection, because core will not edit a
        // document a caller is not looking at.
        using var channel = CreateChannel();
        var client = new DiagramService.DiagramServiceClient(channel);
        var contextClient = new ContextService.ContextServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();
        var bodyPath = IoPath.Combine(_projectFolder, "plan.tml");

        using var cts = CreateMessageTimeout();
        using var call = client.Open(
            new OpenDiagramRequest { ProjectId = projectId, WatchId = watchId, Path = PathOf("plan.tml") },
            headers, cancellationToken: cts.Token);
        await ReadAddAsync(call.ResponseStream, cts.Token);

        // Act: land the period at 2026-03-01 on row 4. 39 days of duration must survive.
        var landing = (new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero) - DateTimeOffset.UnixEpoch).TotalSeconds;
        var moved = await client.MoveElementAsync(
            new MoveElementRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Path = PathOf("plan.tml"),
                ElementId = "discovery",
                Position = new Point2D { X = landing, Y = 4 * 60d },
            },
            headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("", moved.Error);
        var text = await File.ReadAllTextAsync(bodyPath, TestContext.Current.CancellationToken);
        Assert.Contains("begin: 2026-03-01", text, StringComparison.Ordinal);
        Assert.Contains("end: 2026-04-09", text, StringComparison.Ordinal);
        Assert.Contains("row: 4", text, StringComparison.Ordinal);
        Assert.Contains("# A comment no edit may disturb.", text, StringComparison.Ordinal);

        // Act, continued: one undo restores the file byte for byte - the promise the whole
        // module is built around, checked over the real host.
        var undone = await ExecuteProjectActionAsync(contextClient, projectId, watchId, headers, HistoryContextActionProvider.UndoActionId);

        // Assert.
        Assert.True(undone.Accepted, undone.Error);
        Assert.Equal(Plan, await File.ReadAllTextAsync(bodyPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AGridEditThatWouldInvert_IsRefused_AndTheFileUntouched()
    {
        // Arrange: the second of the two paths Requirement 3.4 closes, walked over the real
        // selection chain - which also proves a timeline element resolves while every other
        // module's element resolver is registered beside this one.
        using var channel = CreateChannel();
        var contextClient = new ContextService.ContextServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();
        var entries = await EntriesAsync(channel, headers, projectId, watchId);

        var selected = await contextClient.SelectAsync(
            new SelectRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Selection = ElementChain(entries["plan.tml"], "discovery"),
            },
            headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(selected.Error);

        // Act: a begin later than the end.
        var result = await contextClient.SetPropertyAsync(
            new SetPropertyRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                PropertyId = TimelineContextPropertyProvider.BeginProperty,
                Value = "2026-06-01",
            },
            headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.Accepted);
        Assert.Contains("cannot end before it begins", result.Error, StringComparison.Ordinal);
        Assert.Equal(Plan, await File.ReadAllTextAsync(
            IoPath.Combine(_projectFolder, "plan.tml"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemovingAConnectedElement_AsksWithTheCount_AndTheWholeRemovalIsOneUndo()
    {
        // Arrange: Requirement 2.5 over the real interaction flow - the confirmation carries the
        // connection count before anything runs, and the confirmed removal takes element and
        // connection together as one history entry.
        using var channel = CreateChannel();
        var contextClient = new ContextService.ContextServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();
        var entries = await EntriesAsync(channel, headers, projectId, watchId);
        var bodyPath = IoPath.Combine(_projectFolder, "plan.tml");

        // The confirmation is PUSHED, not returned: without a context watch open on this
        // connection there is nowhere to put the dialog and the execute is refused.
        using var cts = CreateMessageTimeout();
        using var watch = contextClient.Watch(
            new WatchContextRequest { ProjectId = projectId, WatchId = watchId },
            headers, cancellationToken: cts.Token);

        // Wait until the server has actually registered the watch, rather than assume that
        // starting the call did it. Watch() returns as soon as the request is on the wire; the
        // handler registers some time later and only then begins writing. An ExecuteAction sent
        // immediately can arrive first, find nothing listening, and be refused with "This
        // connection is no longer watching the project" - which is what this test did on three
        // runs in four when its class ran alone, and intermittently otherwise.
        //
        // The first message is the handshake and needs no timeout of its own: the handler writes
        // the baseline as it registers and only starts pumping afterwards, so a message in the
        // client's hands is proof the registration happened.
        Assert.True(
            await watch.ResponseStream.MoveNext(cts.Token),
            "the context watch closed before it sent its baseline");

        var selected = await contextClient.SelectAsync(
            new SelectRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Selection = ElementChain(entries["plan.tml"], "discovery"),
            },
            headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(selected.Error);

        // Act: execute opens the confirmation; submit is the user's "Remove".
        var interactionId = ShortGuid.NewShortGuid();
        var executed = await contextClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                InteractionId = interactionId,
                ActionId = TimelineContextActionProvider.RemoveActionId,
            },
            headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(executed.Accepted, executed.Error);

        // Assert, interleaved: the pushed confirmation names the count before anything runs.
        var confirmation = await ReadConfirmationAsync(watch.ResponseStream, cts.Token);
        Assert.NotNull(confirmation);
        Assert.Contains("1 relation", confirmation.Message, StringComparison.Ordinal);
        Assert.True(confirmation.Danger);

        var submitted = await contextClient.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = interactionId },
            headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(submitted.Completed, submitted.Error);
        var after = await File.ReadAllTextAsync(bodyPath, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("discovery", after, StringComparison.Ordinal);
        Assert.DoesNotContain("gate", after, StringComparison.Ordinal);

        // Act, continued: one undo brings the element AND its connection back, byte for byte.
        var undone = await ExecuteProjectActionAsync(contextClient, projectId, watchId, headers, HistoryContextActionProvider.UndoActionId);

        // Assert.
        Assert.True(undone.Accepted, undone.Error);
        Assert.Equal(Plan, await File.ReadAllTextAsync(bodyPath, TestContext.Current.CancellationToken));
    }

    private static async Task<IReadOnlyList<Element>> BaselineAsync(
        GrpcChannel channel,
        Metadata headers,
        ShortGuid projectId,
        string fileName)
    {
        var diagramClient = new DiagramService.DiagramServiceClient(channel);
        using var cts = CreateMessageTimeout();
        using var call = diagramClient.Open(
            new OpenDiagramRequest { ProjectId = projectId, WatchId = ShortGuid.NewShortGuid(), Path = PathOf(fileName) },
            headers, cancellationToken: cts.Token);
        return await ReadAddAsync(call.ResponseStream, cts.Token);
    }

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
            // Reading stopped at the deadline; the caller's assertion reports the empty baseline.
        }
        catch (OperationCanceledException)
        {
            // Same story, thrown the other way.
        }

        return [];
    }

    /// <summary>The next confirmation dialog pushed on a context watch, or null at the deadline.</summary>
    private static async Task<ConfirmDialogPrompt?> ReadConfirmationAsync(
        IAsyncStreamReader<ContextMessage> stream,
        CancellationToken cancellationToken)
    {
        try
        {
            while (await stream.MoveNext(cancellationToken))
            {
                if (stream.Current.MessageCase == ContextMessage.MessageOneofCase.Prompt &&
                    stream.Current.Prompt.PromptCase == ContextPrompt.PromptOneofCase.ConfirmDialog)
                {
                    return stream.Current.Prompt.ConfirmDialog;
                }
            }
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.Cancelled)
        {
            // Deadline; the caller's assertion reports the missing prompt.
        }

        return null;
    }

    private static async Task<IReadOnlyDictionary<string, Common.Wire.ShortGuid>> EntriesAsync(
        GrpcChannel channel,
        Metadata headers,
        ShortGuid projectId,
        ShortGuid watchId)
    {
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var response = await hierarchyClient.ListEntriesAsync(
            new ListEntriesRequest { ProjectId = projectId, WatchId = watchId },
            headers, cancellationToken: TestContext.Current.CancellationToken);

        return response.Entries.Entries_.ToDictionary(entry => entry.Name, entry => entry.Id, StringComparer.Ordinal);
    }

    /// <summary>The canvas's own selection shape: the body file, then the element as its child.</summary>
    private static ContextSelection ElementChain(Common.Wire.ShortGuid entryId, string elementId) =>
        new()
        {
            Source = ContextSelectionSource.Explorer,
            Id = new ContextSource { EntryId = entryId },
            Path = new Path(),
            Child = new ContextSelection
            {
                Source = ContextSelectionSource.DiagramCanvas,
                Id = new ContextSource { ElementId = new ElementId { Value = elementId } },
                Path = new Path(),
            },
        };

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
                Source = new ContextSource { Project = new Google.Protobuf.WellKnownTypes.Empty() },
                InteractionId = ShortGuid.NewShortGuid(),
                ActionId = actionId,
            },
            headers,
            cancellationToken: TestContext.Current.CancellationToken).ResponseAsync;

    private static Path PathOf(string fileName)
    {
        var path = new Path();
        path.Segments.Add(fileName);
        return path;
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

    private async Task<ShortGuid> AddProjectAsync(GrpcChannel channel, Metadata headers)
    {
        var projectClient = new ProjectService.ProjectServiceClient(channel);
        var pathMessage = new Path();
        pathMessage.Segments.AddRange(_projectFolder.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));
        var response = await projectClient.AddProjectAsync(
            new AddProjectRequest { Path = pathMessage },
            headers,
            cancellationToken: TestContext.Current.CancellationToken);
        return response.Added.Id;
    }
}
