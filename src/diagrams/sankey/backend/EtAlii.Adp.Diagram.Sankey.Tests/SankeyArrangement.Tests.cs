using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using Xunit;

namespace EtAlii.Adp.Diagram.Sankey.Tests;

/// <summary>
/// "Arrange diagram" reorders the nodes within their columns so bands cross as little as their
/// values allow, through the real store on a real file, and its undo gives back the original bytes.
/// </summary>
public sealed class SankeyArrangementTests : IDisposable
{
    /// <summary>Two sources whose bands cross on their way to two ends listed the other way round.</summary>
    private const string Crossing =
        "sankey: 1\n" +
        "nodes:\n" +
        "  - id: a\n" +
        "    name: Source A\n" +
        "  - id: b\n" +
        "    name: Source B\n" +
        "  - id: x\n" +
        "    name: End X\n" +
        "  - id: y\n" +
        "    name: End Y\n" +
        "flows:\n" +
        "  - from: a\n" +
        "    to: y\n" +
        "    value: 6\n" +
        "  - from: b\n" +
        "    to: x\n" +
        "    value: 4\n";

    private readonly SankeyTestFolder _folder = new(Path.Combine(AppContext.BaseDirectory, "Fixtures", "lf-line-endings.skv"), "crossing.skv");
    private readonly SankeyDocumentStore _store = new();
    private readonly SankeyTestDispatcher _dispatcher;

    public SankeyArrangementTests()
    {
        File.WriteAllText(_folder.Body, Crossing);
        _dispatcher = new SankeyTestDispatcher(_store);
    }

    public void Dispose() => _folder.Dispose();

    public static TheoryData<string> EveryExample => [SankeyExamples.BrightwaterCoffee, SankeyExamples.UkEnergy, SankeyExamples.RecentGraduates];

    [Fact]
    public async Task ArrangingTwoCrossingBands_UncrossesThem_AndTheUndoGivesTheOriginalBytes()
    {
        // Arrange.
        var original = await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken);
        Assert.True(SankeyArrangement.CrossingsOf(Parse(_folder.Body)) > 0);

        // Act.
        var result = await _dispatcher.DispatchAsync(new ArrangeSankeyCommand(_folder.Body), TestContext.Current.CancellationToken);

        // Assert: nothing crosses, and nothing but the order changed.
        Assert.True(result.IsSuccess, result.Error);
        var model = Parse(_folder.Body);
        Assert.Empty(model.Problems);
        Assert.Equal(0, SankeyArrangement.CrossingsOf(model));
        Assert.Equal(["a", "b", "x", "y"], model.Nodes.Select(node => node.Id).Order(StringComparer.Ordinal));
        Assert.Equal(["a->y", "b->x"], model.Flows.Select(flow => flow.Id));

        // Act: the undo.
        var undone = await _dispatcher.DispatchAsync(Assert.IsAssignableFrom<ICommand>(result.Inverse), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Equal(original, await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ArrangingAnArrangedDiagram_IsRefused_AndWritesNothing()
    {
        // Arrange.
        var first = await _dispatcher.DispatchAsync(new ArrangeSankeyCommand(_folder.Body), TestContext.Current.CancellationToken);
        Assert.True(first.IsSuccess, first.Error);
        var arranged = await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken);

        // Act.
        var second = await _dispatcher.DispatchAsync(new ArrangeSankeyCommand(_folder.Body), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(second.IsSuccess);
        Assert.Contains("already arranged", second.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(arranged, await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(EveryExample))]
    public async Task ArrangingAnExample_NeverAddsCrossings_AndKeepsEveryNodeAndFlow(string example)
    {
        // Arrange.
        using var folder = new SankeyTestFolder(example, Path.GetFileName(example));
        var before = Parse(folder.Body);

        // Act: an example already free of crossings is refused, which is also an arrangement that adds none.
        await _dispatcher.DispatchAsync(new ArrangeSankeyCommand(folder.Body), TestContext.Current.CancellationToken);

        // Assert.
        var after = Parse(folder.Body);
        Assert.Empty(after.Problems);
        Assert.True(SankeyArrangement.CrossingsOf(after) <= SankeyArrangement.CrossingsOf(before));
        Assert.Equal(before.Nodes.Select(node => node.Id).Order(StringComparer.Ordinal), after.Nodes.Select(node => node.Id).Order(StringComparer.Ordinal));
        Assert.Equal(before.Flows.Select(flow => (flow.Id, flow.Value)), after.Flows.Select(flow => (flow.Id, flow.Value)));
    }

    private static SankeyModel Parse(string path) => SankeyParser.Parse(LineDocument.Parse(File.ReadAllText(path)));
}
