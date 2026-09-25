using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// The palette carries data and names actions; what matters is the join - that every entry
/// names an action that exists, and that a drop lands where it was dropped.
/// </summary>
public class DependencyGraphToolboxProviderTests : IDisposable
{
    private readonly string _workspace = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-dgr-toolbox-" + Guid.NewGuid().ToString("N"));

    private readonly DependencyGraphDocumentStore _store = new();
    private readonly DependencyGraphToolboxProvider _toolbox = new();
    private readonly DependencyGraphContextActionProvider _actions;

    public DependencyGraphToolboxProviderTests()
    {
        Directory.CreateDirectory(_workspace);
        _actions = new DependencyGraphContextActionProvider(
            new HistoryStackStore(new DependencyGraphTestDispatcher(_store)), _store);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_workspace);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ItDescribesTheNodeAndNothingElse()
    {
        // Assert.
        // One entry, not the timeline's two: a Moment is a point in time and there are none here,
        // so the entry is deleted rather than renamed into something this type does not have.
        Assert.Equal(["Node"], _toolbox.Items.Select(item => item.Label));
    }

    [Fact]
    public void ItAnswersForThisDiagramType()
    {
        // Assert.
        // Resolved by origin; a mismatch is an empty palette rather than an error anybody sees.
        Assert.Equal(Diagram.DependencyGraph.Origin, _toolbox.Origin);
    }

    [Fact]
    public void EveryEntryCarriesOnlyData_AndAllOfIt()
    {
        // Assert.
        Assert.All(_toolbox.Items, item =>
        {
            ArgumentNullException.ThrowIfNull(item);
            Assert.NotEmpty(item.Id);
            Assert.NotEmpty(item.Label);
            Assert.NotEmpty(item.Icon);
            Assert.NotEmpty(item.Description);
            Assert.NotEmpty(item.DropActionId);
        });
    }

    [Fact]
    public void NoEntryDescribesATime()
    {
        // Assert.
        // The Moment's description named a point in time; a forked entry that kept that wording
        // would be the palette promising something the document cannot hold.
        Assert.All(_toolbox.Items, item =>
        {
            foreach (var word in new[] { "time", "date", "moment", "begin", "duration" })
            {
                Assert.DoesNotContain(word, item.Description, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(word, item.Label, StringComparison.OrdinalIgnoreCase);
            }
        });
    }

    [Fact]
    public void EveryEntryNamesAnActionThatExists()
    {
        // Arrange.
        // The join between the two providers is a string, and a typo in it is a toolbox entry
        // that silently does nothing when dropped.
        var known = typeof(DependencyGraphContextActionProvider)
            .GetFields()
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        // Assert.
        Assert.All(_toolbox.Items, item => Assert.Contains(item.DropActionId, known));
    }

    [Fact]
    public async Task ADrop_CreatesTheNodeAtTheDroppedCoordinateAndRow()
    {
        // Arrange.
        // The whole reason the entry names an action: the drop reuses the add command, carrying
        // its position, so there is no second implementation to disagree with the menu.
        var path = IoPath.Combine(_workspace, "services.dgr");
        await File.WriteAllTextAsync(path, "dependencies: 1\r\nelements: []\r\n", TestContext.Current.CancellationToken);
        _store.Forget(path);
        var item = _toolbox.Items.Single(candidate => candidate.Id == "dependencies.toolbox.node");
        var target = new ContextTarget(
            ContextScope.DiagramElement, path, IsContainer: false, SourceId: default, _workspace,
            ShortGuid.NewShortGuid(), DependencyGraphNewPlacement.IdFor(612.5, 3));

        // Act: the drop names a placement, and the node appears there with nothing asked.
        var result = await _actions.ExecuteAsync(target, item.DropActionId, CancellationToken.None);

        // Assert.
        Assert.IsType<ContextExecutionCompleted>(result);
        var added = _store.GetOrLoad(path).Model.Elements.Single();
        Assert.Equal(612.5d, added.X);
        Assert.Equal(3, added.Row);
    }
}
