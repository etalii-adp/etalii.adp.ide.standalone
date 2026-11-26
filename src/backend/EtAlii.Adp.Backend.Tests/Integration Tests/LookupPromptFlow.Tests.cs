using EtAlii.Adp.Context;
using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Documents.Wire;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ContextService = EtAlii.Adp.Context.Wire.ContextService;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The lookup against the real host (DISL 0.12 §9.4, etalii.adp spec 092, T021): a provider opens a lookup; the prompt says how to
/// show it; the search the client makes while it is open is the provider's own, under the interaction's id and no other; the pick
/// reaches the provider as the interaction's answer; and once the interaction is over its search answers that it is gone.
/// </summary>
public sealed class LookupPromptFlowTests : IClassFixture<LookupPromptFlowTests.Host>
{
    private readonly Host _host;
    private readonly string _projectFolder;

    public LookupPromptFlowTests(Host host)
    {
        _host = host;
        _projectFolder = host.NewProjectFolder();
        host.Provider.Committed.Clear();
    }

    [Fact]
    public async Task TheLookupIsShownAsItWasAsked_SearchedByTheProvider_AndItsPickReachesTheProvider()
    {
        // Arrange.
        var asked = await AskAsync();
        var dialog = asked.Prompt.LookupDialog;

        // Act.
        var found = await asked.Context.SearchLookupAsync(new SearchLookupRequest { InteractionId = asked.InteractionId, Query = "check" }, asked.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var submitted = await asked.Context.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = asked.InteractionId, Value = found.Candidates[0].Value }, asked.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(ContextPrompt.PromptOneofCase.LookupDialog, asked.Prompt.PromptCase);
        Assert.Equal(("Add from another document", "mdi-link-plus", "Search by name", "Nothing like that."), (dialog.Title, dialog.Icon, dialog.Placeholder, dialog.EmptyMessage));
        Assert.Equal(("", false), (found.Error, found.More));
        var candidate = Assert.Single(found.Candidates);
        Assert.Equal(("$k7q2m9xa@features.yaml#checkout=Checkout", "Checkout", "features.yaml", "specs"), (candidate.Value, candidate.Name, candidate.DocumentName, candidate.Folder));
        Assert.True(submitted.Completed, submitted.Error);
        Assert.Equal(["$k7q2m9xa@features.yaml#checkout=Checkout"], _host.Provider.Committed);
    }

    [Fact]
    public async Task ASearchThatMatchesMoreThanAreShown_GivesTheFirstAndSaysThereAreMore()
    {
        // Arrange.
        var asked = await AskAsync();

        // Act: a query the provider answers with more candidates than a lookup shows.
        var found = await asked.Context.SearchLookupAsync(new SearchLookupRequest { InteractionId = asked.InteractionId, Query = "many" }, asked.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal((ContextLookupRequest.Shown, true), (found.Candidates.Count, found.More));
    }

    [Fact]
    public async Task ASearchUnderAnotherInteraction_OrAfterTheLookupEnded_FindsNothing_AndSaysWhy()
    {
        // Arrange.
        var asked = await AskAsync();

        // Act.
        var stranger = await asked.Context.SearchLookupAsync(new SearchLookupRequest { InteractionId = ShortGuid.NewShortGuid(), Query = "check" }, asked.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await asked.Context.CancelInteractionAsync(new CancelInteractionRequest { InteractionId = asked.InteractionId }, asked.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var ended = await asked.Context.SearchLookupAsync(new SearchLookupRequest { InteractionId = asked.InteractionId, Query = "check" }, asked.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal((0, "This lookup is no longer active."), (stranger.Candidates.Count, stranger.Error));
        Assert.Equal((0, "This lookup is no longer active."), (ended.Candidates.Count, ended.Error));
        Assert.Empty(_host.Provider.Committed);
    }

    /// <summary>Signs in, watches, runs the fixture's action on the project root and returns the prompt it pushed.</summary>
    private async Task<Asked> AskAsync()
    {
        var session = await FlowSession.OpenAsync(_host, _projectFolder, TestContext.Current.CancellationToken);
        Documents.Wire.ShortGuid interactionId = ShortGuid.NewShortGuid();
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);

        var watch = session.Context.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await watch.ResponseStream.MoveNext(cts.Token), "The stream ended before the baseline arrived.");
        var pendingPrompt = watch.ResponseStream.ReadUntilPromptAsync(cts.Token);

        var executed = await session.Context.ExecuteActionAsync(
            new ExecuteActionRequest { ProjectId = session.ProjectId, WatchId = session.WatchId, InteractionId = interactionId, ActionId = LookupProvider.ActionId },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(executed.Accepted, executed.Error);

        return new Asked(session.Context, session.Headers, interactionId, await pendingPrompt);
    }

    private sealed record Asked(ContextService.ContextServiceClient Context, Metadata Headers, Documents.Wire.ShortGuid InteractionId, ContextPrompt Prompt);

    /// <summary>The shared host, with the fixture's lookup action registered.</summary>
    public sealed class Host : HostFixture
    {
        internal LookupProvider Provider { get; } = new();

        protected override void ConfigureServices(IServiceCollection services) => services.AddSingleton<IContextActionProvider>(Provider);
    }

    /// <summary>Opens a lookup over one element, whose name it matches against the query.</summary>
    internal sealed class LookupProvider : IContextActionProvider
    {
        public const string ActionId = "fixture.lookup";

        private static readonly ContextLookupCandidate Checkout = new("$k7q2m9xa@features.yaml#checkout=Checkout", "Checkout", "features.yaml", "specs");

        public List<string> Committed { get; } = [];

        public ContextScope Scope => ContextScope.Hierarchy;

        public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>(
                target.IsContainer ? [new ContextActionGroupDefinition([new ContextActionDefinition(ActionId, "Look up", "mdi-link-plus")])] : []);

        public ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionRequiresLookup(new ContextLookupRequest(
                "Add from another document", "mdi-link-plus", "Search by name", "Nothing like that.", Search)));

        public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ContextValidationResult.Accepted);

        public ValueTask<ContextCommitResult> CommitAsync(ContextTarget target, string actionId, string value, string text, CancellationToken cancellationToken)
        {
            Committed.Add(value);
            return ValueTask.FromResult(ContextCommitResult.Succeeded);
        }

        private static IReadOnlyList<ContextLookupCandidate> Search(string query) =>
            query == "many" ? [.. Enumerable.Range(0, ContextLookupRequest.Shown + 10).Select(Many)]
            : Checkout.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ? [Checkout] : [];

        private static ContextLookupCandidate Many(int index) => new($"$m1m1m1m1@many.tml#e{index}=Many {index}", $"Many {index}", "many.tml", "");
    }
}
