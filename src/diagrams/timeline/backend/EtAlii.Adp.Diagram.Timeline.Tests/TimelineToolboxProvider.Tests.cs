using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Context;
using EtAlii.Adp.History;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// The palette carries data and names actions; what matters is the join - that every entry
/// names an action that exists, and that a drop lands where it was dropped.
/// </summary>
public class TimelineToolboxProviderTests : IDisposable
{
    private readonly string _workspace = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-timeline-toolbox-" + Guid.NewGuid().ToString("N"));

    private readonly TimelineDocumentStore _store = new();
    private readonly TimelineToolboxProvider _toolbox = new();
    private readonly TimelineContextActionProvider _actions;

    public TimelineToolboxProviderTests()
    {
        Directory.CreateDirectory(_workspace);
        _actions = new TimelineContextActionProvider(new HistoryStackStore(new TimelineTestDispatcher(_store)), _store);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_workspace);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ItDescribesTheElementAndTheMoment()
    {
        // Assert.
        Assert.Equal(["Element", "Moment"], _toolbox.Items.Select(item => item.Label));
    }

    [Fact]
    public void ItAnswersForThisDiagramType()
    {
        // Assert.
        // Resolved by origin; a mismatch is an empty palette rather than an error anybody sees.
        Assert.Equal(Diagram.Timeline.Origin, _toolbox.Origin);
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
    public void EveryEntryNamesAnActionThatExists()
    {
        // Arrange.
        // The join between the two providers is a string, and a typo in it is a toolbox entry
        // that silently does nothing when dropped.
        var known = typeof(TimelineContextActionProvider)
            .GetFields()
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        // Assert.
        Assert.All(_toolbox.Items, item => Assert.Contains(item.DropActionId, known));
    }

    [Fact]
    public async Task ADrop_CreatesTheElementAtTheDroppedTimeAndRow()
    {
        // Arrange.
        // The whole reason the entry names an action: the drop reuses the add command, carrying
        // its position, so there is no second implementation to disagree with the menu.
        var path = IoPath.Combine(_workspace, "plan.tml");
        await File.WriteAllTextAsync(path, "timeline: 1\r\nelements: []\r\n", TestContext.Current.CancellationToken);
        _store.Forget(path);
        var item = _toolbox.Items.Single(candidate => candidate.Id == "timeline.toolbox.element");
        var seconds = TimelineScale.ToSeconds(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
        var target = new ContextTarget(
            ContextScope.DiagramElement, path, IsContainer: false, SourceId: default, _workspace,
            ShortGuid.NewShortGuid(), TimelineNewPlacement.IdFor(seconds, 3));

        // Act: the drop names a placement, and the element appears there with nothing asked.
        var result = await _actions.ExecuteAsync(target, item.DropActionId, CancellationToken.None);

        // Assert.
        Assert.IsType<ContextExecutionCompleted>(result);
        var added = _store.GetOrLoad(path).Model.Elements.Single();
        Assert.Equal("2026-09-01", added.Begin.Text);
        Assert.Equal(3, added.Row);
        Assert.True(added.IsPeriod);
    }
}
