using EtAlii.Adp.Context;
using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy.Wire;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ContextService = EtAlii.Adp.Context.Wire.ContextService;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// A reveal against the real host (etalii.adp spec 092, T022, FR-014): an action that points at an element of a document of the
/// project puts the document's path, the entry the connection knows it by, the tool that opens it and the element on that
/// connection's stream, so its workspace can open the document there; one that points at a document the project no longer
/// holds is refused with a sentence, and nothing is pushed.
/// </summary>
public sealed class RevealFlowTests : IClassFixture<RevealFlowTests.Host>
{
    private readonly Host _host;
    private readonly string _projectFolder;

    public RevealFlowTests(Host host)
    {
        _host = host;
        _projectFolder = host.NewProjectFolder();
        Directory.CreateDirectory(IoPath.Combine(_projectFolder, "docs"));
        File.WriteAllText(IoPath.Combine(_projectFolder, "docs", "plan.tml"), "timeline: 1\nelements:\n  - id: aaa\n    label: Launch\n    begin: 2027-01-04\n    row: 0\n");
        host.Provider.PointAt = IoPath.Combine(_projectFolder, "docs", "plan.tml");
    }

    [Fact]
    public async Task AnActionThatPointsAtAnElement_PutsItsDocumentsEntryToolAndElementOnTheStream()
    {
        // Arrange.
        var session = await FlowSession.OpenAsync(_host, _projectFolder, TestContext.Current.CancellationToken);
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        var watch = session.Context.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await watch.ResponseStream.MoveNext(cts.Token), "The stream ended before the baseline arrived.");
        var pending = watch.ResponseStream.FirstAsync(message => message.MessageCase == ContextMessage.MessageOneofCase.Reveal, cts.Token);

        // Act.
        var executed = await ExecuteAsync(session.Context, session.Headers, session.ProjectId, session.WatchId);
        var reveal = (await pending).Reveal;

        // Assert: the entry is the one this connection's explorer lists for the file.
        var root = await session.Hierarchy.ListEntriesAsync(new ListEntriesRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var docs = root.Entries.Entries_.Single(entry => entry.Name == "docs").Id;
        var listed = await session.Hierarchy.ListEntriesAsync(new ListEntriesRequest { ProjectId = session.ProjectId, WatchId = session.WatchId, FolderId = docs }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(executed.Accepted, executed.Error);
        Assert.Equal(["docs", "plan.tml"], reveal.Path.Segments);
        Assert.Equal("aaa", reveal.ElementId);
        Assert.Equal(listed.Entries.Entries_.Single(entry => entry.Name == "plan.tml").Id, reveal.EntryId);
        Assert.NotEqual("", reveal.MimeType);
    }

    [Fact]
    public async Task AnActionThatPointsAtADocumentThatIsGone_IsRefused()
    {
        // Arrange.
        var session = await FlowSession.OpenAsync(_host, _projectFolder, TestContext.Current.CancellationToken);
        File.Delete(_host.Provider.PointAt);

        // Act.
        var executed = await ExecuteAsync(session.Context, session.Headers, session.ProjectId, session.WatchId);

        // Assert.
        Assert.False(executed.Accepted);
        Assert.Equal("That document is not in this project any more.", executed.Error);
    }

    private static async Task<ExecuteActionResponse> ExecuteAsync(ContextService.ContextServiceClient context, Metadata headers, Documents.Wire.ShortGuid projectId, Documents.Wire.ShortGuid watchId) =>
        await context.ExecuteActionAsync(
            new ExecuteActionRequest { ProjectId = projectId, WatchId = watchId, InteractionId = ShortGuid.NewShortGuid(), ActionId = PointingProvider.ActionId },
            headers,
            cancellationToken: TestContext.Current.CancellationToken);

    /// <summary>The shared host, with the fixture's pointing action registered.</summary>
    public sealed class Host : HostFixture
    {
        internal PointingProvider Provider { get; } = new();

        protected override void ConfigureServices(IServiceCollection services) => services.AddSingleton<IContextActionProvider>(Provider);
    }

    /// <summary>Points at the element <c>aaa</c> of the file the test names.</summary>
    internal sealed class PointingProvider : IContextActionProvider
    {
        public const string ActionId = "fixture.reveal";

        public string PointAt { get; set; } = "";

        public ContextScope Scope => ContextScope.Hierarchy;

        public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>(
                target.IsContainer ? [new ContextActionGroupDefinition([new ContextActionDefinition(ActionId, "Go there", "mdi-open-in-new")])] : []);

        public ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionReveals(PointAt, "aaa"));

        public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ContextValidationResult.Accepted);

        public ValueTask<ContextCommitResult> CommitAsync(ContextTarget target, string actionId, string value, string text, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ContextCommitResult.Succeeded);
    }
}
