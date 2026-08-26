using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Context;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

/// <summary>
/// The palette: what it offers, what it deliberately does not, and that every entry's drop
/// really creates something (Requirement 13).
/// </summary>
public sealed class WardleyToolboxProviderTests : IDisposable
{
    private readonly string _root = IoPath.Combine(IoPath.GetTempPath(), $"wardley-toolbox-{Guid.NewGuid():N}");
    private readonly ServiceProvider _services;
    private readonly IWardleyDocumentStore _documents;
    private readonly WardleyToolboxProvider _toolbox = new();
    private readonly WardleyContextActionProvider _actions;
    private readonly string _path;

    public WardleyToolboxProviderTests()
    {
        Directory.CreateDirectory(_root);
        _services = new ServiceCollection().AddCommands().AddWardleyMap().BuildServiceProvider();
        _documents = _services.GetRequiredService<IWardleyDocumentStore>();
        _actions = _services.GetServices<IContextActionProvider>().OfType<WardleyContextActionProvider>().Single();
        _path = IoPath.Combine(_root, "map.owm");
        File.WriteAllText(_path, "title Empty\n");
    }

    public void Dispose()
    {
        _services.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A scratch folder that outlives the test is untidy, never a failure.
        }
    }

    [Fact]
    public void ItIsKeyedByThisModulesOrigin()
    {
        // Arrange, act and assert. Requirement 13.1 - core resolves it by origin and never
        // learns what a Wardley map is.
        Assert.Equal(Diagram.WardleyMap.Origin, _toolbox.Origin);
    }

    [Fact]
    public void ItOffersWhatRequirement132Asks()
    {
        // Act.
        var labels = _toolbox.Items.Select(item => item.Label).ToArray();

        // Assert.
        Assert.Equal(
            ["Component", "Anchor", "Market", "Ecosystem", "Submap", "Pipeline", "Note", "Annotation"],
            labels);
    }

    [Fact]
    public void ThereIsNoLinkEntry()
    {
        // Assert. Requirement 13.6 - a link needs two endpoints and a drop has one, so it is
        // created through the context action instead.
        Assert.DoesNotContain(_toolbox.Items, item => item.Label.Contains("Link", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(_actions);
    }

    [Fact]
    public void EveryEntryIsDescribed()
    {
        // Assert. The palette shows a label, an icon and a hint it does not write itself, so a
        // blank one is a blank row in a panel nobody can fix from the client.
        foreach (var item in _toolbox.Items)
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Id), item.Label);
            Assert.False(string.IsNullOrWhiteSpace(item.Icon), item.Label);
            Assert.False(string.IsNullOrWhiteSpace(item.Description), item.Label);
            Assert.False(string.IsNullOrWhiteSpace(item.DropActionId), item.Label);
        }

        Assert.Equal(_toolbox.Items.Count, _toolbox.Items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task EveryDropActionIdResolvesToAnActionThisModuleActuallyContributes()
    {
        // Arrange. Requirement 13.3 - an entry naming an action nothing implements is a palette
        // whose drops fail, which is precisely what happened to C4 before its adds landed.
        var onTheMap = await OfferedIds(elementId: "");

        await Execute(WardleyContextActionProvider.AddComponentActionId, "Kettle");
        var onAComponent = await OfferedIds(IdOf("Kettle"));

        var offered = onTheMap.Concat(onAComponent).ToHashSet(StringComparer.Ordinal);

        // Act and assert.
        foreach (var item in _toolbox.Items)
        {
            Assert.True(offered.Contains(item.DropActionId), $"'{item.Label}' names an action nothing contributes: {item.DropActionId}");
        }
    }

    [Fact]
    public async Task DroppingAComponentEntry_WritesOneComponent()
    {
        // Act.
        var result = await Execute(WardleyContextActionProvider.AddComponentActionId, "Kettle");

        // Assert.
        Assert.True(result.Completed, result.Error);
        Assert.Equal("title Empty\ncomponent Kettle [0.5, 0.5]\n", File.ReadAllText(_path));
    }

    [Fact]
    public async Task DroppingAMarketEntry_WritesAComponentCarryingTheDecorator()
    {
        // Act. Requirement 13.2 - a market is a component with `(market)`, not a kind of its own.
        var result = await Execute(WardleyContextActionProvider.AddMarketActionId, "Tea Buyers");

        // Assert.
        Assert.True(result.Completed, result.Error);
        Assert.Equal("title Empty\ncomponent Tea Buyers [0.5, 0.5] (market)\n", File.ReadAllText(_path));
    }

    [Fact]
    public async Task DroppingAMarketEntry_IsOneUndo()
    {
        // Arrange. Written in one command precisely so that the decorator does not arrive as a
        // second history entry the user has to undo twice.
        var before = File.ReadAllText(_path);

        // Act.
        await Execute(WardleyContextActionProvider.AddMarketActionId, "Tea Buyers");
        await _services.GetRequiredService<IHistoryStackStore>().Get(_root).UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(before, File.ReadAllText(_path));
    }

    [Fact]
    public async Task DroppingAnEcosystemEntry_WritesTheOtherDecorator()
    {
        // Act.
        await Execute(WardleyContextActionProvider.AddEcosystemActionId, "Plugins");

        // Assert.
        Assert.Contains("(ecosystem)", File.ReadAllText(_path), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DroppingAnAnchorEntry_WritesTheUserNeed()
    {
        // Act.
        await Execute(WardleyContextActionProvider.AddAnchorActionId, "Business");

        // Assert.
        Assert.Contains("anchor Business [0.5, 0.5]", File.ReadAllText(_path), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DroppingANoteEntry_PinsTheText()
    {
        // Act.
        var result = await Execute(WardleyContextActionProvider.AddNoteActionId, "Mind the gap");

        // Assert.
        Assert.True(result.Completed, result.Error);
        Assert.Equal("title Empty\nnote Mind the gap [0.5, 0.5]\n", File.ReadAllText(_path));
    }

    [Fact]
    public async Task DroppingAnAnnotationEntry_NumbersItPastTheHighestAlreadyThere()
    {
        // Arrange. A map whose annotation 2 was deleted still has a 3, and reusing that number
        // would put two of them on the map.
        File.WriteAllText(_path, "annotation 1 [0.2, 0.2] first\nannotation 3 [0.3, 0.3] third\n");
        _documents.Forget(_path);

        // Act.
        await Execute(WardleyContextActionProvider.AddAnnotationActionId, "fourth");

        // Assert.
        Assert.Contains("annotation 4 [0.5, 0.5] fourth", File.ReadAllText(_path), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DroppingAnAnnotationEntry_StartsAtOneOnAMapWithNone()
    {
        // Act.
        await Execute(WardleyContextActionProvider.AddAnnotationActionId, "the first thing to say");

        // Assert.
        Assert.Contains("annotation 1 [0.5, 0.5] the first thing to say", File.ReadAllText(_path), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANoteOrAnnotationWithNothingToSay_IsRefused()
    {
        // Act. A note's text is also the only handle its identity has.
        var note = await _actions.ValidateAsync(
            Target(""), WardleyContextActionProvider.AddNoteActionId, "   ", TestContext.Current.CancellationToken);
        var committed = await Execute(WardleyContextActionProvider.AddNoteActionId, "   ");

        // Assert.
        Assert.False(note.Valid);
        Assert.Contains("something to say", note.Reason, StringComparison.Ordinal);
        Assert.False(committed.Completed);
        Assert.Equal("title Empty\n", File.ReadAllText(_path));
    }

    [Fact]
    public async Task DroppingThePipelineEntry_AsksForTheFirstComponentToPutInIt()
    {
        // Arrange. A pipeline belongs to a component, so its entry drops onto one.
        await Execute(WardleyContextActionProvider.AddComponentActionId, "Kettle");

        // Act.
        var asked = await _actions.ExecuteAsync(
            Target(IdOf("Kettle")), WardleyContextActionProvider.AddToPipelineActionId, TestContext.Current.CancellationToken);
        var committed = await _actions.CommitAsync(
            Target(IdOf("Kettle")),
            WardleyContextActionProvider.AddToPipelineActionId,
            "Electric Kettle",
            "",
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.IsType<ContextExecutionRequiresInput>(asked);
        Assert.True(committed.Completed, committed.Error);
        Assert.Contains("pipeline Kettle", File.ReadAllText(_path), StringComparison.Ordinal);
    }

    // ---- plumbing -----------------------------------------------------------------------------

    private string IdOf(string name) => _documents
        .Identities(_path)
        .Single(entry => entry.Kind == WardleyIdentityKind.Component && entry.Key == name)
        .Id;

    private ContextTarget Target(string elementId) => new(
        ContextScope.DiagramElement, _path, IsContainer: false, SourceId: default, _root, default, elementId);

    private async Task<IReadOnlyList<string>> OfferedIds(string elementId)
    {
        var groups = await _actions.DiscoverAsync(Target(elementId), TestContext.Current.CancellationToken);
        return groups.SelectMany(group => group.Actions).Select(action => action.Id).ToArray();
    }

    private Task<ContextCommitResult> Execute(string actionId, string value) =>
        _actions.CommitAsync(Target(""), actionId, value, "", TestContext.Current.CancellationToken).AsTask();
}
