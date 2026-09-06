using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Common;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

/// <summary>
/// The verbs: what is offered, what is withheld, and that each one dispatches the module's own
/// command through the project's history (Requirements 11.4-11.6, 9.8).
/// </summary>
public sealed class WardleyContextActionProviderTests : IDisposable
{
    private const string Map = """
        title Tea Shop
        component Cup of Tea [0.79, 0.61]
        component Kettle [0.43, 0.35] (buy)
        component Power [0.1, 0.7]
        Cup of Tea->Kettle
        Kettle+>Power

        """;

    private readonly string _root = IoPath.Combine(IoPath.GetTempPath(), $"wardley-actions-{Guid.NewGuid():N}");
    private readonly ServiceProvider _services;
    private readonly IWardleyDocumentStore _documents;
    private readonly WardleyContextActionProvider _provider;
    private readonly string _path;

    public WardleyContextActionProviderTests()
    {
        Directory.CreateDirectory(_root);
        _services = new ServiceCollection().AddCommands().AddWardleyMap().BuildServiceProvider();
        _documents = _services.GetRequiredService<IWardleyDocumentStore>();
        _provider = _services.GetServices<IContextActionProvider>().OfType<WardleyContextActionProvider>().Single();
        _path = IoPath.Combine(_root, "tea.owm");
        File.WriteAllText(_path, Map);
    }

    public void Dispose()
    {
        _services.Dispose();
        TestFolder.TryDelete(_root);
    }

    // ---- what is offered ----------------------------------------------------------------------

    [Fact]
    public async Task OnlyTheRenamePrompt_IsMarkedForInlineEditing()
    {
        // Arrange.
        // Three prompts asked together: a marker on the right action proves nothing if a
        // neighbour has quietly acquired one. Rename asks for the component's Name, which is
        // the drawn label. Add-component asks for a name that does not exist yet, and evolve
        // asks for a maturity number - emphatically not a label.
        var kettle = IdOf("Kettle");

        // Act.
        var rename = await _provider.ExecuteAsync(Target(kettle), WardleyContextActionProvider.RenameActionId, TestContext.Current.CancellationToken);
        var evolve = await _provider.ExecuteAsync(Target(kettle), WardleyContextActionProvider.SetEvolveActionId, TestContext.Current.CancellationToken);
        var add = await _provider.ExecuteAsync(Target(""), WardleyContextActionProvider.AddComponentActionId, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(kettle, Assert.IsType<ContextExecutionRequiresInput>(rename).Request.InlineLabelElementId);
        Assert.Equal("", Assert.IsType<ContextExecutionRequiresInput>(evolve).Request.InlineLabelElementId);
        Assert.Equal("", Assert.IsType<ContextExecutionRequiresInput>(add).Request.InlineLabelElementId);
    }

    [Fact]
    public void ItContributesToTheDiagramElementScope_AndNoOther()
    {
        // Arrange, act and assert.
        Assert.Equal(ContextScope.DiagramElement, _provider.Scope);
    }

    [Fact]
    public async Task AComponent_IsOfferedTheEditsOfRequirement93()
    {
        // Act.
        var ids = await ActionIdsFor(IdOf("Kettle"));

        // Assert. Every one of these dispatches a command, and each command has an inverse -
        // which is what makes the menu and the canvas two routes to one implementation.
        Assert.Contains(WardleyContextActionProvider.RenameActionId, ids);
        Assert.Contains(WardleyContextActionProvider.RemoveActionId, ids);
        Assert.Contains(WardleyContextActionProvider.SetEvolveActionId, ids);
        Assert.Contains(WardleyContextActionProvider.ToggleInertiaActionId, ids);
        Assert.Contains(WardleyContextActionProvider.LinkActionId, ids);
        Assert.Contains(WardleyContextActionProvider.AddToPipelineActionId, ids);
        Assert.Contains(WardleyContextActionProvider.DecoratorActionIdFor(WardleyDecorator.Buy), ids);
    }

    [Fact]
    public async Task EveryShortcutIsDescribedByTheBackend()
    {
        // Act.
        var actions = await ActionsFor(IdOf("Kettle"));

        // Assert. Requirement 11.5 - the client holds no key-to-action table, so a shortcut that
        // is not described here does not exist anywhere.
        var rename = actions.Single(action => action.Id == WardleyContextActionProvider.RenameActionId);
        var remove = actions.Single(action => action.Id == WardleyContextActionProvider.RemoveActionId);
        Assert.Equal("F2", rename.Shortcut?.Key);
        Assert.Equal("Delete", remove.Shortcut?.Key);
    }

    [Fact]
    public async Task ALabelSaysWhichWayTheToggleGoes()
    {
        // Act. Kettle carries `(buy)`; Power carries nothing.
        var kettle = await ActionsFor(IdOf("Kettle"));
        var power = await ActionsFor(IdOf("Power"));

        // Assert. A toggle whose label does not say what it will do is a coin flip.
        var id = WardleyContextActionProvider.DecoratorActionIdFor(WardleyDecorator.Buy);
        Assert.Equal("Clear buy", kettle.Single(action => action.Id == id).Label);
        Assert.Equal("Mark as buy", power.Single(action => action.Id == id).Label);
    }

    [Fact]
    public async Task AnUnlinkIsNotOfferedOnAnElementWithNoLink()
    {
        // Arrange. Requirement 11.6's own example.
        await File.WriteAllTextAsync(_path, "component Alone [0.5, 0.5]\ncomponent Other [0.2, 0.2]\nOther->Alone\n", TestContext.Current.CancellationToken);
        _documents.Forget(_path);
        await File.WriteAllTextAsync(_path, "component Alone [0.5, 0.5]\n", TestContext.Current.CancellationToken);
        _documents.Forget(_path);

        // Act.
        var ids = await ActionIdsFor(IdOf("Alone"));

        // Assert.
        Assert.DoesNotContain(WardleyContextActionProvider.UnlinkActionId, ids);
    }

    [Fact]
    public async Task StopEvolvingIsOnlyOfferedOnSomethingThatIsEvolving()
    {
        // Arrange.
        await File.WriteAllTextAsync(_path, "component Kettle [0.4, 0.3]\ncomponent Power [0.1, 0.7]\nevolve Kettle 0.7\n", TestContext.Current.CancellationToken);
        _documents.Forget(_path);

        // Act.
        var evolving = await ActionIdsFor(IdOf("Kettle"));
        var still = await ActionIdsFor(IdOf("Power"));

        // Assert.
        Assert.Contains(WardleyContextActionProvider.ClearEvolveActionId, evolving);
        Assert.DoesNotContain(WardleyContextActionProvider.ClearEvolveActionId, still);
    }

    [Fact]
    public async Task ALegacyPipelineIsOfferedNoMembership()
    {
        // Arrange. Requirement 3.2 forbids rewriting the legacy form into the nested one, so
        // there is nothing that could be added to it.
        await File.WriteAllTextAsync(_path, "component Power [0.1, 0.7]\npipeline Power [0.30, 0.85]\n", TestContext.Current.CancellationToken);
        _documents.Forget(_path);

        // Act.
        var ids = await ActionIdsFor(IdOf("Power"));

        // Assert.
        Assert.DoesNotContain(WardleyContextActionProvider.AddToPipelineActionId, ids);
    }

    [Fact]
    public async Task TheMapItself_IsOfferedTheAdds()
    {
        // Act. No element id: the diagram rather than something on it.
        var ids = await ActionIdsFor("");

        // Assert. Everything a map can be given that needs no other element to exist first -
        // which is also, one for one, what the toolbox offers as a drop.
        Assert.Equal(
            [
                WardleyContextActionProvider.AddComponentActionId,
                WardleyContextActionProvider.AddAnchorActionId,
                WardleyContextActionProvider.AddSubmapActionId,
                WardleyContextActionProvider.AddMarketActionId,
                WardleyContextActionProvider.AddEcosystemActionId,
                WardleyContextActionProvider.AddNoteActionId,
                WardleyContextActionProvider.AddAnnotationActionId,
            ],
            ids);
    }

    [Fact]
    public async Task AReadOnlyFileIsOfferedNothing()
    {
        // Arrange. Requirement 11.6 - an edit that would not reach disk is withheld rather than
        // offered and then quietly lost.
        File.SetAttributes(_path, FileAttributes.ReadOnly);
        try
        {
            // Act.
            var ids = await ActionIdsFor(IdOf("Kettle"));

            // Assert.
            Assert.Empty(ids);
        }
        finally
        {
            File.SetAttributes(_path, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task ANoteIsOfferedNothing_BecauseNoCommandEditsOneYet()
    {
        // Arrange.
        await File.WriteAllTextAsync(_path, "component Alpha [0.5, 0.5]\nnote Mind the gap [0.2, 0.2]\n", TestContext.Current.CancellationToken);
        _documents.Forget(_path);
        var note = _documents.Identities(_path).Single(entry => entry.Kind == WardleyIdentityKind.Note);

        // Act.
        var ids = await ActionIdsFor(note.Id);

        // Assert. Offering something nothing implements is the disagreement between menu and
        // outcome that Requirement 11.6 forbids.
        Assert.Empty(ids);
    }

    // ---- what they do -------------------------------------------------------------------------

    [Fact]
    public async Task ToggleInertia_EditsTheFileThroughTheHistory()
    {
        // Act.
        var result = await _provider.ExecuteAsync(
            Target(IdOf("Power")), WardleyContextActionProvider.ToggleInertiaActionId, TestContext.Current.CancellationToken);

        // Assert. Immediate: there is nothing to ask about a toggle.
        Assert.IsType<ContextExecutionCompleted>(result);
        Assert.Contains("component Power [0.1, 0.7] inertia", await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToggleInertia_IsUndoneByTheProjectsHistory()
    {
        // Arrange. Requirement 9.8 - one command behind every route means undo works whichever
        // route was taken.
        var before = await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken);

        // Act.
        await _provider.ExecuteAsync(
            Target(IdOf("Power")), WardleyContextActionProvider.ToggleInertiaActionId, TestContext.Current.CancellationToken);
        await _services.GetRequiredService<IHistoryStackStore>().Get(_root).UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(before, await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ADecoratorToggle_AddsAndThenRemovesTheSameWord()
    {
        // Arrange.
        var id = WardleyContextActionProvider.DecoratorActionIdFor(WardleyDecorator.Buy);

        // Act.
        await _provider.ExecuteAsync(Target(IdOf("Kettle")), id, TestContext.Current.CancellationToken);
        var cleared = await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken);
        await _provider.ExecuteAsync(Target(IdOf("Kettle")), id, TestContext.Current.CancellationToken);

        // Assert. The label said "Clear buy" because it was there; after clearing it, setting it
        // again puts the same word back.
        Assert.DoesNotContain("(buy)", cleared, StringComparison.Ordinal);
        Assert.Contains("(buy)", await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rename_AsksFirst_AndThenCommitsTheCommand()
    {
        // Act.
        var asked = await _provider.ExecuteAsync(
            Target(IdOf("Kettle")), WardleyContextActionProvider.RenameActionId, TestContext.Current.CancellationToken);
        var committed = await _provider.CommitAsync(
            Target(IdOf("Kettle")), WardleyContextActionProvider.RenameActionId, "Boiler", "", TestContext.Current.CancellationToken);

        // Assert. The dialog starts from the current name, so an edit begins where the user is.
        var request = Assert.IsType<ContextExecutionRequiresInput>(asked).Request;
        Assert.Equal("Kettle", request.InitialValue);
        Assert.True(committed.Completed, committed.Error);
        Assert.Contains("component Boiler [0.43, 0.35]", await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Contains("Cup of Tea->Boiler", await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Remove_AsksForConfirmationBecauseItTakesMoreThanTheStatementInFrontOfTheUser()
    {
        // Act.
        var asked = await _provider.ExecuteAsync(
            Target(IdOf("Kettle")), WardleyContextActionProvider.RemoveActionId, TestContext.Current.CancellationToken);

        // Assert.
        var request = Assert.IsType<ContextExecutionRequiresConfirmation>(asked).Request;
        Assert.True(request.Danger);
        Assert.Contains("every link", request.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LinkTo_OffersEveryOtherElement_AndLinksTheChosenOne()
    {
        // Act.
        var asked = await _provider.ExecuteAsync(
            Target(IdOf("Cup of Tea")), WardleyContextActionProvider.LinkActionId, TestContext.Current.CancellationToken);
        var request = Assert.IsType<ContextExecutionRequiresChoice>(asked).Request;
        var power = request.Options.Single(option => option.Label == "Power");
        var committed = await _provider.CommitAsync(
            Target(IdOf("Cup of Tea")), WardleyContextActionProvider.LinkActionId, power.Id, "", TestContext.Current.CancellationToken);

        // Assert. The option's id IS the element id the command needs, so the dialog's answer is
        // already what gets dispatched.
        Assert.Equal(["Kettle", "Power"], request.Options.Select(option => option.Label));
        Assert.True(committed.Completed, committed.Error);
        Assert.Contains("Cup of Tea->Power", await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FlowTo_WritesTheOtherArrow()
    {
        // Act.
        var asked = await _provider.ExecuteAsync(
            Target(IdOf("Cup of Tea")), WardleyContextActionProvider.FlowActionId, TestContext.Current.CancellationToken);
        var options = Assert.IsType<ContextExecutionRequiresChoice>(asked).Request.Options;
        await _provider.CommitAsync(
            Target(IdOf("Cup of Tea")),
            WardleyContextActionProvider.FlowActionId,
            options.Single(option => option.Label == "Power").Id,
            "",
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains("Cup of Tea+>Power", await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unlink_WithOneLink_DoesNotAskWhichOne()
    {
        // Arrange. A dialog offering a choice of one exists only to be dismissed.
        await File.WriteAllTextAsync(_path, "component Alpha [0.5, 0.5]\ncomponent Beta [0.2, 0.2]\nAlpha->Beta\n", TestContext.Current.CancellationToken);
        _documents.Forget(_path);

        // Act.
        var result = await _provider.ExecuteAsync(
            Target(IdOf("Alpha")), WardleyContextActionProvider.UnlinkActionId, TestContext.Current.CancellationToken);

        // Assert.
        Assert.IsType<ContextExecutionCompleted>(result);
        Assert.Equal("component Alpha [0.5, 0.5]\ncomponent Beta [0.2, 0.2]\n", await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Unlink_WithSeveralLinks_AsksWhichOne_AndRemovesOnlyThatOne()
    {
        // Act. Kettle is an end of both links in the fixture.
        var asked = await _provider.ExecuteAsync(
            Target(IdOf("Kettle")), WardleyContextActionProvider.UnlinkActionId, TestContext.Current.CancellationToken);
        var request = Assert.IsType<ContextExecutionRequiresChoice>(asked).Request;
        var chosen = request.Options.Single(option => option.Label == "Kettle +> Power");
        var committed = await _provider.CommitAsync(
            Target(IdOf("Kettle")), WardleyContextActionProvider.UnlinkActionId, chosen.Id, "", TestContext.Current.CancellationToken);

        // Assert. The label says which arrow it is, because the two claims are different.
        Assert.Equal(["Cup of Tea -> Kettle", "Kettle +> Power"], request.Options.Select(option => option.Label));
        Assert.True(committed.Completed, committed.Error);
        var text = await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken);
        Assert.Contains("Cup of Tea->Kettle", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Kettle+>Power", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALinkItself_IsSelectableAndRemovable()
    {
        // Arrange. Selecting the arrow and pressing Delete is the gesture this exists for.
        var link = _documents.Identities(_path).First(entry => entry.Kind == WardleyIdentityKind.Link);

        // Act.
        var offered = await ActionIdsFor(link.Id);
        var result = await _provider.ExecuteAsync(
            Target(link.Id), WardleyContextActionProvider.UnlinkActionId, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal([WardleyContextActionProvider.UnlinkActionId], offered);
        Assert.IsType<ContextExecutionCompleted>(result);
        Assert.DoesNotContain("Cup of Tea->Kettle", await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Evolve_RefusesANumberOffTheScale_AndAcceptsOneOnIt()
    {
        // Arrange, act and assert. Requirement 7.3 clamps a DRAG past the edge, because that is
        // a slip of the hand; a typed 1.4 is a misunderstanding of the scale, and saying so is
        // more use than silently storing something else.
        var target = Target(IdOf("Kettle"));
        var refused = await _provider.ValidateAsync(target, WardleyContextActionProvider.SetEvolveActionId, "1.4", TestContext.Current.CancellationToken);
        var accepted = await _provider.ValidateAsync(target, WardleyContextActionProvider.SetEvolveActionId, "0.8", TestContext.Current.CancellationToken);

        Assert.False(refused.Valid);
        Assert.Contains("between 0 and 1", refused.Reason, StringComparison.Ordinal);
        Assert.True(accepted.Valid);
    }

    [Fact]
    public async Task Evolve_StartsFromWhereTheElementIs_WhenItIsNotEvolvingYet()
    {
        // Act.
        var asked = await _provider.ExecuteAsync(
            Target(IdOf("Kettle")), WardleyContextActionProvider.SetEvolveActionId, TestContext.Current.CancellationToken);

        // Assert. An evolution target starts at the component's own maturity, which is the one
        // number the user is certainly thinking about.
        Assert.Equal("0.35", Assert.IsType<ContextExecutionRequiresInput>(asked).Request.InitialValue);
    }

    [Fact]
    public async Task AddToPipeline_CommitsTheMembershipCommand()
    {
        // Act.
        var committed = await _provider.CommitAsync(
            Target(IdOf("Kettle")),
            WardleyContextActionProvider.AddToPipelineActionId,
            "Electric Kettle",
            "",
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(committed.Completed, committed.Error);
        Assert.Contains("  component Electric Kettle [0.5]", await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task APipelineChild_IsOfferedOnlyItsRemoval()
    {
        // Arrange.
        await File.WriteAllTextAsync(_path, "component Kettle [0.4, 0.3]\npipeline Kettle\n{\n  component Electric Kettle [0.63]\n}\n", TestContext.Current.CancellationToken);
        _documents.Forget(_path);
        var child = _documents.Identities(_path).Single(entry => entry.Kind == WardleyIdentityKind.PipelineChild);

        // Act.
        var ids = await ActionIdsFor(child.Id);
        var result = await _provider.ExecuteAsync(
            Target(child.Id), WardleyContextActionProvider.RemoveFromPipelineActionId, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal([WardleyContextActionProvider.RemoveFromPipelineActionId], ids);
        Assert.IsType<ContextExecutionCompleted>(result);
        Assert.Equal("component Kettle [0.4, 0.3]\npipeline Kettle\n{\n}\n", await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnElementThatIsGone_IsRefusedWithAMessageRatherThanThrowing()
    {
        // Act.
        var result = await _provider.ExecuteAsync(
            Target("no-such-id"), WardleyContextActionProvider.RenameActionId, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("That element is no longer on this map.", Assert.IsType<ContextExecutionFailed>(result).Message);
    }

    // ---- plumbing -----------------------------------------------------------------------------

    private string IdOf(string name) => _documents
        .Identities(_path)
        .Single(entry => entry.Kind == WardleyIdentityKind.Component && entry.Key == name)
        .Id;

    private ContextTarget Target(string elementId) => new(
        ContextScope.DiagramElement, _path, IsContainer: false, SourceId: default, _root, default, elementId);

    private async Task<IReadOnlyList<ContextActionDefinition>> ActionsFor(string elementId)
    {
        var groups = await _provider.DiscoverAsync(Target(elementId), TestContext.Current.CancellationToken);
        return groups.SelectMany(group => group.Actions).ToArray();
    }

    private async Task<IReadOnlyList<string>> ActionIdsFor(string elementId) =>
        (await ActionsFor(elementId)).Select(action => action.Id).ToArray();
}
