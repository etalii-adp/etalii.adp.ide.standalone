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
    /// <summary>One reader for each processor, at least two, so that none waits for a thread a spinning reader holds.</summary>
    private static readonly int Readers = Math.Clamp(Environment.ProcessorCount, 2, 8);

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

            // Act.
            var found = await AtOnce(_ => FindLast(diagram));

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

            // Act.
            var counts = await AtOnce(reader => CountOf(diagram, reader));

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

    /// <summary>What <paramref name="read"/> gives on each of the readers, all of them let go at once.</summary>
    private static Task<T[]> AtOnce<T>(Func<int, T> read)
    {
        var start = new StartLine(Readers);
        return Task.WhenAll(Enumerable.Range(0, Readers).Select(reader => Task.Run(() => start.Released(reader, read), TestContext.Current.CancellationToken)));
    }

    private static string? FindLast(DislDiagram diagram) => diagram.ElementById($"n{Nodes - 1}")?.Id;

    /// <summary>The stations, asked for by every reader at once, each reader also asking for a type of its own so that the lists are made side by side.</summary>
    private static int CountOf(DislDiagram diagram, int reader)
    {
        _ = diagram.NodesOfType(reader % 2 == 0 ? "Halt" : "Station");
        return diagram.NodesOfType("Station").Count;
    }

    /// <summary>Holds every reader until all of them are there, so that they ask at the same moment.</summary>
    private sealed class StartLine(int readers)
    {
        private int _arrived;

        public T Released<T>(int reader, Func<int, T> read)
        {
            Interlocked.Increment(ref _arrived);
            while (Volatile.Read(ref _arrived) < readers) Thread.SpinWait(20);
            return read(reader);
        }
    }
}
