using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests.Parity;

/// <summary>
/// A connect is refused as the hand-written verdict refused it (functional-decomposition-graph-disl-fbl
/// task 7, Requirements 3.2 and 3.6): the type, cardinality and cycle checks in that order, worded by the
/// definition, and the two refusals that are not checks, for every document of the parity corpus, every
/// relation and one it does not have, and every ordered pair of its element ids and an id it does not hold.
/// </summary>
public class FdgDerivedRefusalsTests
{
    public static TheoryData<string> Documents() => FdgCorpus.Documents();

    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryRefusal_IsTheHandWrittenOne(string path)
    {
        // Arrange.
        (DocumentEntry<LineDocument, FdgModel> readable, _) = FdgCorpus.Entries(path);
        var model = readable.Model;
        List<string> ids = [.. model.Elements.Select(element => element.Id).Distinct(StringComparer.Ordinal), "parity:ghost"];
        List<string> differences = [];

        // Act.
        foreach (var relation in FdgConnectionTypes.All.Append("not-a-relation"))
        {
            foreach (var from in ids)
            {
                foreach (var to in ids)
                {
                    var expected = FdgConnectVerdict.RefusalFor(model, relation, from, to);
                    var actual = ConnectFdgElementsCommandHandler.RefusalOf(model, relation, from, to);
                    if (expected != actual) differences.Add($"{relation} {from} -> {to}: expected {expected ?? "allowed"}, actual {actual ?? "allowed"}");
                }
            }
        }

        // Assert.
        Assert.True(differences.Count == 0, $"{path}:\n{string.Join("\n", differences.Take(20))}");
    }
}
