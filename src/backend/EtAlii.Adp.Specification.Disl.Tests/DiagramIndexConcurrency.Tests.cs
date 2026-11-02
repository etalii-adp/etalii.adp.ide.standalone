using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// A diagram that is only read may be read from several threads at once: a host keeps one reading of a body for every
/// connection that shows it, and C4's body keeps one per text for every reader of that text. What it keeps between two
/// changes, the element of each id and the elements of each type, is then first made by whichever thread asks first,
/// while the others read it.
/// </summary>
public class DiagramIndexConcurrencyTests
{
    private const int Rounds = 50;
    private const int Readers = 8;
    private const int Nodes = 2000;

    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "metamodel": {
          "types": { "Station": {}, "Halt": { "extends": "Station" } } }
        """));

    [Fact]
    public async Task TheElementOfAnId_IsFound_WhenSeveralThreadsAskFirstAtOnce()
    {
        for (var round = 0; round < Rounds; round++)
        {
            // Arrange.
            var diagram = Station();
            using var start = new Barrier(Readers);

            // Act.
            var found = await Task.WhenAll(Enumerable.Range(0, Readers).Select(_ => Task.Run(() => FindLast(diagram, start), TestContext.Current.CancellationToken)));

            // Assert.
            Assert.All(found, id => Assert.Equal($"n{Nodes - 1}", id));
        }
    }

    [Fact]
    public async Task TheNodesOfAType_AreAllOfThem_WhenSeveralThreadsAskFirstAtOnce()
    {
        for (var round = 0; round < Rounds; round++)
        {
            // Arrange.
            var diagram = Station();
            using var start = new Barrier(Readers);

            // Act.
            var counts = await Task.WhenAll(Enumerable.Range(0, Readers).Select(reader => Task.Run(() => CountOf(diagram, start, reader), TestContext.Current.CancellationToken)));

            // Assert.
            Assert.All(counts, count => Assert.Equal(Nodes, count));
        }
    }

    private static DislDiagram Station()
    {
        var diagram = new DislDiagram(Specification);
        for (var node = 0; node < Nodes; node++) diagram.AddNode(node % 2 == 0 ? "Station" : "Halt", $"n{node}");
        return diagram;
    }

    private static string? FindLast(DislDiagram diagram, Barrier start)
    {
        start.SignalAndWait(TestContext.Current.CancellationToken);
        return diagram.ElementById($"n{Nodes - 1}")?.Id;
    }

    /// <summary>The stations, asked for by every reader at once, each reader also asking for a type of its own so that the lists are made side by side.</summary>
    private static int CountOf(DislDiagram diagram, Barrier start, int reader)
    {
        start.SignalAndWait(TestContext.Current.CancellationToken);
        _ = diagram.NodesOfType(reader % 2 == 0 ? "Halt" : "Station");
        return diagram.NodesOfType("Station").Count;
    }
}
