using EtAlii.Adp.Backend.Hierarchy;
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
/// The whole Add arc against the real host: the root's actions on the baseline, the choice
/// prompt pushed down the connection's own stream for the root and for a folder, the
/// "not supported yet" answer on submit with the folder untouched, no Add for a file, and
/// the folder vanishing between the prompt and the submit.
/// </summary>
public class AddDiagramFlowTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string DeveloperUsername = "admin";
    private const string DeveloperCredential = "changeme";
    private const string SessionTokenHeader = "session-token";
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StreamStartupGrace = TimeSpan.FromMilliseconds(500);

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _appDataRoot;
    private readonly string _projectFolder;

    public AddDiagramFlowTests(WebApplicationFactory<Program> baseFactory)
    {
        _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.IntegrationTests", Guid.NewGuid().ToString("N"));
        _projectFolder = IoPath.Combine(_appDataRoot, "sample-project");
        Directory.CreateDirectory(_projectFolder);

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
                    provider.GetRequiredService<EtAlii.Adp.Diagram.DiagramValidators>()));
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

    /// <summary>
    /// The per-message timeout, linked to the test's own cancellation token so a stream that
    /// never delivers gives up as soon as the test is cancelled rather than waiting it out.
    /// </summary>
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

    private static async Task<ContextSelectionChanged> ReadBaselineAsync(IAsyncStreamReader<ContextMessage> stream, CancellationToken cancellationToken)
    {
        Assert.True(await stream.MoveNext(cancellationToken), "the stream ended before the baseline arrived");
        Assert.Equal(ContextMessage.MessageOneofCase.Selection, stream.Current.MessageCase);
        return stream.Current.Selection;
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

    //private string[] ProjectListing() => Directory.GetFileSystemEntries(_projectFolder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();

    /// <summary>The first selectable leaf in the tree, depth first - a real diagram type to choose.</summary>
    private static ContextOption FirstLeaf(IEnumerable<ContextOption> options)
    {
        foreach (var option in options)
        {
            if (option.Selectable)
            {
                return option;
            }

            if (option.Children.Count > 0)
            {
                return FirstLeaf(option.Children);
            }
        }

        throw new InvalidOperationException("No selectable option in the tree.");
    }

    [Fact]
    public async Task Baseline_WithNothingSelected_CarriesAddForTheRoot_EnabledBesideGreyedRenameAndDelete()
    {
        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var contextClient = new ContextService.ContextServiceClient(channel);
        var watchId = ShortGuid.NewShortGuid();

        using var cts = CreateMessageTimeout();
        using var contextCall = contextClient.Watch(new WatchContextRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        var baseline = await ReadBaselineAsync(contextCall.ResponseStream, cts.Token);

        Assert.Null(baseline.Selection);
        var actions = baseline.Actions.SelectMany(group => group.Actions).ToList();
        var add = Assert.Single(actions, action => action.Id == AddDiagramContextActionProvider.AddActionId);
        Assert.True(add.Available);
        Assert.Equal("Insert", add.Shortcut.Key);
        var validate = Assert.Single(actions, action => action.Id == Problems.ValidateContextActionProvider.ValidateActionId);
        Assert.True(validate.Available); // Validating the root is validating the whole project - always sensible.
        // The root is a folder, but the one folder the project must not rename or delete.
        Assert.All(
            actions.Where(action => action.Id is not AddDiagramContextActionProvider.AddActionId
                and not Problems.ValidateContextActionProvider.ValidateActionId),
            action =>
            {
                Assert.False(action.Available);
                Assert.NotEqual("", action.UnavailableReason);
            });
    }

    [Fact]
    public async Task Add_OnTheRoot_OpensTheChoiceDialog_AndSubmittingCreatesTheDiagram()
    {
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "existing.txt"), "x", TestContext.Current.CancellationToken);

        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var contextClient = new ContextService.ContextServiceClient(channel);
        var watchId = ShortGuid.NewShortGuid();

        using var cts = CreateMessageTimeout();
        using var contextCall = contextClient.Watch(new WatchContextRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        await ReadBaselineAsync(contextCall.ResponseStream, cts.Token);
        var pendingPrompt = ReadUntilPromptAsync(contextCall.ResponseStream, cts.Token);
        await Task.Delay(StreamStartupGrace, TestContext.Current.CancellationToken);

        // No source: the explorer's empty space.
        var interactionId = ShortGuid.NewShortGuid();
        var executed = await contextClient.ExecuteActionAsync(
            new ExecuteActionRequest { ProjectId = projectId, WatchId = watchId, InteractionId = interactionId, ActionId = AddDiagramContextActionProvider.AddActionId },
            headers,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(executed.Accepted, executed.Error);

        var prompt = await pendingPrompt;
        Assert.Equal(ContextPrompt.PromptOneofCase.ChoiceDialog, prompt.PromptCase);
        Assert.Equal("Add diagram", prompt.ChoiceDialog.Title);
        Assert.Equal("Add", prompt.ChoiceDialog.ConfirmLabel);
        Assert.NotEmpty(prompt.ChoiceDialog.Options);
        // Top level is vendors, not selectable; something selectable lies beneath.
        Assert.All(prompt.ChoiceDialog.Options, vendor => Assert.False(vendor.Selectable));
        var leaf = FirstLeaf(prompt.ChoiceDialog.Options);

        // The dialog also carries a name field, and each selectable option a suggestion for it.
        Assert.Equal("Name", prompt.ChoiceDialog.NameField.Label);
        Assert.NotEqual("", leaf.SuggestedValue);

        var submitted = await contextClient.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = interactionId, Value = leaf.Id, Text = leaf.SuggestedValue },
            headers,
            cancellationToken: TestContext.Current.CancellationToken);

        // create-diagram-file replaced this spec's "not supported yet" answer with the real
        // thing: a file on disk, and its project-relative path reported back.
        Assert.True(submitted.Completed, submitted.Error);
        Assert.Equal(new[] { $"{leaf.SuggestedValue}.adp" }, submitted.CreatedPath.Segments);
        Assert.True(File.Exists(IoPath.Combine(_projectFolder, $"{leaf.SuggestedValue}.adp")));
    }

    [Fact]
    public async Task Add_OnAFolder_ViaItsEntryId_OpensTheChoiceDialog()
    {
        Directory.CreateDirectory(IoPath.Combine(_projectFolder, "docs"));

        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var contextClient = new ContextService.ContextServiceClient(channel);
        var watchId = ShortGuid.NewShortGuid();

        var entries = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        var folderId = entries.Entries.Entries_.Single(e => e.Name == "docs").Id;

        using var cts = CreateMessageTimeout();
        using var contextCall = contextClient.Watch(new WatchContextRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        await ReadBaselineAsync(contextCall.ResponseStream, cts.Token);
        var pendingPrompt = ReadUntilPromptAsync(contextCall.ResponseStream, cts.Token);
        await Task.Delay(StreamStartupGrace, TestContext.Current.CancellationToken);

        var discovered = await contextClient.DiscoverActionsAsync(
            new DiscoverActionsRequest { ProjectId = projectId, WatchId = watchId, Source = new ContextSource { EntryId = folderId } },
            headers,
            cancellationToken: TestContext.Current.CancellationToken);
        var add = Assert.Single(discovered.Groups.SelectMany(g => g.Actions), a => a.Id == AddDiagramContextActionProvider.AddActionId);
        Assert.True(add.Available);

        var executed = await contextClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                InteractionId = ShortGuid.NewShortGuid(),
                Source = new ContextSource { EntryId = folderId },
                ActionId = AddDiagramContextActionProvider.AddActionId,
            },
            headers,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(executed.Accepted, executed.Error);

        var prompt = await pendingPrompt;
        Assert.Equal(ContextPrompt.PromptOneofCase.ChoiceDialog, prompt.PromptCase);
    }

    [Fact]
    public async Task DiscoverActions_OnAFile_OffersNoAdd()
    {
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "a.txt"), "x", TestContext.Current.CancellationToken);

        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var contextClient = new ContextService.ContextServiceClient(channel);
        var watchId = ShortGuid.NewShortGuid();

        var entries = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        var fileId = entries.Entries.Entries_.Single(e => e.Name == "a.txt").Id;

        var discovered = await contextClient.DiscoverActionsAsync(
            new DiscoverActionsRequest { ProjectId = projectId, WatchId = watchId, Source = new ContextSource { EntryId = fileId } },
            headers,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain(discovered.Groups.SelectMany(g => g.Actions), a => a.Id == AddDiagramContextActionProvider.AddActionId);
        // ...while the file's own actions are still there.
        Assert.Contains(discovered.Groups.SelectMany(g => g.Actions), a => a.Id == HierarchyContextActionProvider.RenameActionId);
    }

    [Fact]
    public async Task Submit_AfterTheFolderVanished_ReportsTheFolder()
    {
        var folder = IoPath.Combine(_projectFolder, "doomed");
        Directory.CreateDirectory(folder);

        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var contextClient = new ContextService.ContextServiceClient(channel);
        var watchId = ShortGuid.NewShortGuid();

        var entries = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        var folderId = entries.Entries.Entries_.Single(e => e.Name == "doomed").Id;

        using var cts = CreateMessageTimeout();
        using var contextCall = contextClient.Watch(new WatchContextRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        await ReadBaselineAsync(contextCall.ResponseStream, cts.Token);
        var pendingPrompt = ReadUntilPromptAsync(contextCall.ResponseStream, cts.Token);
        await Task.Delay(StreamStartupGrace, TestContext.Current.CancellationToken);

        var interactionId = ShortGuid.NewShortGuid();
        var executed = await contextClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                InteractionId = interactionId,
                Source = new ContextSource { EntryId = folderId },
                ActionId = AddDiagramContextActionProvider.AddActionId,
            },
            headers,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(executed.Accepted, executed.Error);
        var prompt = await pendingPrompt;
        var leaf = FirstLeaf(prompt.ChoiceDialog.Options);

        // The dialog is open; the folder goes away underneath it.
        Directory.Delete(folder, recursive: true);

        var submitted = await contextClient.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = interactionId, Value = leaf.Id },
            headers,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(submitted.Completed);
        Assert.Equal("The folder no longer exists.", submitted.Error);
    }
}
