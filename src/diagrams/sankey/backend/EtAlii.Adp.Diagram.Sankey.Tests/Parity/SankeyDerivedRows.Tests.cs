using EtAlii.Adp.Context;
using Xunit;

namespace EtAlii.Adp.Diagram.Sankey.Tests.Parity;

/// <summary>
/// The property rows derived from the DISL definition equal the hand-written ones (sankey-disl-fbl task 6), row for
/// row and candidate for candidate: for every document of the parity corpus, every node and flow id, the empty id, a
/// placement, a connect gesture and an unknown id.
/// </summary>
public class SankeyDerivedRowsTests
{
    public static TheoryData<string> Documents() => SankeyCorpus.Names();

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryRow_IsTheHandWrittenOne(string name)
    {
        // Arrange.
        var entry = await SankeyCorpus.ReadAsync(name, TestContext.Current.CancellationToken);

        // Act and assert.
        foreach (var target in SankeyCorpus.Targets(entry.Model))
        {
            var expected = HandWrittenSankey.Rows(entry, target).Select(Text).ToList();
            var actual = SankeyDefinition.Rows(entry, target).Select(Text).ToList();
            Assert.True(
                expected.SequenceEqual(actual),
                $"{name} {target}:\nexpected {string.Join("\n         ", expected)}\nactual   {string.Join("\n         ", actual)}");
        }
    }

    private static string Text(ContextPropertyDefinition row) =>
        $"{row.Id} | {row.Label} | {row.Value.Replace("\n", "\\n", StringComparison.Ordinal)} | {row.Editor} | {row.ReadOnlyReason} | {row.Group} | {(row.Candidates is null ? "null" : "[" + string.Join("; ", row.Candidates) + "]")}";
}
