using System.Text;
using EtAlii.Adp.Diagram.DependencyGraph.Tests.Parity.Oracle;
using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests.Parity;

/// <summary>
/// The cross-check of Requirement 1.4, against the oracle: for every <c>.dgr</c> file of the repository, the nodes and
/// dependencies the module's binding reads, each by the id it is written with, are the ones the parser the module used
/// to read with reads. It moved here from <c>EtAlii.Adp.Specification.Fbl.Tests</c> with the parser (dependency-graph-
/// disl-fbl, task 8), as the design's amendment rules, so no test project references another.
/// </summary>
public class DependencyGraphCrossCheckTests
{
    private static readonly DependencyGraphTranscript _transcript = new();

    /// <summary>How many <c>.dgr</c> files the repository held when this was written.</summary>
    private const int Minimum = 13;

    public static TheoryData<string> Files()
    {
        var data = new TheoryData<string>();
        foreach ((string name, _) in _transcript.Corpus().Where(document => document.Name.EndsWith(".dgr", StringComparison.Ordinal))) data.Add(name);
        return data;
    }

    [Theory]
    [MemberData(nameof(Files))]
    public async Task TheBindingReadsTheIdsTheOracleReads(string name)
    {
        // Arrange.
        var bytes = await _transcript.Corpus().Single(document => document.Name == name).Read(TestContext.Current.CancellationToken);
        var text = Encoding.UTF8.GetString(bytes);

        // Act.
        var body = DependencyGraphBody.Parse(text);
        var oracle = DependencyGraphParser.Parse(LineDocument.Parse(text));

        // Assert.
        Assert.False(body.IsUnreadable);
        Assert.Equal(Ids(oracle.Elements.Select(node => node.Id)), Ids(Written(body, "Node")));
        Assert.Equal(Ids(oracle.Relations.Select(dependency => dependency.Id)), Ids(Written(body, "Dependency")));
    }

    [Fact]
    public void TheCrossCheck_TakesEveryDgrFileOfTheRepository()
    {
        // Act.
        var files = Files().Count;

        // Assert: the cross-check above runs over all of them, so a broken enumeration fails here rather than passing on none.
        Assert.True(files >= Minimum, $"{files} .dgr files were found; at least {Minimum} were there when this was written.");
    }

    private static IEnumerable<string> Written(DependencyGraphBody body, string type) =>
        body.Model.Elements.Where(element => element.Type == type).Select(element => DependencyGraphReading.TextOf(element, "storedId"));

    private static SortedSet<string> Ids(IEnumerable<string> ids) => new(ids.Where(id => id.Length > 0), StringComparer.Ordinal);
}
