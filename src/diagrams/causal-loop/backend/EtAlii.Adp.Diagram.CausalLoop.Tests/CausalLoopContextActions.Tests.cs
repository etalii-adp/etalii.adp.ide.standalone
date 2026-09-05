using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Context;
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
    private readonly CausalLoopContextActionProvider _actions;

    public CausalLoopContextActionsTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _path = IoPath.Combine(_root, "feedback.cld");
        File.WriteAllText(_path, Corpus);

        _provider = new ServiceCollection().AddCommands().AddCausalLoop().BuildServiceProvider();
        _actions = new CausalLoopContextActionProvider(_store, _provider.GetRequiredService<IHistoryStackStore>());
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
    public async Task APlacement_OffersANewVariable()
    {
        // Act.
        var actions = await Discover(CausalLoopSelection.PlacementFor(120, 240));

        // Assert.
        Assert.Equal(
            CausalLoopContextActionProvider.AddVariableActionId,
            Assert.Single(actions).Id);
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

        File.WriteAllText(_path, Corpus.Replace("link population -> births +", "link population -> births + delayed", StringComparison.Ordinal));
        _store.Reload(_path);

        var after = Assert.Single(
            await Discover("link:population|births"),
            action => action.Id == CausalLoopContextActionProvider.ToggleDelayActionId);

        // Assert.
        Assert.Equal("Delayed", before.Label);
        Assert.Equal("Not delayed", after.Label);
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
        // Act.
        var result = await _actions.ValidateAsync(
            Target(CausalLoopSelection.PlacementFor(0, 0)),
            CausalLoopContextActionProvider.AddVariableActionId,
            "two words",
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.Valid);
        Assert.Equal(CausalLoopWriter.UnusableName, result.Reason);
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
        Assert.Contains("link population -> births -", File.ReadAllText(_path), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddingALink_AsksForTheOtherEnd_ThenWritesIt()
    {
        // Arrange.
        File.WriteAllText(_path, Corpus + "variable deaths \"Deaths\"\r\n");
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
        Assert.Contains("link population -> deaths +", File.ReadAllText(_path), StringComparison.Ordinal);
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
