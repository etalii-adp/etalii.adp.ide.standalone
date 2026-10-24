using EtAlii.Adp.Context;
using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Projects;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using ContextService = EtAlii.Adp.Context.Wire.ContextService;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The file dialog against the real host (knowledge-designer task 11, Requirements 10.6 and
/// 5.1): a provider asks for a file with a predicate; the prompt carries the workspace's files
/// that the predicate accepts and no other, with the pinned entries first; the chosen path
/// reaches the provider; a path that was not offered never does; and cancelling changes nothing.
/// </summary>
public sealed class FilePromptFlowTests : IClassFixture<FilePromptFlowTests.Host>
{
    private readonly Host _host;
    private readonly string _projectFolder;
    private readonly PickFileProvider _provider;

    public FilePromptFlowTests(Host host)
    {
        // One host serves the class, so the provider it holds is told which test it now serves.
        _host = host;
        _projectFolder = host.NewProjectFolder();
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
        _provider = host.Provider;
        _provider.Begin(IoPath.Combine(_projectFolder, "notes.txt"));
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
        Assert.Equal(("Name there", "Cities"), (dialog.NameField.Label, dialog.NameField.InitialValue));

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
            new SubmitInteractionRequest { InteractionId = asked.InteractionId, Value = "data/deep/rivers.yaml", Text = "Seats" }, asked.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the file, and beside it what was typed in the name field.
        Assert.True(submitted.Completed, submitted.Error);
        Assert.Equal(["data/deep/rivers.yaml"], _provider.Committed);
        Assert.Equal(["Seats"], _provider.Named);
    }

    [Fact]
    public async Task AnActionThatAsks_ResolvesTheProjectOnce()
    {
        // Act.
        var asked = await AskAsync();

        // Assert: the target and the interaction it opens are found under one reading of the
        // user's projects, not one each (code-structure FR-062, SC-016).
        Assert.Equal(1, asked.ProjectReads);
    }

    [Fact]
    public async Task AnAnswerThatLeadsToAFurtherQuestion_BringsItsDialogWithIt_AsAnInteractionOfItsOwn()
    {
        // Arrange.
        var asked = await AskAsync();

        // Act: the file whose choice the provider follows with a question.
        var first = await asked.Context.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = asked.InteractionId, Value = "cities.yaml" }, asked.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: taken, and the next dialog comes on the answer, under an interaction of its own.
        Assert.True(first.Completed, first.Error);
        Assert.Equal(ContextPrompt.PromptOneofCase.ConfirmDialog, first.NextPrompt.PromptCase);
        Assert.Equal(("Both ways?", "Yes"), (first.NextPrompt.ConfirmDialog.Message, first.NextPrompt.ConfirmDialog.ConfirmLabel));
        Assert.NotEqual(asked.InteractionId, first.NextPrompt.InteractionId);

        // Act: the further question is answered; the first interaction is over.
        var second = await asked.Context.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = first.NextPrompt.InteractionId, Value = "" }, asked.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var again = await asked.Context.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = asked.InteractionId, Value = "cities.yaml" }, asked.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: committed under the id the provider gave, with nothing more to ask.
        Assert.True(second.Completed, second.Error);
        Assert.Null(second.NextPrompt);
        Assert.False(again.Completed);
        Assert.Equal([PickFileProvider.ActionId, PickFileProvider.FollowUpId + "cities.yaml"], _provider.Under);
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
        // The session lives as long as the test: its channel carries every call the test makes
        // through the returned client.
        var session = await FlowSession.OpenAsync(_host, _projectFolder, TestContext.Current.CancellationToken);
        var headers = session.Headers;
        var context = session.Context;
        var watchId = session.WatchId;
        Documents.Wire.ShortGuid interactionId = ShortGuid.NewShortGuid();
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);

        var watch = context.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await watch.ResponseStream.MoveNext(cts.Token), "The stream ended before the baseline arrived.");
        var pendingPrompt = watch.ResponseStream.ReadUntilPromptAsync(cts.Token);

        // No source: the project root.
        var readsBefore = _host.Projects.Reads;
        var executed = await context.ExecuteActionAsync(
            new ExecuteActionRequest { ProjectId = session.ProjectId, WatchId = watchId, InteractionId = interactionId, ActionId = PickFileProvider.ActionId },
            headers,
            cancellationToken: TestContext.Current.CancellationToken);
        var projectReads = _host.Projects.Reads - readsBefore;
        Assert.True(executed.Accepted, executed.Error);

        return new Asked(context, headers, interactionId, await pendingPrompt, projectReads);
    }

    private sealed record Asked(ContextService.ContextServiceClient Context, Metadata Headers, Documents.Wire.ShortGuid InteractionId, ContextPrompt Prompt, int ProjectReads);

    /// <summary>The shared host, with the fixture's file-asking action registered and its project list counted.</summary>
    public sealed class Host : HostFixture
    {
        internal PickFileProvider Provider { get; } = new();

        internal CountingProjectStore Projects { get; private set; } = null!;

        protected override void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IContextActionProvider>(Provider);

            // The host's own store, counted: how often a request asks for the user's projects.
            var store = (IProjectStore)services.Single(descriptor => descriptor.ServiceType == typeof(IProjectStore)).ImplementationInstance!;
            Projects = new CountingProjectStore(store);
            services.RemoveAll<IProjectStore>();
            services.AddSingleton<IProjectStore>(Projects);
        }
    }

    /// <summary>A project store that counts the readings of the project list it passes on.</summary>
    internal sealed class CountingProjectStore(IProjectStore inner) : IProjectStore
    {
        private int _reads;

        public int Reads => Volatile.Read(ref _reads);

        public IReadOnlyList<ProjectRecord> List(ShortGuid userId)
        {
            Interlocked.Increment(ref _reads);
            return inner.List(userId);
        }

        public Task<ProjectRecord> AddAsync(ShortGuid userId, string name, PathRecord path, CancellationToken cancellationToken) => inner.AddAsync(userId, name, path, cancellationToken);

        public Task RemoveAsync(ShortGuid userId, ShortGuid projectId, CancellationToken cancellationToken) => inner.RemoveAsync(userId, projectId, cancellationToken);
    }

    /// <summary>Asks for a YAML file of the project, with one pinned file its own rule would refuse.</summary>
    internal sealed class PickFileProvider : IContextActionProvider
    {
        private string _pinnedPath = "";
        public const string ActionId = "fixture.pick-file";
        public const string FollowUpId = "fixture.pick-file.then:";

        /// <summary>The action id each commit came under.</summary>
        public List<string> Under { get; } = [];

        public List<string> Committed { get; } = [];

        /// <summary>What was typed in the dialog's name field, per commit.</summary>
        public List<string> Named { get; } = [];

        public ContextScope Scope => ContextScope.Hierarchy;

        /// <summary>Forgets what an earlier test committed and pins the given test's file.</summary>
        public void Begin(string pinnedPath)
        {
            _pinnedPath = pinnedPath;
            Under.Clear();
            Committed.Clear();
            Named.Clear();
        }

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
                [new ContextPinnedFile("This file", _pinnedPath)],
                new ContextTextFieldRequest("Name there", "Cities"))));

        public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ContextValidationResult.Accepted);

        public ValueTask<ContextCommitResult> CommitAsync(ContextTarget target, string actionId, string value, string text, CancellationToken cancellationToken)
        {
            Committed.Add(value);
            Named.Add(text);
            Under.Add(actionId);

            // One file leads to a further question, answered under an id that carries the file.
            return ValueTask.FromResult(actionId == ActionId && value == "cities.yaml"
                ? ContextCommitResult.Asks(new ContextExecutionRequiresConfirmation(new ContextConfirmationRequest("Relate", "mdi-link-variant", "Both ways?", "Yes", Danger: false)), FollowUpId + value)
                : ContextCommitResult.Succeeded);
        }
    }
}
