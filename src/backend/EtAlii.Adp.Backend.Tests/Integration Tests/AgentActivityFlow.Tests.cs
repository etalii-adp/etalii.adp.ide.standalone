using EtAlii.Adp.Authentication.Wire;
using EtAlii.Adp.Context;
using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Diagram.AgentActivityDiagram;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Documents.Wire;
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
using ContextService = EtAlii.Adp.Context.Wire.ContextService;
using HierarchyService = EtAlii.Adp.Hierarchy.Wire.HierarchyService;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here
using Path = EtAlii.Adp.Documents.Wire.Path;
using ProjectService = EtAlii.Adp.Projects.Wire.ProjectService;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The agent activity diagram over the real host (agent-activity-diagram Requirement 8): what an
/// agent writes to the file reaches a canvas that has it open, a gesture on the canvas is written
/// to the file, and an undo never takes an agent's write with it.
/// </summary>
public class AgentActivityFlowTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string DeveloperUsername = "admin";
    private const string DeveloperCredential = "changeme";
    private const string SessionTokenHeader = "session-token";
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(20);

    private const string Work =
        "agent-activity-diagram: 1\r\n" +
        "projects:\r\n" +
        "  - id: p-adp\r\n" +
        "    name: ADP\r\n" +
        "specifications:\r\n" +
        "  - id: s-activity\r\n" +
        "    project: p-adp\r\n" +
        "    name: Agent activity diagram\r\n" +
        "    status: progressing\r\n" +
        "    tasks:\r\n" +
        "      - id: t-1\r\n" +
        "        title: The module's client\r\n" +
        "        status: progressing\r\n";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _appDataRoot;
    private readonly string _projectFolder;

    public AgentActivityFlowTests(WebApplicationFactory<Program> baseFactory)
    {
        _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.IntegrationTests", Guid.NewGuid().ToString("N"));
        _projectFolder = IoPath.Combine(_appDataRoot, "sample-project");
        Directory.CreateDirectory(_projectFolder);
        File.WriteAllText(IoPath.Combine(_projectFolder, "work.adp"), "etalii/agent-activity-diagram\r\nbody: work.aad\r\n");
        File.WriteAllText(Body, Work);

        _factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("developer");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IProjectStore>();
                services.AddSingleton<IProjectStore>(new FileProjectStore(_appDataRoot));
                // The problem cache lives and dies with this test, not in the real user profile.
                services.RemoveAll<Problems.IProblemStore>();
                services.AddSingleton<Problems.IProblemStore>(provider => new Problems.ProblemStore(
                    _appDataRoot,
                    provider.GetRequiredService<Hierarchy.DiagramFileRouter>(),
                    provider.GetRequiredService<Diagram.DiagramValidators>()));
            });
        });
    }

    private string Body => IoPath.Combine(_projectFolder, "work.aad");

    public void Dispose()
    {
        _factory.Dispose();
        TestFolder.TryDelete(_appDataRoot);
    }

    [Fact]
    public async Task WhatAnAgentWrites_ReachesTheCanvasThatHasTheFileOpen()
    {
        // Arrange: a canvas with the file open.
        using var channel = CreateChannel();
        var client = new DiagramService.DiagramServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        using var cts = CreateMessageTimeout();
        using var call = client.Open(new OpenDiagramRequest { ProjectId = projectId, WatchId = ShortGuid.NewShortGuid(), Path = WorkPath() }, headers, cancellationToken: cts.Token);
        var baseline = await ReadAddAsync(call.ResponseStream, cts.Token);
        Assert.Equal("progressing", Payload(baseline, "s-activity").Tasks.Single().Status);

        // Act: an agent finishes its task and says so in the file, as it would from any program.
        await File.WriteAllTextAsync(Body, Work.Replace("        status: progressing\r\n", "        status: finished\r\n", StringComparison.Ordinal), TestContext.Current.CancellationToken);

        // Assert: the canvas is sent the specification again, its task finished. Which other
        // deltas a file system's watcher causes on the way differs by platform - one build server
        // re-sent the project first - so the stream is read until the change itself arrives.
        string? status = null;
        while (status != "finished")
        {
            var update = await ReadAddAsync(call.ResponseStream, cts.Token);
            Assert.True(update.Count > 0, "The stream ended, or the deadline passed, before the agent's change arrived.");
            if (update.Any(element => element.Id.Value == "s-activity"))
            {
                status = Payload(update, "s-activity").Tasks.Single().Status;
            }
        }
    }

    [Fact]
    public async Task AGestureOnTheCanvas_IsWrittenToTheFile_AndUndoneOnlyWhileNoAgentWroteAfterIt()
    {
        // Arrange: a canvas with the file open and the file selected, as a canvas has it.
        using var channel = CreateChannel();
        var client = new DiagramService.DiagramServiceClient(channel);
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var contextClient = new ContextService.ContextServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();
        await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        var entryId = await NestedEntryLookup.EntryIdOfAsync(hierarchyClient, projectId, watchId, headers, "work.adp");
        using var cts = CreateMessageTimeout();
        using var contextCall = contextClient.Watch(new WatchContextRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: cts.Token);
        using var call = client.Open(new OpenDiagramRequest { ProjectId = projectId, WatchId = watchId, Path = WorkPath() }, headers, cancellationToken: cts.Token);
        await ReadAddAsync(call.ResponseStream, cts.Token);
        var selected = await contextClient.SelectAsync(new SelectRequest { ProjectId = projectId, WatchId = watchId, Selection = FileChain(entryId) }, headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("", selected.Error);

        // Act: a drag locks the specification where it was let go.
        var moved = await client.MoveElementAsync(
            new MoveElementRequest { ProjectId = projectId, WatchId = watchId, Path = WorkPath(), ElementId = "s-activity", Position = new Point2D { X = 240, Y = -80 } },
            headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the lock is the reader's part of the file, created with its first entry.
        Assert.Equal("", moved.Error);
        var locked = Work + "view:\r\n  placements:\r\n    - element: s-activity\r\n      x: 240\r\n      y: -80\r\n";
        Assert.Equal(locked, await File.ReadAllTextAsync(Body, TestContext.Current.CancellationToken));

        // Act: the archived switch, pressed on the canvas - the diagram's own target, not an element's.
        var switched = await contextClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Source = new ContextSource { ElementId = new ElementId { Value = AadElementMapper.ViewId } },
                InteractionId = ShortGuid.NewShortGuid(),
                ActionId = AadContextActionProvider.ShowArchivedActionId,
            },
            headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(switched.Accepted, switched.Error);
        var shown = locked.Replace("view:\r\n", "view:\r\n  showArchived: true\r\n", StringComparison.Ordinal);
        Assert.Equal(shown, await File.ReadAllTextAsync(Body, TestContext.Current.CancellationToken));

        // Undo takes the switch back, and the lock stays.
        var undone = await ExecuteProjectActionAsync(contextClient, projectId, watchId, headers, HistoryContextActionProvider.UndoActionId);
        Assert.True(undone.Accepted, undone.Error);
        Assert.Equal(locked, await File.ReadAllTextAsync(Body, TestContext.Current.CancellationToken));

        // Act: an agent writes the file; the edit before that can no longer be undone over it.
        var agents = locked.Replace("    name: ADP\r\n", "    name: ADP, renamed by an agent\r\n", StringComparison.Ordinal);
        await File.WriteAllTextAsync(Body, agents, TestContext.Current.CancellationToken);
        var refused = await ExecuteProjectActionAsync(contextClient, projectId, watchId, headers, HistoryContextActionProvider.UndoActionId);

        // Assert: nothing is written, and the agent's rename is still there (Requirement 8.7).
        Assert.False(refused.Accepted);
        Assert.Contains("changed by another program", refused.Error, StringComparison.Ordinal);
        Assert.Equal(agents, await File.ReadAllTextAsync(Body, TestContext.Current.CancellationToken));
    }

    private static Path WorkPath()
    {
        var path = new Path();
        path.Segments.Add("work.adp");
        return path;
    }

    private static AadElementPayload Payload(IReadOnlyList<Element> elements, string id) =>
        AadElementPayload.Parser.ParseFrom(elements.Single(element => element.Id.Value == id).Payload.Value.Span);

    private static ContextSelection FileChain(Documents.Wire.ShortGuid entryId)
    {
        var chain = new ContextSelection { Source = ContextSelectionSource.Explorer, Id = new ContextSource { EntryId = entryId }, Path = new Path() };
        chain.Path.Segments.Add("work.adp");
        return chain;
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
        var response = await authClient.LoginAsync(new LoginRequest { Username = DeveloperUsername, Credential = DeveloperCredential }, cancellationToken: TestContext.Current.CancellationToken);
        return new Metadata { { SessionTokenHeader, response.Session.Value } };
    }

    private async Task<Documents.Wire.ShortGuid> AddProjectAsync(GrpcChannel channel, Metadata headers)
    {
        var projectClient = new ProjectService.ProjectServiceClient(channel);
        var pathMessage = new Path();
        pathMessage.Segments.AddRange(_projectFolder.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));
        var response = await projectClient.AddProjectAsync(new AddProjectRequest { Path = pathMessage }, headers, cancellationToken: TestContext.Current.CancellationToken);
        return response.Added.Id;
    }

    private static Task<ExecuteActionResponse> ExecuteProjectActionAsync(
        ContextService.ContextServiceClient contextClient,
        Documents.Wire.ShortGuid projectId,
        Documents.Wire.ShortGuid watchId,
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

    /// <summary>The next delta that adds or replaces elements; an empty list when none came before the deadline.</summary>
    private static async Task<IReadOnlyList<Element>> ReadAddAsync(IAsyncStreamReader<Delta> stream, CancellationToken cancellationToken)
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
}
