using EtAlii.Adp.Authentication.Wire;
using EtAlii.Adp.Context;
using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.Projects;
using EtAlii.Adp.Projects.Wire;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using ContextService = EtAlii.Adp.Context.Wire.ContextService;
using IoPath = System.IO.Path;
using Path = EtAlii.Adp.Documents.Wire.Path;
using ProjectService = EtAlii.Adp.Projects.Wire.ProjectService;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The file dialog against the real host (knowledge-designer task 11, Requirements 10.6 and
/// 5.1): a provider asks for a file with a predicate; the prompt carries the workspace's files
/// that the predicate accepts and no other, with the pinned entries first; the chosen path
/// reaches the provider; a path that was not offered never does; and cancelling changes nothing.
/// </summary>
public sealed class FilePromptFlowTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string DeveloperUsername = "admin";
    private const string DeveloperCredential = "changeme";
    private const string SessionTokenHeader = "session-token";
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StreamStartupGrace = TimeSpan.FromMilliseconds(500);

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _appDataRoot;
    private readonly string _projectFolder;
    private readonly PickFileProvider _provider;

    public FilePromptFlowTests(WebApplicationFactory<Program> baseFactory)
    {
        _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.IntegrationTests", Guid.NewGuid().ToString("N"));
        _projectFolder = IoPath.Combine(_appDataRoot, "sample-project");
        Directory.CreateDirectory(IoPath.Combine(_projectFolder, "data", "deep"));
        Directory.CreateDirectory(IoPath.Combine(_projectFolder, "docs"));
        Directory.CreateDirectory(IoPath.Combine(_projectFolder, ".hidden"));
        File.WriteAllText(IoPath.Combine(_projectFolder, "cities.yaml"), "x");
        File.WriteAllText(IoPath.Combine(_projectFolder, "notes.txt"), "x");
        File.WriteAllText(IoPath.Combine(_projectFolder, "data", "countries.yaml"), "x");
        File.WriteAllText(IoPath.Combine(_projectFolder, "data", "readme.md"), "x");
        File.WriteAllText(IoPath.Combine(_projectFolder, "data", "deep", "rivers.yaml"), "x");
        File.WriteAllText(IoPath.Combine(_projectFolder, "docs", "guide.md"), "x");
        File.WriteAllText(IoPath.Combine(_projectFolder, ".hidden", "secret.yaml"), "x");
        _provider = new PickFileProvider(IoPath.Combine(_projectFolder, "notes.txt"));

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
                    provider.GetRequiredService<DiagramFileRouter>(),
                    provider.GetRequiredService<Diagram.DiagramValidators>()));
                services.AddSingleton<IContextActionProvider>(_provider);
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        TestFolder.TryDelete(_appDataRoot);
    }

    [Fact]
    public async Task TheDialogOffersOnlyWhatThePredicateAccepts_PinnedFirst_AndTheChosenPathReachesTheProvider()
    {
        // Arrange.
        var asked = await AskAsync();
        var dialog = asked.Prompt.FileDialog;

        // Assert: what the provider said, carried as it said it.
        Assert.Equal(ContextPrompt.PromptOneofCase.FileDialog, asked.Prompt.PromptCase);
        Assert.Equal(("Relate to", "mdi-link-variant", "Choose", "No table in this project."), (dialog.Title, dialog.Icon, dialog.ConfirmLabel, dialog.EmptyMessage));

        // The pinned entry comes first and is offered although the predicate refuses its file.
        var pinned = Assert.Single(dialog.Pinned);
        Assert.Equal(("notes.txt", "This file", true), (pinned.Id, pinned.Label, pinned.Selectable));

        // The tree: the folders that lead to an accepted file, and the accepted files. The docs
        // folder holds none and is gone; a file the predicate refuses is not offered; nothing
        // under a dot folder is.
        Assert.Equal(
            ["data/ (group)", "data/deep/ (group)", "data/deep/rivers.yaml", "data/countries.yaml", "cities.yaml"],
            Flatten(dialog.Files).Select(option => option.Selectable ? option.Id : option.Id + "/ (group)"));
        Assert.Equal(["data", "cities.yaml"], dialog.Files.Select(option => option.Label));

        // Act: the answer is a workspace path.
        var submitted = await asked.Context.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = asked.InteractionId, Value = "data/deep/rivers.yaml" }, asked.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(submitted.Completed, submitted.Error);
        Assert.Equal(["data/deep/rivers.yaml"], _provider.Committed);
    }

    [Theory]
    [InlineData("data/readme.md")]
    [InlineData("docs/guide.md")]
    [InlineData(".hidden/secret.yaml")]
    [InlineData("data")]
    [InlineData("../outside.yaml")]
    [InlineData("")]
    public async Task APathTheDialogDidNotOffer_IsRefused_AndNeverReachesTheProvider(string value)
    {
        // Arrange.
        var asked = await AskAsync();

        // Act.
        var submitted = await asked.Context.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = asked.InteractionId, Value = value }, asked.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: refused, with the dialog left open - a choice that is offered still goes through.
        Assert.False(submitted.Completed);
        Assert.Equal("That file cannot be chosen here.", submitted.Error);
        Assert.Empty(_provider.Committed);

        var retried = await asked.Context.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = asked.InteractionId, Value = "notes.txt" }, asked.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(retried.Completed, retried.Error);
        Assert.Equal(["notes.txt"], _provider.Committed);
    }

    [Fact]
    public async Task Cancelling_WritesNothing_AndTheDialogIsOver()
    {
        // Arrange.
        var asked = await AskAsync();
        var before = Listing();

        // Act.
        await asked.Context.CancelInteractionAsync(new CancelInteractionRequest { InteractionId = asked.InteractionId }, asked.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var late = await asked.Context.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = asked.InteractionId, Value = "cities.yaml" }, asked.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(late.Completed);
        Assert.Empty(_provider.Committed);
        Assert.Equal(before, Listing());
    }

    private string[] Listing() => [.. Directory.GetFileSystemEntries(_projectFolder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];

    private static IEnumerable<ContextOption> Flatten(IEnumerable<ContextOption> options) =>
        options.SelectMany(option => new[] { option }.Concat(Flatten(option.Children)));

    /// <summary>Signs in, watches, runs the fixture's action on the project root and returns the prompt it pushed.</summary>
    private async Task<Asked> AskAsync()
    {
        var httpClient = _factory.CreateDefaultClient();
        var channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions { HttpClient = httpClient });
        var login = await new AuthenticationService.AuthenticationServiceClient(channel).LoginAsync(
            new LoginRequest { Username = DeveloperUsername, Credential = DeveloperCredential }, cancellationToken: TestContext.Current.CancellationToken);
        var headers = new Metadata { { SessionTokenHeader, login.Session.Value } };

        var pathMessage = new Path();
        pathMessage.Segments.AddRange(_projectFolder.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));
        var added = await new ProjectService.ProjectServiceClient(channel).AddProjectAsync(new AddProjectRequest { Path = pathMessage }, headers, cancellationToken: TestContext.Current.CancellationToken);

        var context = new ContextService.ContextServiceClient(channel);
        Documents.Wire.ShortGuid watchId = ShortGuid.NewShortGuid();
        Documents.Wire.ShortGuid interactionId = ShortGuid.NewShortGuid();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(MessageTimeout);

        var watch = context.Watch(new WatchContextRequest { ProjectId = added.Added.Id, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await watch.ResponseStream.MoveNext(cts.Token), "The stream ended before the baseline arrived.");
        var pendingPrompt = ReadUntilPromptAsync(watch.ResponseStream, cts.Token);
        await Task.Delay(StreamStartupGrace, TestContext.Current.CancellationToken);

        // No source: the project root.
        var executed = await context.ExecuteActionAsync(
            new ExecuteActionRequest { ProjectId = added.Added.Id, WatchId = watchId, InteractionId = interactionId, ActionId = PickFileProvider.ActionId },
            headers,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(executed.Accepted, executed.Error);

        return new Asked(context, headers, interactionId, await pendingPrompt);
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

    private sealed record Asked(ContextService.ContextServiceClient Context, Metadata Headers, Documents.Wire.ShortGuid InteractionId, ContextPrompt Prompt);

    /// <summary>Asks for a YAML file of the project, with one pinned file its own rule would refuse.</summary>
    private sealed class PickFileProvider(string pinnedPath) : IContextActionProvider
    {
        public const string ActionId = "fixture.pick-file";

        public List<string> Committed { get; } = [];

        public ContextScope Scope => ContextScope.Hierarchy;

        public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>(
                target.IsContainer ? [new ContextActionGroupDefinition([new ContextActionDefinition(ActionId, "Pick a file", "mdi-link-variant")])] : []);

        public ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionRequiresFile(new ContextFileRequest(
                "Relate to",
                "mdi-link-variant",
                "Choose",
                path => path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase),
                "No table in this project.",
                [new ContextPinnedFile("This file", pinnedPath)])));

        public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ContextValidationResult.Accepted);

        public ValueTask<ContextCommitResult> CommitAsync(ContextTarget target, string actionId, string value, string text, CancellationToken cancellationToken)
        {
            Committed.Add(value);
            return ValueTask.FromResult(ContextCommitResult.Succeeded);
        }
    }
}
