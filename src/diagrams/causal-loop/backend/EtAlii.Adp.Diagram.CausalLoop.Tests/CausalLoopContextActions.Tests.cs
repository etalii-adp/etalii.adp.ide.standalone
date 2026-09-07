using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Context;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.CausalLoop.Tests;

/// <summary>
/// The context surface (causal-loop-diagram Requirements 5.3, 5.4, 5.5): every gesture reachable
/// through the standard path, every one that cannot apply discovered unavailable with its reason,
/// and a toolbox described by the backend as data.
/// </summary>
public class CausalLoopContextActionsTests : IDisposable
{
    private const string Corpus =
        "causal-loop 1\r\n"
        + "variable population \"Population\"\r\n"
        + "variable births \"Births\"\r\n"
        + "link population -> births +\r\n"
        + "link births -> population +\r\n"
        + "loop R1 \"Births beget births\" population births\r\n";

    private readonly string _root;
    private readonly string _path;
    private readonly ServiceProvider _provider;
    private readonly CausalLoopDocumentStore _store = new();
    private readonly IDiagramViewportRegistry _sessions = new DiagramViewportRegistry();
    private readonly CausalLoopContextActionProvider _actions;

    public CausalLoopContextActionsTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _path = IoPath.Combine(_root, "feedback.cld");
        File.WriteAllText(_path, Corpus);

        _provider = new ServiceCollection()
            .AddSingleton(_sessions)
            .AddCommands().AddHierarchyCommandHandlers()
            .AddCausalLoop()
            .BuildServiceProvider();
        // The registry is the host's, registered by AddDiagrams rather than by this module, so
        // these tests supply one directly rather than pulling in the whole diagram stack.
        _actions = new CausalLoopContextActionProvider(
            _store, _provider.GetRequiredService<IHistoryStackStore>(), _sessions);
    }

    public void Dispose()
    {
        _provider.Dispose();
        TestFolder.TryDelete(_root);
        GC.SuppressFinalize(this);
    }

    private ContextTarget Target(string elementId) =>
        new(ContextScope.DiagramElement, _path, false, ShortGuid.NewShortGuid(), _root, default, elementId);

    private async Task<IReadOnlyList<ContextActionDefinition>> Discover(string elementId)
    {
        var groups = await _actions.DiscoverAsync(Target(elementId), TestContext.Current.CancellationToken);
        return [.. groups.SelectMany(group => group.Actions)];
    }

    // ---- what each selection offers ---------------------------------------------------------

    [Fact]
    public async Task AVariable_OffersItsGestures()
    {
        // Act.
        var actions = await Discover("variable:population");

        // Assert.
        Assert.NotEmpty(actions);
        Assert.Contains(actions, action => action.Id == CausalLoopContextActionProvider.AddLinkActionId);
        Assert.Contains(actions, action => action.Id == CausalLoopContextActionProvider.RenameVariableActionId);
        Assert.Contains(actions, action => action.Id == CausalLoopContextActionProvider.RemoveVariableActionId);
    }

    [Fact]
    public async Task ALink_OffersItsPolarityItsDelayAndItsRemoval()
    {
        // Act.
        var actions = await Discover("link:population|births");

        // Assert.
        Assert.Contains(actions, action => action.Id == CausalLoopContextActionProvider.MakeNegativeActionId);
        Assert.Contains(actions, action => action.Id == CausalLoopContextActionProvider.ToggleDelayActionId);
        Assert.Contains(actions, action => action.Id == CausalLoopContextActionProvider.RemoveLinkActionId);
    }

    [Fact]
    public async Task ALoop_OffersRenameAndRemoval_AndNothingAboutPolarity()
    {
        // Act.
        var actions = await Discover("loop:R1");

        // Assert.
        // A loop's polarity is counted from its links, so there is nothing here to set it to.
        Assert.Contains(actions, action => action.Id == CausalLoopContextActionProvider.RemoveLoopActionId);
        Assert.DoesNotContain(actions, action => action.Id == CausalLoopContextActionProvider.MakePositiveActionId);
    }

    [Fact]
    public async Task APlacement_OffersANewVariable_AndTheDiagramWideArrangement()
    {
        // Act.
        var actions = await Discover(CausalLoopSelection.PlacementFor(120, 240));

        // Assert.
        // With nothing selected the diagram itself is the subject, which is where a diagram-wide
        // action belongs (Requirement 6.1). It is offered here and nowhere else.
        Assert.Contains(actions, action => action.Id == CausalLoopContextActionProvider.AddVariableActionId);
        Assert.True(
            Assert.Single(actions, action => action.Id == CausalLoopContextActionProvider.ArrangeActionId).Available);

        Assert.DoesNotContain(
            await Discover("variable:population"),
            action => action.Id == CausalLoopContextActionProvider.ArrangeActionId);
    }

    /// <summary>
    /// Requirement 6.1 again, from the other side: the arrangement is something a user invokes,
    /// so it is discovered as an action rather than happening when a document opens.
    /// </summary>
    [Fact]
    public async Task TheArrangement_IsUnavailableWithItsReason_OnADiagramWithNothingToArrange()
    {
        // Arrange.
        await File.WriteAllTextAsync(_path, "causal-loop 1\r\nvariable alone \"Alone\"\r\n", TestContext.Current.CancellationToken);
        _store.Reload(_path);

        // Act.
        var arrange = Assert.Single(
            await Discover(CausalLoopSelection.PlacementFor(0, 0)),
            action => action.Id == CausalLoopContextActionProvider.ArrangeActionId);

        // Assert.
        Assert.False(arrange.Available);
        Assert.Contains("two variables", arrange.UnavailableReason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The arrangement needs the registration, which only the open session knows: a context
    /// target carries the body it was opened for, and a body does not know which .adp registered
    /// it. With no session on this connection the action says so rather than failing obscurely.
    /// </summary>
    [Fact]
    public async Task TheArrangement_SaysSo_WhenTheDiagramIsNotOpenOnThisConnection()
    {
        // Act.
        var result = await _actions.ExecuteAsync(
            Target(CausalLoopSelection.PlacementFor(0, 0)),
            CausalLoopContextActionProvider.ArrangeActionId,
            TestContext.Current.CancellationToken);

        // Assert.
        var failed = Assert.IsType<ContextExecutionFailed>(result);
        Assert.Contains("not open on this connection", failed.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The arrangement runs, end to end, through the path a user's right-click actually takes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The gap this closes.</b> The command had ten tests and the provider had sixteen, and
    /// between them sat the one step neither exercised: the provider reaching the open session
    /// through the viewport registry and calling it. The only test of that seam asserted the
    /// <i>negative</i> case - that a diagram not open on the connection says so - which passes
    /// whether or not the positive case works at all.
    /// </para>
    /// <para>
    /// So this registers a real session the way the diagram service does, executes the action the
    /// way the context service does, and asserts on the file. No target is built by hand.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheArrangement_RunsThroughTheActionPath_AndIsOneUndoAway()
    {
        // Arrange.
        var adpPath = IoPath.Combine(_root, "feedback.adp");
        await File.WriteAllTextAsync(adpPath, "systems/causal-loop-diagram\r\n", TestContext.Current.CancellationToken);
        var before = await File.ReadAllTextAsync(adpPath, TestContext.Current.CancellationToken);

        var watchId = ShortGuid.NewShortGuid();
        var history = _provider.GetRequiredService<IHistoryStackStore>();
        var session = new CausalLoopSessionFactory(
                ServiceCollectionAddCausalLoopExtension.CausalLoopOrigin,
                _store,
                new CausalLoopElementMapper(),
                history)
            .Open(watchId, _root, _path, adpPath);

        // Registered exactly as the diagram service registers an open stream - which is what
        // lets a unary action reach the session behind it.
        _sessions.Register(watchId, _path, session, _ => { });

        var target = new ContextTarget(
            ContextScope.DiagramElement, _path, false, ShortGuid.NewShortGuid(), _root, watchId,
            CausalLoopSelection.PlacementFor(0, 0));

        // Act.
        var result = await _actions.ExecuteAsync(
            target, CausalLoopContextActionProvider.ArrangeActionId, TestContext.Current.CancellationToken);

        // Assert.
        Assert.IsType<ContextExecutionCompleted>(result);

        // The arrangement landed in the registration, and every variable took a position.
        var stored = RegistrationLayout.Read(adpPath);
        Assert.Equal(2, stored.Count);
        Assert.Contains("variable:population", stored.Keys);
        Assert.Contains("variable:births", stored.Keys);

        // The body is untouched: an arrangement is an opinion about where things are drawn.
        Assert.Equal(Corpus, await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken));

        // And it is ONE undo away, not one per variable (Requirement 6.8).
        var undone = await history.Get(_root).UndoAsync(TestContext.Current.CancellationToken);
        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Equal(before, await File.ReadAllTextAsync(adpPath, TestContext.Current.CancellationToken));
    }

    // ---- unavailable with a reason, never silently absent -----------------------------------

    /// <summary>
    /// Requirement 5.4. A missing entry tells a user nothing; a greyed one with a sentence tells
    /// them what to change.
    /// </summary>
    [Fact]
    public async Task AGestureThatCannotApply_IsOfferedUnavailableWithItsReason()
    {
        // Act.
        var actions = await Discover("variable:nowhere");

        // Assert.
        Assert.NotEmpty(actions);
        Assert.All(actions, action =>
        {
            ArgumentNullException.ThrowIfNull(action);

            Assert.False(action.Available);
            Assert.Equal(CausalLoopWriter.NoSuchVariable, action.UnavailableReason);
        });
    }

    [Fact]
    public async Task ALinkAlreadyStatingAPolarity_SaysSoRatherThanOfferingItAgain()
    {
        // Act.
        var actions = await Discover("link:population|births");

        // Assert.
        var positive = Assert.Single(actions, action => action.Id == CausalLoopContextActionProvider.MakePositiveActionId);
        Assert.False(positive.Available);
        Assert.Contains("already states +", positive.UnavailableReason, StringComparison.Ordinal);

        // ...and the one it does not state is offered.
        var negative = Assert.Single(actions, action => action.Id == CausalLoopContextActionProvider.MakeNegativeActionId);
        Assert.True(negative.Available);
    }

    [Fact]
    public async Task TheDelayGesture_ReadsAsTheOppositeOfWhatTheLinkStates()
    {
        // Act.
        var before = Assert.Single(
            await Discover("link:population|births"),
            action => action.Id == CausalLoopContextActionProvider.ToggleDelayActionId);

        await File.WriteAllTextAsync(_path, Corpus.Replace("link population -> births +", "link population -> births + delayed", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        _store.Reload(_path);

        var after = Assert.Single(
            await Discover("link:population|births"),
            action => action.Id == CausalLoopContextActionProvider.ToggleDelayActionId);

        // Assert.
        Assert.Equal("Delayed", before.Label);
        Assert.Equal("Not delayed", after.Label);
    }

    [Fact]
    public async Task TheFlipGesture_ReadsAsTheOppositeOfWhatTheLinkStates_AndWritesTheWord()
    {
        // Which side an arc bows to is the module's own decision until the author overrides it,
        // so this gesture is a toggle on the document rather than a view setting: it has to
        // offer the opposite of what is written, and write what it offered.
        // Act.
        var before = Assert.Single(
            await Discover("link:population|births"),
            action => action.Id == CausalLoopContextActionProvider.FlipCurvatureActionId);

        var result = await _actions.ExecuteAsync(
            Target("link:population|births"),
            CausalLoopContextActionProvider.FlipCurvatureActionId,
            TestContext.Current.CancellationToken);

        var written = await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken);
        _store.Reload(_path);

        var after = Assert.Single(
            await Discover("link:population|births"),
            action => action.Id == CausalLoopContextActionProvider.FlipCurvatureActionId);

        // Assert.
        Assert.IsType<ContextExecutionCompleted>(result);
        Assert.Equal("Flip the curve", before.Label);
        Assert.Contains("link population -> births + flipped", written, StringComparison.Ordinal);
        Assert.Equal("Curve back the other way", after.Label);
    }

    // ---- the gestures that replaced the dialogs ---------------------------------------------

    [Fact]
    public async Task AddingAVariable_CompletesWithACalculatedName_WithoutAskingForOne()
    {
        // The drop and the canvas "Add variable" no longer prompt; the module names the variable.
        // Act.
        var result = await _actions.ExecuteAsync(
            Target(CausalLoopSelection.PlacementFor(10, 20)),
            CausalLoopContextActionProvider.AddVariableActionId,
            TestContext.Current.CancellationToken);

        // Assert. Not a dialog; a variable appears, named by the module.
        Assert.IsNotType<ContextExecutionRequiresInput>(result);
        Assert.IsType<ContextExecutionCompleted>(result);
        _store.Reload(_path);
        Assert.Contains(_store.GetOrLoad(_path).Model.Variables, variable => variable.Id == "variable1");
    }

    [Fact]
    public async Task RenamingAVariable_OpensInlineOverIt_AndEditsTheLabelNotTheIdentifier()
    {
        // The rename is the inline editor now, marked with the variable's element id so the canvas
        // opens it over the pill; committing sets the LABEL, leaving the id its links refer to.
        // Act.
        var prompt = await _actions.ExecuteAsync(
            Target("variable:births"),
            CausalLoopContextActionProvider.RenameVariableActionId,
            TestContext.Current.CancellationToken);

        // Assert: an inline input, seeded with the current label.
        var input = Assert.IsType<ContextExecutionRequiresInput>(prompt);
        Assert.Equal("variable:births", input.Request.InlineLabelElementId);
        Assert.Equal("Births", input.Request.InitialValue);

        // And committing changes the label while the id and its links stand.
        await _actions.CommitAsync(
            Target("variable:births"), CausalLoopContextActionProvider.RenameVariableActionId,
            "Newborns", "", TestContext.Current.CancellationToken);
        _store.Reload(_path);
        var model = _store.GetOrLoad(_path).Model;
        Assert.Contains(model.Variables, variable => variable.Id == "births" && variable.Display == "Newborns");
        Assert.Contains(model.Links, link => link.From == "births" || link.To == "births");
    }

    [Fact]
    public async Task ADrawnLink_StatesItAndClaimsNoDialog_ThroughTheConnectGesture()
    {
        // The right-button draw raises a `rel:{from}->{to}`; the provider states that link with no
        // dialog. (The corpus already links both ways, so this draws a self-loop-free fresh pair.)
        // Arrange.
        var freshPath = IoPath.Combine(_root, "fresh.cld");
        await File.WriteAllTextAsync(
            freshPath, "causal-loop 1\r\n\r\nvariable a\r\nvariable b\r\nlink a -> b +\r\n",
            TestContext.Current.CancellationToken);
        var target = new ContextTarget(
            ContextScope.DiagramElement, freshPath, false, ShortGuid.NewShortGuid(), _root, default,
            CausalLoopSelection.RelationFor("b", "a"));

        // Act.
        var result = await _actions.ExecuteAsync(
            target, CausalLoopContextActionProvider.ConnectActionId, TestContext.Current.CancellationToken);

        // Assert.
        Assert.IsType<ContextExecutionCompleted>(result);
        _store.Reload(freshPath);
        var model = _store.GetOrLoad(freshPath).Model;
        Assert.Contains(model.Links, link => link.From == "b" && link.To == "a");
        // And the reverse link closes a loop, which the same edit claimed.
        Assert.Single(model.Loops);
    }

    [Fact]
    public async Task TheConnectGesture_IsOfferedForARelationTarget_SoExecuteActionAcceptsIt()
    {
        // ExecuteAction runs only an action the target discovered. A gesture-only action still has
        // to be discovered for its transient target, or the gesture is refused as unavailable.
        // Act.
        var actions = (await _actions.DiscoverAsync(
            Target(CausalLoopSelection.RelationFor("population", "births")),
            TestContext.Current.CancellationToken)).SelectMany(group => group.Actions).ToList();

        // Assert.
        Assert.Contains(actions, action => action.Id == CausalLoopContextActionProvider.ConnectActionId);
    }

    // ---- the removal that takes more than it names ------------------------------------------

    /// <summary>
    /// Removing a variable takes the links and loops that named it, so the count is stated before
    /// anything runs rather than discovered afterwards.
    /// </summary>
    [Fact]
    public async Task RemovingAVariable_SaysWhatElseItWillTake()
    {
        // Act.
        var label = Assert.Single(
            await Discover("variable:population"),
            action => action.Id == CausalLoopContextActionProvider.RemoveVariableActionId).Label;

        var result = await _actions.ExecuteAsync(
            Target("variable:population"),
            CausalLoopContextActionProvider.RemoveVariableActionId,
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains("3 references", label, StringComparison.Ordinal);
        var confirmation = Assert.IsType<ContextExecutionRequiresConfirmation>(result);
        Assert.Contains("also removes the 3 links and loops", confirmation.Request.Message, StringComparison.Ordinal);
        Assert.True(confirmation.Request.Danger);
    }

    // ---- validation --------------------------------------------------------------------------

    [Fact]
    public async Task ANameTheFormatCannotRoundTrip_IsRejectedBeforeItIsWritten()
    {
        // Adding a variable no longer asks for a name - the module calculates one - so the name
        // the format has to round-trip is the one the inline rename edits: the label, which is
        // written quoted and so cannot itself hold a quote.
        // Act.
        var result = await _actions.ValidateAsync(
            Target("variable:population"),
            CausalLoopContextActionProvider.RenameVariableActionId,
            "a \" quote",
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.Valid);
    }

    [Fact]
    public async Task ALinkToSomethingUndeclared_IsRejectedNamingIt()
    {
        // Act.
        var result = await _actions.ValidateAsync(
            Target("variable:population"),
            CausalLoopContextActionProvider.AddLinkActionId,
            "nowhere",
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.Valid);
        Assert.Contains("'nowhere'", result.Reason, StringComparison.Ordinal);
    }

    // ---- the gesture actually runs -----------------------------------------------------------

    [Fact]
    public async Task ChangingAPolarity_LandsAsACommandAndChangesTheDocument()
    {
        // Act.
        var result = await _actions.ExecuteAsync(
            Target("link:population|births"),
            CausalLoopContextActionProvider.MakeNegativeActionId,
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.IsType<ContextExecutionCompleted>(result);
        Assert.Contains("link population -> births -", await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddingALink_AsksForTheOtherEnd_ThenWritesIt()
    {
        // Arrange.
        await File.WriteAllTextAsync(_path, Corpus + "variable deaths \"Deaths\"\r\n", TestContext.Current.CancellationToken);
        _store.Reload(_path);

        // Act.
        var asked = await _actions.ExecuteAsync(
            Target("variable:population"),
            CausalLoopContextActionProvider.AddLinkActionId,
            TestContext.Current.CancellationToken);

        var committed = await _actions.CommitAsync(
            Target("variable:population"),
            CausalLoopContextActionProvider.AddLinkActionId,
            "deaths",
            "",
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.IsType<ContextExecutionRequiresInput>(asked);
        Assert.True(committed.Completed, committed.Error);
        Assert.Contains("link population -> deaths +", await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    /// <summary>
    /// The fixture is deliberately a file whose <i>content</i> this module could parse. A foreign
    /// document full of unreadable text would leave this passing for the wrong reason: nothing
    /// would be found whether or not the extension was checked at all.
    /// </summary>
    [Fact]
    public async Task ADocumentOfAnotherType_IsNotAnswered_EvenWhenItsContentWouldParse()
    {
        // Arrange.
        var other = IoPath.Combine(_root, "notes.md");
        await File.WriteAllTextAsync(other, Corpus, TestContext.Current.CancellationToken);

        // Act.
        var groups = await _actions.DiscoverAsync(
            new ContextTarget(
                ContextScope.DiagramElement, other, false, ShortGuid.NewShortGuid(), _root, default, "variable:population"),
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(groups);
    }

    [Fact]
    public async Task AnActionThatIsNotOurs_IsNotClaimed()
    {
        // Act.
        var result = await _actions.ExecuteAsync(
            Target("variable:population"), "mindmap.add-node", TestContext.Current.CancellationToken);

        // Assert.
        Assert.IsType<ContextExecutionFailed>(result);
    }

    // ---- the toolbox --------------------------------------------------------------------------

    [Fact]
    public void TheToolbox_IsDataFromTheBackend_AndEveryEntryDropsIntoAnAction()
    {
        // Arrange.
        var toolbox = new CausalLoopToolboxProvider();

        // Act & assert.
        Assert.NotEmpty(toolbox.Items);
        Assert.Equal(ServiceCollectionAddCausalLoopExtension.CausalLoopOrigin, toolbox.Origin);

        // Each entry names an action the provider actually offers, so a drop inherits that
        // action's command, refusals and undo rather than having an implementation of its own.
        var known = new[]
        {
            CausalLoopContextActionProvider.AddVariableActionId,
            CausalLoopContextActionProvider.AddLinkActionId,
            CausalLoopContextActionProvider.AddLoopActionId,
        };
        Assert.All(toolbox.Items, item =>
        {
            ArgumentNullException.ThrowIfNull(item);

            Assert.Contains(item.DropActionId, known);
            Assert.NotEqual("", item.Label);
            // The description says where the entry goes, so a user finds out before trying.
            Assert.Contains("Drop on", item.Description, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void TheProvidersAreRegistered()
    {
        // Act & assert.
        Assert.NotEmpty(_provider.GetServices<IContextActionProvider>());
        Assert.Equal(
            ServiceCollectionAddCausalLoopExtension.CausalLoopOrigin,
            Assert.Single(_provider.GetServices<IDiagramToolboxProvider>()).Origin);
    }
}
