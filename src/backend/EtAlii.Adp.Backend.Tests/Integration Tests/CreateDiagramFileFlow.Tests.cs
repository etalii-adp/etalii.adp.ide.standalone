using System.Text;
using EtAlii.Adp.Diagram;
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
/// Creating a diagram, end to end against the real host: the prompt's name field and its
/// per-option suggestions, the name judged while it is typed, the file that appears on disk
/// with the chosen type's MIME type as its only line, the project-relative path reported
/// back, and the entry reaching the connection through the ordinary hierarchy change feed.
/// </summary>
public class CreateDiagramFileFlowTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string DeveloperUsername = "admin";
    private const string DeveloperCredential = "changeme";
    private const string SessionTokenHeader = "session-token";
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StreamStartupGrace = TimeSpan.FromMilliseconds(500);

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _appDataRoot;
    private readonly string _projectFolder;

    public CreateDiagramFileFlowTests(WebApplicationFactory<Program> baseFactory)
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

    private GrpcChannel CreateChannel()
    {
        var httpClient = _factory.CreateDefaultClient();
        return GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions { HttpClient = httpClient });
    }

    private static async Task<Metadata> LoginAsync(GrpcChannel channel)
    {
        var authClient = new AuthenticationService.AuthenticationServiceClient(channel);
        var response = await authClient.LoginAsync(new LoginRequest { Username = DeveloperUsername, Credential = DeveloperCredential });
        return new Metadata { { SessionTokenHeader, response.Session.Value } };
    }

    private async Task<Contracts.ShortGuid> AddProjectAsync(GrpcChannel channel, Metadata headers)
    {
        var projectClient = new ProjectService.ProjectServiceClient(channel);
        var pathMessage = new Path();
        pathMessage.Segments.AddRange(_projectFolder.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));
        var response = await projectClient.AddProjectAsync(new AddProjectRequest { Path = pathMessage }, headers);
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

    private static async Task<HierarchyChange> ReadUntilChangeAsync(
        IAsyncStreamReader<HierarchyMessage> stream, HierarchyChange.ChangeOneofCase wanted, CancellationToken cancellationToken)
    {
        while (await stream.MoveNext(cancellationToken))
        {
            var message = stream.Current;
            if (message.MessageCase == HierarchyMessage.MessageOneofCase.Change && message.Change.ChangeCase == wanted)
            {
                return message.Change;
            }
        }

        throw new InvalidOperationException($"The stream ended before a {wanted} change arrived.");
    }

    /// <summary>The first selectable leaf in the tree, depth first - a real diagram type to choose.</summary>
    private static ContextOption FirstLeaf(IEnumerable<ContextOption> options)
    {
        foreach (var option in options)
        {
            if (option.Selectable)
            {
                return option;
            }

            var leaf = FirstLeaf(option.Children);
            if (leaf is not null)
            {
                return leaf;
            }
        }

        return null!;
    }

    private sealed record Session(
        GrpcChannel Channel,
        Metadata Headers,
        Contracts.ShortGuid ProjectId,
        Contracts.ShortGuid WatchId,
        HierarchyService.HierarchyServiceClient Hierarchy,
        ContextService.ContextServiceClient Context) : IDisposable
    {
        public void Dispose() => Channel.Dispose();
    }

    private async Task<Session> OpenSessionAsync()
    {
        var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        return new Session(
            channel, headers, projectId, ShortGuid.NewShortGuid(),
            new HierarchyService.HierarchyServiceClient(channel),
            new ContextService.ContextServiceClient(channel));
    }

    /// <summary>Opens Add on the given target and returns the prompt it pushed, with its interaction id.</summary>
    private async Task<(ContextPrompt Prompt, Contracts.ShortGuid InteractionId)> OpenAddDialogAsync(
        Session session, IAsyncStreamReader<ContextMessage> stream, CancellationToken cancellationToken, Contracts.ShortGuid? folderId = null)
    {
        var pendingPrompt = ReadUntilPromptAsync(stream, cancellationToken);
        await Task.Delay(StreamStartupGrace);

        var interactionId = ShortGuid.NewShortGuid();
        var request = new ExecuteActionRequest
        {
            ProjectId = session.ProjectId,
            WatchId = session.WatchId,
            InteractionId = interactionId,
            ActionId = AddDiagramContextActionProvider.AddActionId,
        };
        if (folderId is not null)
        {
            request.Source = new ContextSource { EntryId = folderId };
        }

        var executed = await session.Context.ExecuteActionAsync(request, session.Headers);
        Assert.True(executed.Accepted, executed.Error);

        return (await pendingPrompt, interactionId);
    }

    [Fact]
    public async Task TheFullArc_JudgesTheNameWhileTyping_ThenCreatesTheDiagramAndReportsWhereItLanded()
    {
        using var session = await OpenSessionAsync();
        using var cts = new CancellationTokenSource(MessageTimeout);
        using var contextCall = session.Context.Watch(
            new WatchContextRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers);
        await ReadBaselineAsync(contextCall.ResponseStream, cts.Token);

        var (prompt, interactionId) = await OpenAddDialogAsync(session, contextCall.ResponseStream, cts.Token);
        Assert.Equal(ContextPrompt.PromptOneofCase.ChoiceDialog, prompt.PromptCase);

        // The dialog can be answered without typing: it carries a name field and every
        // selectable option carries the name to put in it.
        Assert.Equal("Name", prompt.ChoiceDialog.NameField.Label);
        var leaf = FirstLeaf(prompt.ChoiceDialog.Options);
        Assert.NotEqual("", leaf.SuggestedValue);

        var rejected = await session.Context.ProposeInputAsync(
            new ProposeInputRequest { InteractionId = interactionId, Revision = 1, Value = "sub/domain" }, session.Headers);
        Assert.False(rejected.Valid);
        Assert.NotEqual("", rejected.Reason);
        Assert.Equal(1u, rejected.Revision);

        var accepted = await session.Context.ProposeInputAsync(
            new ProposeInputRequest { InteractionId = interactionId, Revision = 2, Value = "domain" }, session.Headers);
        Assert.True(accepted.Valid);

        var submitted = await session.Context.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = interactionId, Value = leaf.Id, Text = "domain" }, session.Headers);

        Assert.True(submitted.Completed, submitted.Error);
        Assert.Equal(new[] { "domain.adp" }, submitted.CreatedPath.Segments);

        // The bytes, not the string: a byte-order mark would be invisible in a comparison.
        var created = IoPath.Combine(_projectFolder, "domain.adp");
        var expectedMimeType = DiagramDefinition.All.Single(definition => definition.Origin.Key == leaf.Id).Origin.MimeType;
        Assert.Equal(Encoding.UTF8.GetBytes(expectedMimeType + "\n"), File.ReadAllBytes(created));
    }

    [Fact]
    public async Task TheCreatedFile_ArrivesThroughTheOrdinaryHierarchyChangeFeed_AndNoScratchFileEverDoes()
    {
        using var session = await OpenSessionAsync();
        using var cts = new CancellationTokenSource(MessageTimeout);

        // The watcher only runs while a hierarchy stream is open, and the root must have been
        // listed for a create under it to be reported at all.
        await session.Hierarchy.ListEntriesAsync(
            new ListEntriesRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers);
        using var hierarchyCall = session.Hierarchy.WatchHierarchy(
            new WatchHierarchyRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers);
        var pendingCreate = ReadUntilChangeAsync(hierarchyCall.ResponseStream, HierarchyChange.ChangeOneofCase.Created, cts.Token);

        using var contextCall = session.Context.Watch(
            new WatchContextRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers);
        await ReadBaselineAsync(contextCall.ResponseStream, cts.Token);
        var (prompt, interactionId) = await OpenAddDialogAsync(session, contextCall.ResponseStream, cts.Token);
        var leaf = FirstLeaf(prompt.ChoiceDialog.Options);

        var submitted = await session.Context.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = interactionId, Value = leaf.Id, Text = "domain" }, session.Headers);
        Assert.True(submitted.Completed, submitted.Error);

        // The first created entry the connection hears about is the diagram itself: the
        // writer's scratch file is never an entry, so it cannot arrive first.
        var change = await pendingCreate;
        Assert.Equal("domain.adp", change.Created.Entry.Name);
        Assert.Equal(EntryKind.File, change.Created.Entry.Kind);
    }

    [Fact]
    public async Task AddingIntoAFolder_CreatesTheDiagramThere_AndReportsAPathRelativeToTheProject()
    {
        Directory.CreateDirectory(IoPath.Combine(_projectFolder, "docs"));

        using var session = await OpenSessionAsync();
        using var cts = new CancellationTokenSource(MessageTimeout);
        var entries = await session.Hierarchy.ListEntriesAsync(
            new ListEntriesRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers);
        var folderId = entries.Entries.Entries_.Single(entry => entry.Name == "docs").Id;

        using var contextCall = session.Context.Watch(
            new WatchContextRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers);
        await ReadBaselineAsync(contextCall.ResponseStream, cts.Token);
        var (prompt, interactionId) = await OpenAddDialogAsync(session, contextCall.ResponseStream, cts.Token, folderId);
        var leaf = FirstLeaf(prompt.ChoiceDialog.Options);

        var submitted = await session.Context.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = interactionId, Value = leaf.Id, Text = "domain" }, session.Headers);

        Assert.True(submitted.Completed, submitted.Error);
        Assert.Equal(new[] { "docs", "domain.adp" }, submitted.CreatedPath.Segments);
        Assert.True(File.Exists(IoPath.Combine(_projectFolder, "docs", "domain.adp")));
        Assert.False(File.Exists(IoPath.Combine(_projectFolder, "domain.adp")));
    }

    [Fact]
    public async Task ANameTakenBehindTheDialogsBack_IsReported_AndTheExistingFileIsUntouched()
    {
        using var session = await OpenSessionAsync();
        using var cts = new CancellationTokenSource(MessageTimeout);
        using var contextCall = session.Context.Watch(
            new WatchContextRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers);
        await ReadBaselineAsync(contextCall.ResponseStream, cts.Token);
        var (prompt, interactionId) = await OpenAddDialogAsync(session, contextCall.ResponseStream, cts.Token);
        var leaf = FirstLeaf(prompt.ChoiceDialog.Options);

        // Created while the dialog was open, after its suggestion was computed.
        var existing = IoPath.Combine(_projectFolder, "domain.adp");
        File.WriteAllText(existing, "someone else's diagram");

        var submitted = await session.Context.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = interactionId, Value = leaf.Id, Text = "domain" }, session.Headers);

        Assert.False(submitted.Completed);
        Assert.Contains("already exists", submitted.Error, StringComparison.Ordinal);
        Assert.Equal("someone else's diagram", File.ReadAllText(existing));
    }

    [Fact]
    public async Task ANameThatIsAPath_IsRefusedServerSide_EvenWithNoProposeBeforeIt()
    {
        using var session = await OpenSessionAsync();
        using var cts = new CancellationTokenSource(MessageTimeout);
        using var contextCall = session.Context.Watch(
            new WatchContextRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers);
        await ReadBaselineAsync(contextCall.ResponseStream, cts.Token);
        var (prompt, interactionId) = await OpenAddDialogAsync(session, contextCall.ResponseStream, cts.Token);
        var leaf = FirstLeaf(prompt.ChoiceDialog.Options);

        var submitted = await session.Context.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = interactionId, Value = leaf.Id, Text = @"..\escaped" }, session.Headers);

        Assert.False(submitted.Completed);
        Assert.False(File.Exists(IoPath.Combine(_appDataRoot, "escaped.adp")));
        Assert.Empty(Directory.GetFiles(_projectFolder));
    }

    [Fact]
    public async Task AnEmptyName_IsRefused_RatherThanCreatingAFileCalledJustTheExtension()
    {
        using var session = await OpenSessionAsync();
        using var cts = new CancellationTokenSource(MessageTimeout);
        using var contextCall = session.Context.Watch(
            new WatchContextRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers);
        await ReadBaselineAsync(contextCall.ResponseStream, cts.Token);
        var (prompt, interactionId) = await OpenAddDialogAsync(session, contextCall.ResponseStream, cts.Token);
        var leaf = FirstLeaf(prompt.ChoiceDialog.Options);

        var submitted = await session.Context.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = interactionId, Value = leaf.Id, Text = "" }, session.Headers);

        Assert.False(submitted.Completed);
        Assert.Equal("Enter a name.", submitted.Error);
        Assert.Empty(Directory.GetFiles(_projectFolder));
    }

    [Fact]
    public async Task TwoDiagramsOfTheSameTypeInOneFolder_BothSucceed_BecauseTheSuggestionCountsUp()
    {
        using var session = await OpenSessionAsync();
        using var cts = new CancellationTokenSource(MessageTimeout);
        using var contextCall = session.Context.Watch(
            new WatchContextRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers);
        await ReadBaselineAsync(contextCall.ResponseStream, cts.Token);

        var (first, firstInteraction) = await OpenAddDialogAsync(session, contextCall.ResponseStream, cts.Token);
        var leaf = FirstLeaf(first.ChoiceDialog.Options);
        var firstName = leaf.SuggestedValue;
        var firstSubmit = await session.Context.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = firstInteraction, Value = leaf.Id, Text = firstName }, session.Headers);
        Assert.True(firstSubmit.Completed, firstSubmit.Error);

        // The second dialog is built after the first file exists, so its suggestion moves on.
        var (second, secondInteraction) = await OpenAddDialogAsync(session, contextCall.ResponseStream, cts.Token);
        var secondLeaf = FirstLeaf(second.ChoiceDialog.Options);
        Assert.Equal($"{firstName}-2", secondLeaf.SuggestedValue);

        var secondSubmit = await session.Context.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = secondInteraction, Value = secondLeaf.Id, Text = secondLeaf.SuggestedValue }, session.Headers);

        Assert.True(secondSubmit.Completed, secondSubmit.Error);
        Assert.True(File.Exists(IoPath.Combine(_projectFolder, $"{firstName}.adp")));
        Assert.True(File.Exists(IoPath.Combine(_projectFolder, $"{firstName}-2.adp")));
    }
}
