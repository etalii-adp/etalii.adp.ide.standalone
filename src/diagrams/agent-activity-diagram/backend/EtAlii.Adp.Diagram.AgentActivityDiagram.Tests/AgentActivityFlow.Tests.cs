using EtAlii.Adp.Context;
using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy.Wire;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Xunit;
using ContextService = EtAlii.Adp.Context.Wire.ContextService;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here
using Path = EtAlii.Adp.Documents.Wire.Path;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram.Tests;

/// <summary>
/// The agent activity diagram over the real host (agent-activity-diagram Requirement 8): what an
/// agent writes to the file reaches a canvas that has it open, a gesture on the canvas is written
/// to the file, and an undo never takes an agent's write with it.
/// </summary>
public class AgentActivityFlowTests(HostFixture host) : IClassFixture<HostFixture>
{
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

    private readonly string _projectFolder = Seed(host.NewProjectFolder());

    private static string Seed(string projectFolder)
    {
        File.WriteAllText(IoPath.Combine(projectFolder, "work.adp"), "etalii/agent-activity-diagram\r\nbody: work.aad\r\n");
        File.WriteAllText(IoPath.Combine(projectFolder, "work.aad"), Work);
        return projectFolder;
    }

    private string Body => IoPath.Combine(_projectFolder, "work.aad");

    [Fact]
    public async Task WhatAnAgentWrites_ReachesTheCanvasThatHasTheFileOpen()
    {
        // Arrange: a canvas with the file open.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var client = new DiagramService.DiagramServiceClient(session.Channel);
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var call = client.Open(new OpenDiagramRequest { ProjectId = session.ProjectId, WatchId = ShortGuid.NewShortGuid(), Path = WorkPath() }, session.Headers, cancellationToken: cts.Token);
        var baseline = await call.ResponseStream.ReadAddAsync(cts.Token);
        Assert.Equal("progressing", Payload(baseline, "s-activity").Tasks.Single().Status);

        // Act: an agent finishes its task and says so in the file, as it would from any program.
        await File.WriteAllTextAsync(Body, Work.Replace("        status: progressing\r\n", "        status: finished\r\n", StringComparison.Ordinal), TestContext.Current.CancellationToken);

        // Assert: the canvas is sent the specification again, its task finished. Which other
        // deltas a file system's watcher causes on the way differs by platform - one build server
        // re-sent the project first - so the stream is read until the change itself arrives.
        string? status = null;
        while (status != "finished")
        {
            var update = await call.ResponseStream.ReadAddAsync(cts.Token);
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
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var client = new DiagramService.DiagramServiceClient(session.Channel);
        var hierarchyClient = session.Hierarchy;
        var contextClient = session.Context;
        var watchId = ShortGuid.NewShortGuid();
        await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = session.ProjectId, WatchId = watchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var entryId = await NestedEntryLookup.EntryIdOfAsync(hierarchyClient, session.ProjectId, watchId, session.Headers, "work.adp");
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var contextCall = contextClient.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = watchId }, session.Headers, cancellationToken: cts.Token);
        using var call = client.Open(new OpenDiagramRequest { ProjectId = session.ProjectId, WatchId = watchId, Path = WorkPath() }, session.Headers, cancellationToken: cts.Token);
        await call.ResponseStream.ReadAddAsync(cts.Token);
        var selected = await contextClient.SelectAsync(new SelectRequest { ProjectId = session.ProjectId, WatchId = watchId, Selection = FileChain(entryId) }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("", selected.Error);

        // Act: a drag locks the specification where it was let go.
        var moved = await client.MoveElementAsync(
            new MoveElementRequest { ProjectId = session.ProjectId, WatchId = watchId, Path = WorkPath(), ElementId = "s-activity", Position = new Point2D { X = 240, Y = -80 } },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the lock is the reader's part of the file, created with its first entry.
        Assert.Equal("", moved.Error);
        var locked = Work + "view:\r\n  placements:\r\n    - element: s-activity\r\n      x: 240\r\n      y: -80\r\n";
        Assert.Equal(locked, await File.ReadAllTextAsync(Body, TestContext.Current.CancellationToken));

        // Act: the archived switch, pressed on the canvas - the diagram's own target, not an element's.
        var switched = await contextClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = session.ProjectId,
                WatchId = watchId,
                Source = new ContextSource { ElementId = new ElementId { Value = AadElementMapper.ViewId } },
                InteractionId = ShortGuid.NewShortGuid(),
                ActionId = AadContextActionProvider.ShowArchivedActionId,
            },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(switched.Accepted, switched.Error);
        var shown = locked.Replace("view:\r\n", "view:\r\n  showArchived: true\r\n", StringComparison.Ordinal);
        Assert.Equal(shown, await File.ReadAllTextAsync(Body, TestContext.Current.CancellationToken));

        // Undo takes the switch back, and the lock stays.
        var undone = await ExecuteProjectActionAsync(contextClient, session.ProjectId, watchId, session.Headers, HistoryContextActionProvider.UndoActionId);
        Assert.True(undone.Accepted, undone.Error);
        Assert.Equal(locked, await File.ReadAllTextAsync(Body, TestContext.Current.CancellationToken));

        // Act: an agent writes the file; the edit before that can no longer be undone over it.
        var agents = locked.Replace("    name: ADP\r\n", "    name: ADP, renamed by an agent\r\n", StringComparison.Ordinal);
        await File.WriteAllTextAsync(Body, agents, TestContext.Current.CancellationToken);
        var refused = await ExecuteProjectActionAsync(contextClient, session.ProjectId, watchId, session.Headers, HistoryContextActionProvider.UndoActionId);

        // Assert: nothing is written, and the agent's rename is still there (Requirement 8.7).
        Assert.False(refused.Accepted);
        Assert.Contains("changed by another program", refused.Error, StringComparison.Ordinal);
        Assert.Equal(agents, await File.ReadAllTextAsync(Body, TestContext.Current.CancellationToken));
    }

    private static Path WorkPath()
    {
        var path = Path.Of("work.adp");
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
}
