using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.Hierarchy.Wire;
using EtAlii.Adp.TestSupport.Host;
using Grpc.Core;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The whole Add arc against the real host: the root's actions on the baseline, the choice
/// prompt pushed down the connection's own stream for the root and for a folder, the
/// "not supported yet" answer on submit with the folder untouched, no Add for a file, and
/// the folder vanishing between the prompt and the submit.
/// </summary>
public class AddDiagramFlowTests(HostFixture host) : IClassFixture<HostFixture>
{
    private readonly string _projectFolder = host.NewProjectFolder();

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

    /// <summary>
    /// The first selectable leaf that takes a name, depth first - a real diagram type to choose
    /// and then name.
    /// </summary>
    /// <remarks>
    /// The name-suppressed check is what keeps this pointing at the right kind of type. A
    /// folder-subject type is selectable and sorts first (ansible), but its registration is
    /// named for it and its dialog offers no name field - so every assertion in this file about
    /// naming, validation and collisions is meaningless against one. Before folder-subject
    /// types behaved differently these tests were already choosing one and nothing said so.
    /// </remarks>
    private static ContextOption FirstLeaf(IEnumerable<ContextOption> options)
    {
        foreach (var option in options)
        {
            if (option.Selectable && option.NameSuppressedReason.Length == 0)
            {
                return option;
            }

            // Keep looking rather than returning whatever the first group yields. This used to
            // return unconditionally, so it only ever searched the first vendor - which went
            // unnoticed while that vendor's first leaf was always the answer.
            foreach (var leaf in option.Children)
            {
                if (leaf.Selectable && leaf.NameSuppressedReason.Length == 0)
                {
                    return leaf;
                }
            }
        }

        throw new InvalidOperationException("No selectable option that takes a name is in the tree.");
    }

    [Fact]
    public async Task Baseline_WithNothingSelected_CarriesAddForTheRoot_EnabledBesideGreyedRenameAndDelete()
    {
        // Arrange.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var contextClient = session.Context;
        var watchId = ShortGuid.NewShortGuid();

        // Act.
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var contextCall = contextClient.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = watchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var baseline = await ReadBaselineAsync(contextCall.ResponseStream, cts.Token);

        // Assert.
        Assert.Null(baseline.Selection);
        var actions = baseline.Actions.Actions();
        var add = Assert.Single(actions, action => action.Id == AddDiagramContextActionProvider.AddActionId);
        Assert.True(add.Available);
        Assert.Equal("Insert", add.Shortcut.Key);
        var validate = Assert.Single(actions, action => action.Id == Problems.ValidateContextActionProvider.ValidateActionId);
        Assert.True(validate.Available); // Validating the root is validating the whole project - always sensible.
        // The root is a folder, but the one folder the project must not rename or delete.
        // The additive actions - Add, Validate, New folder - stay enabled: they act within
        // the root, never on it.
        Assert.All(
            actions.Where(action => action.Id is not AddDiagramContextActionProvider.AddActionId
                and not Problems.ValidateContextActionProvider.ValidateActionId
                and not HierarchyContextActionProvider.AddFolderActionId), AssertAction);
        return;

        static void AssertAction(ContextAction action)
        {
            ArgumentNullException.ThrowIfNull(action);
            Assert.False(action.Available);
            Assert.NotEqual("", action.UnavailableReason);
        }
    }

    [Fact]
    public async Task Add_OnTheRoot_OpensTheChoiceDialog_AndSubmittingCreatesTheDiagram()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "existing.txt"), "x", TestContext.Current.CancellationToken);

        // Arrange, continued.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var contextClient = session.Context;
        var watchId = ShortGuid.NewShortGuid();

        // Arrange, continued.
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var contextCall = contextClient.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = watchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await ReadBaselineAsync(contextCall.ResponseStream, cts.Token);
        var pendingPrompt = ReadUntilPromptAsync(contextCall.ResponseStream, cts.Token);

        // Arrange, continued.
        // No source: the explorer's empty space.
        var interactionId = ShortGuid.NewShortGuid();
        var executed = await contextClient.ExecuteActionAsync(
            new ExecuteActionRequest { ProjectId = session.ProjectId, WatchId = watchId, InteractionId = interactionId, ActionId = AddDiagramContextActionProvider.AddActionId },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(executed.Accepted, executed.Error);

        // Arrange, continued.
        var prompt = await pendingPrompt;
        Assert.Equal(ContextPrompt.PromptOneofCase.ChoiceDialog, prompt.PromptCase);
        Assert.Equal("Add diagram", prompt.ChoiceDialog.Title);
        Assert.Equal("Add", prompt.ChoiceDialog.ConfirmLabel);
        Assert.NotEmpty(prompt.ChoiceDialog.Options);
        // Top level is vendors, not selectable; something selectable lies beneath.
        Assert.All(prompt.ChoiceDialog.Options, vendor => Assert.False(vendor.Selectable));
        var leaf = FirstLeaf(prompt.ChoiceDialog.Options);

        // Arrange, continued.
        // The dialog also carries a name field, and each selectable option a suggestion for it.
        Assert.Equal("Name", prompt.ChoiceDialog.NameField.Label);
        Assert.NotEqual("", leaf.SuggestedValue);

        // Act.
        var submitted = await contextClient.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = interactionId, Value = leaf.Id, Text = leaf.SuggestedValue },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        // create-diagram-file replaced this spec's "not supported yet" answer with the real
        // thing: a file on disk, and its project-relative path reported back.
        Assert.True(submitted.Completed, submitted.Error);
        Assert.Equal(new[] { $"{leaf.SuggestedValue}.adp" }, submitted.CreatedPath.Segments);
        Assert.True(File.Exists(IoPath.Combine(_projectFolder, $"{leaf.SuggestedValue}.adp")));
    }

    [Fact]
    public async Task Add_OnAFolder_ViaItsEntryId_OpensTheChoiceDialog()
    {
        // Arrange.
        Directory.CreateDirectory(IoPath.Combine(_projectFolder, "docs"));

        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var hierarchyClient = session.Hierarchy;
        var contextClient = session.Context;
        var watchId = ShortGuid.NewShortGuid();

        var entries = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = session.ProjectId, WatchId = watchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var folderId = entries.Entries.Entries_.Single(e => e.Name == "docs").Id;

        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var contextCall = contextClient.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = watchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await ReadBaselineAsync(contextCall.ResponseStream, cts.Token);
        var pendingPrompt = ReadUntilPromptAsync(contextCall.ResponseStream, cts.Token);

        // Act and assert, step by step.
        var discovered = await contextClient.DiscoverActionsAsync(
            new DiscoverActionsRequest { ProjectId = session.ProjectId, WatchId = watchId, Source = new ContextSource { EntryId = folderId } },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken);
        var add = Assert.Single(discovered.Groups.Actions(), a => a.Id == AddDiagramContextActionProvider.AddActionId);
        Assert.True(add.Available);

        var executed = await contextClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = session.ProjectId,
                WatchId = watchId,
                InteractionId = ShortGuid.NewShortGuid(),
                Source = new ContextSource { EntryId = folderId },
                ActionId = AddDiagramContextActionProvider.AddActionId,
            },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(executed.Accepted, executed.Error);

        var prompt = await pendingPrompt;
        Assert.Equal(ContextPrompt.PromptOneofCase.ChoiceDialog, prompt.PromptCase);
    }

    [Fact]
    public async Task DiscoverActions_OnAFile_OffersNoAdd()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "a.txt"), "x", TestContext.Current.CancellationToken);

        // Arrange, continued.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var hierarchyClient = session.Hierarchy;
        var contextClient = session.Context;
        var watchId = ShortGuid.NewShortGuid();

        // Arrange, continued.
        var entries = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = session.ProjectId, WatchId = watchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var fileId = entries.Entries.Entries_.Single(e => e.Name == "a.txt").Id;

        // Act.
        var discovered = await contextClient.DiscoverActionsAsync(
            new DiscoverActionsRequest { ProjectId = session.ProjectId, WatchId = watchId, Source = new ContextSource { EntryId = fileId } },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.DoesNotContain(discovered.Groups.Actions(), a => a.Id == AddDiagramContextActionProvider.AddActionId);
        // ...while the file's own actions are still there.
        Assert.Contains(discovered.Groups.Actions(), a => a.Id == HierarchyContextActionProvider.RenameActionId);
    }

    [Fact]
    public async Task Submit_AfterTheFolderVanished_ReportsTheFolder()
    {
        // Arrange.
        var folder = IoPath.Combine(_projectFolder, "doomed");
        Directory.CreateDirectory(folder);

        // Arrange, continued.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var hierarchyClient = session.Hierarchy;
        var contextClient = session.Context;
        var watchId = ShortGuid.NewShortGuid();

        // Arrange, continued.
        var entries = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = session.ProjectId, WatchId = watchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var folderId = entries.Entries.Entries_.Single(e => e.Name == "doomed").Id;

        // Arrange, continued.
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var contextCall = contextClient.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = watchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await ReadBaselineAsync(contextCall.ResponseStream, cts.Token);
        var pendingPrompt = ReadUntilPromptAsync(contextCall.ResponseStream, cts.Token);

        // Arrange, continued.
        var interactionId = ShortGuid.NewShortGuid();
        var executed = await contextClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = session.ProjectId,
                WatchId = watchId,
                InteractionId = interactionId,
                Source = new ContextSource { EntryId = folderId },
                ActionId = AddDiagramContextActionProvider.AddActionId,
            },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(executed.Accepted, executed.Error);
        var prompt = await pendingPrompt;
        var leaf = FirstLeaf(prompt.ChoiceDialog.Options);

        // Arrange, continued.
        // The dialog is open; the folder goes away underneath it.
        Directory.Delete(folder, recursive: true);

        // Act.
        var submitted = await contextClient.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = interactionId, Value = leaf.Id },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(submitted.Completed);
        Assert.Equal("The folder no longer exists.", submitted.Error);
    }
}
