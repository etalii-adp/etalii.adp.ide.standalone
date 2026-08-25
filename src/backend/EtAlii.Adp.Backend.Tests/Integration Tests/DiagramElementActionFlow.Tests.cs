using EtAlii.Adp.Backend.Projects;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The canvas's whole editing surface hangs on one call shape: a keystroke forwarded as an
/// <c>ExecuteAction</c> whose source is a bare element id (mindmap-diagram Requirement 8.4).
/// Found broken by the diagram-workspace-tabs manual pass - the target resolver answered
/// "an unrecognised source resolved to nothing" for every element source - so this proves the
/// shape over the real host: the element resolves within the connection's current selection.
/// </summary>
public class DiagramElementActionFlowTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string DeveloperUsername = "admin";
    private const string DeveloperCredential = "changeme";
    private const string SessionTokenHeader = "session-token";
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StreamStartupGrace = TimeSpan.FromMilliseconds(500);

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _appDataRoot;
    private readonly string _projectFolder;

    public DiagramElementActionFlowTests(WebApplicationFactory<Program> baseFactory)
    {
        _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.IntegrationTests", Guid.NewGuid().ToString("N"));
        _projectFolder = IoPath.Combine(_appDataRoot, "sample-project");
        Directory.CreateDirectory(_projectFolder);

        // A registered mindmap with one known root node - what the canvas would be showing.
        File.WriteAllText(IoPath.Combine(_projectFolder, "roadmap.adp"), "freeplane/mindmap\n");
        File.WriteAllText(
            IoPath.Combine(_projectFolder, "roadmap.mm"),
            "<map version=\"freeplane 1.11.5\">\n<node TEXT=\"roadmap\" ID=\"ID_1\"/>\n</map>\n");

        _factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("developer");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IProjectStore>();
                services.AddSingleton<IProjectStore>(new FileProjectStore(_appDataRoot));
                // The problem cache must live and die with this test, not in the real user
                // profile the host's AddProblems registration points at (found by the
                // errors-and-warnings-panel manual pass: every run left a cache file behind).
                services.RemoveAll<Problems.IProblemStore>();
                services.AddSingleton<Problems.IProblemStore>(provider => new Problems.ProblemStore(
                    _appDataRoot,
                    provider.GetRequiredService<Hierarchy.DiagramFileRouter>(),
                    provider.GetRequiredService<Diagram.DiagramValidators>()));
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        if (Directory.Exists(_appDataRoot))
        {
            Directory.Delete(_appDataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task AKeystrokeAgainstTheSelectedNode_ReachesItsActionAndPromptsForTheChild()
    {
        // Arrange.
        using var channel = CreateChannel();
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var contextClient = new ContextService.ContextServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();

        // Arrange, continued.
        var entries = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        var entryId = entries.Entries.Entries_.Single(e => e.Name == "roadmap.adp").Id;

        // Arrange, continued.
        using var cts = CreateMessageTimeout();
        using var contextCall = contextClient.Watch(new WatchContextRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        var pendingPrompt = ReadUntilPromptAsync(contextCall.ResponseStream, cts.Token);
        await Task.Delay(StreamStartupGrace, TestContext.Current.CancellationToken);

        // Arrange, continued.
        // The canvas's click: the nested file -> node selection.
        var selectResponse = await contextClient.SelectAsync(
            new SelectRequest { ProjectId = projectId, WatchId = watchId, Selection = NodeChain(entryId, "ID_1") },
            headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("", selectResponse.Error);

        // Act.
        // The canvas's keystroke: Insert against the focused node, source = the bare element id.
        var executed = await contextClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Source = new ContextSource { ElementId = new ElementId { Value = "ID_1" } },
                InteractionId = ShortGuid.NewShortGuid(),
                Shortcut = new ContextShortcut { Key = "Insert" },
            },
            headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(executed.Accepted, executed.Error);
        var prompt = await pendingPrompt;
        Assert.Equal(ContextPrompt.PromptOneofCase.InputDialog, prompt.PromptCase);
        Assert.Equal("Add child", prompt.InputDialog.Title);
    }

    [Fact]
    public async Task AKeystrokeAgainstAnotherNodeOfTheSelectedDiagram_ResolvesUnderTheSelectionsFile()
    {
        // Arrange.
        // Focus moved on the canvas and the keystroke beat the new selection's round trip:
        // only the file is selected, yet the element must still resolve - under that file.
        await File.WriteAllTextAsync(
            IoPath.Combine(_projectFolder, "wide.adp"), "freeplane/mindmap\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            IoPath.Combine(_projectFolder, "wide.mm"),
            "<map version=\"freeplane 1.11.5\">\n<node TEXT=\"wide\" ID=\"ID_root\">\n<node TEXT=\"child\" ID=\"ID_child\"/>\n</node>\n</map>\n",
            TestContext.Current.CancellationToken);

        // Arrange, continued.
        using var channel = CreateChannel();
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var contextClient = new ContextService.ContextServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();

        // Arrange, continued.
        var entries = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        var entryId = entries.Entries.Entries_.Single(e => e.Name == "wide.adp").Id;

        // Arrange, continued.
        using var cts = CreateMessageTimeout();
        using var contextCall = contextClient.Watch(new WatchContextRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        var pendingPrompt = ReadUntilPromptAsync(contextCall.ResponseStream, cts.Token);
        await Task.Delay(StreamStartupGrace, TestContext.Current.CancellationToken);

        // Arrange, continued.
        // Only the file is selected - no node level in the chain.
        await contextClient.SelectAsync(
            new SelectRequest { ProjectId = projectId, WatchId = watchId, Selection = FileChain(entryId) },
            headers, cancellationToken: TestContext.Current.CancellationToken);

        // Act.
        var executed = await contextClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Source = new ContextSource { ElementId = new ElementId { Value = "ID_child" } },
                InteractionId = ShortGuid.NewShortGuid(),
                Shortcut = new ContextShortcut { Key = "F2" },
            },
            headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(executed.Accepted, executed.Error);
        var prompt = await pendingPrompt;
        Assert.Equal(ContextPrompt.PromptOneofCase.InputDialog, prompt.PromptCase);
        Assert.Equal("child", prompt.InputDialog.InitialValue); // the rename prompt carries that node's text
    }

    // ---- helpers ----------------------------------------------------------------------

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

    private async Task<ShortGuid> AddProjectAsync(GrpcChannel channel, Metadata headers)
    {
        var projectClient = new ProjectService.ProjectServiceClient(channel);
        var pathMessage = new Path();
        pathMessage.Segments.AddRange(_projectFolder.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));
        var response = await projectClient.AddProjectAsync(new AddProjectRequest { Path = pathMessage }, headers, cancellationToken: TestContext.Current.CancellationToken);
        return response.Added.Id;
    }

    /// <summary>The canvas's nested selection: the .adp file, then the node as its child.</summary>
    private static ContextSelection NodeChain(ShortGuid entryId, string nodeId, string[]? filePath = null, string[]? nodePath = null)
    {
        var chain = FileChain(entryId);
        chain.Path.Segments.AddRange(filePath ?? []);
        chain.Child = new ContextSelection
        {
            Source = ContextSelectionSource.DiagramCanvas,
            Id = new ContextSource { ElementId = new ElementId { Value = nodeId } },
            Path = new Path(),
        };
        chain.Child.Path.Segments.AddRange(nodePath ?? []);
        return chain;
    }

    [Fact]
    public async Task TheCanvassOwnSelectionShape_OfANonRootNode_IsAccepted()
    {
        // Arrange.
        // What MindmapCanvas actually sends for a click on a non-root node: the file with its
        // project-relative path, the node as a DIAGRAM_CANVAS child. Found rejected by the
        // manual pass - every earlier test had clicked only the root, whose one-segment path
        // happened to satisfy the resolver's full text-chain expectation.
        await File.WriteAllTextAsync(
            IoPath.Combine(_projectFolder, "deep.adp"), "freeplane/mindmap\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            IoPath.Combine(_projectFolder, "deep.mm"),
            "<map version=\"freeplane 1.11.5\">\n<node TEXT=\"deep\" ID=\"ID_d0\">\n<node TEXT=\"Alpha\" ID=\"ID_d1\"/>\n</node>\n</map>\n",
            TestContext.Current.CancellationToken);

        using var channel = CreateChannel();
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var contextClient = new ContextService.ContextServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();

        var entries = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        var entryId = entries.Entries.Entries_.Single(e => e.Name == "deep.adp").Id;

        // Act and assert, step by step.
        // The canvas's shape: the file with its project-relative path, the node with an empty
        // path that asks the backend to fill in the full text chain.
        var canvasShape = await contextClient.SelectAsync(
            new SelectRequest { ProjectId = projectId, WatchId = watchId, Selection = NodeChain(entryId, "ID_d1", filePath: ["deep.adp"]) },
            headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("", canvasShape.Error);

        // A partial node path - the node's own text without its ancestors - is not the node's
        // path and stays refused: the resolver checks the chain, it does not guess at it.
        var partialPath = await contextClient.SelectAsync(
            new SelectRequest { ProjectId = projectId, WatchId = watchId, Selection = NodeChain(entryId, "ID_d1", filePath: ["deep.adp"], nodePath: ["Alpha"]) },
            headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEqual("", partialPath.Error);
    }

    [Fact]
    public async Task ACompletedAction_RepushesTheSelectionsActions_SoACollapseOffersExpand()
    {
        // Arrange.
        // Found by the bezier-connector manual pass: after Collapse ran from the node's
        // context menu, re-opening the menu still offered Collapse. The completed action
        // changed what applies to the very same selection, but nobody re-derived its actions.
        await File.WriteAllTextAsync(
            IoPath.Combine(_projectFolder, "fold.adp"), "freeplane/mindmap\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            IoPath.Combine(_projectFolder, "fold.mm"),
            "<map version=\"freeplane 1.11.5\">\n<node TEXT=\"fold\" ID=\"ID_f0\">\n<node TEXT=\"branch\" ID=\"ID_f1\">\n<node TEXT=\"leaf\" ID=\"ID_f2\"/>\n</node>\n</node>\n</map>\n",
            TestContext.Current.CancellationToken);

        // Arrange, continued.
        using var channel = CreateChannel();
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var contextClient = new ContextService.ContextServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();

        // Arrange, continued.
        var entries = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        var entryId = entries.Entries.Entries_.Single(e => e.Name == "fold.adp").Id;

        // Arrange, continued.
        using var cts = CreateMessageTimeout();
        using var contextCall = contextClient.Watch(new WatchContextRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        await Task.Delay(StreamStartupGrace, TestContext.Current.CancellationToken);

        // Arrange, continued.
        await contextClient.SelectAsync(
            new SelectRequest { ProjectId = projectId, WatchId = watchId, Selection = NodeChain(entryId, "ID_f1", filePath: ["fold.adp"]) },
            headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("Collapse", ActionLabels(await ReadUntilSelectionActionsAsync(contextCall.ResponseStream, cts.Token)));

        // Act.
        var executed = await contextClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Source = new ContextSource { ElementId = new ElementId { Value = "ID_f1" } },
                InteractionId = ShortGuid.NewShortGuid(),
                ActionId = "mindmap.toggle-fold",
            },
            headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(executed.Accepted, executed.Error);

        // Assert.
        Assert.Contains("Expand", ActionLabels(await ReadUntilSelectionActionsAsync(contextCall.ResponseStream, cts.Token)));
    }

    private static ContextSelection FileChain(ShortGuid entryId) => new()
    {
        Source = ContextSelectionSource.Explorer,
        Id = new ContextSource { EntryId = entryId },
        Path = new Path(),
    };

    private static string[] ActionLabels(ContextSelectionChanged changed) =>
        changed.Actions.SelectMany(group => group.Actions).Select(action => action.Label).ToArray();

    private static async Task<ContextSelectionChanged> ReadUntilSelectionActionsAsync(IAsyncStreamReader<ContextMessage> stream, CancellationToken cancellationToken)
    {
        while (await stream.MoveNext(cancellationToken))
        {
            if (stream.Current.MessageCase == ContextMessage.MessageOneofCase.Selection
                && stream.Current.Selection.Selection is not null
                && stream.Current.Selection.Actions.Count > 0)
            {
                return stream.Current.Selection;
            }
        }

        throw new InvalidOperationException("The stream ended before a selection with actions arrived.");
    }

    private static async Task<ContextPrompt> ReadUntilPromptAsync(IAsyncStreamReader<ContextMessage> stream, CancellationToken cancellationToken)
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
}
