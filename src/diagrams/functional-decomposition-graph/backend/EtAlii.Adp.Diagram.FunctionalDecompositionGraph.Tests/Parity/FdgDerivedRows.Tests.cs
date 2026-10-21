using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests.Parity;

/// <summary>
/// The property rows derived from the DISL definition equal the hand-written ones
/// (functional-decomposition-graph-disl-fbl task 6, Requirement 3.8), row for row: for every document
/// of the parity corpus and every target its transcript records, readable and read-only.
/// </summary>
public class FdgDerivedRowsTests
{
    public static TheoryData<string> Documents() => FdgCorpus.Documents();

    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryRow_IsTheHandWrittenOne(string path)
    {
        // Arrange.
        (DocumentEntry<LineDocument, FdgModel> readable, DocumentEntry<LineDocument, FdgModel> readOnly) = FdgCorpus.Entries(path);

        // Act and assert.
        foreach (var target in FdgCorpus.Targets(readable.Model))
        {
            foreach (var entry in new[] { readable, readOnly })
            {
                var expected = HandWrittenFdg.Rows(entry, target).Select(Text).ToList();
                var actual = FdgDefinition.Rows(entry, target).Select(Text).ToList();
                Assert.True(
                    expected.SequenceEqual(actual),
                    $"{path} {target} (read-only: {!entry.IsUsable}):\nexpected {string.Join("\n         ", expected)}\nactual   {string.Join("\n         ", actual)}");
            }
        }
    }

    private static string Text(ContextPropertyDefinition row) =>
        $"{row.Id} | {row.Label} | {row.Value.Replace("\n", "\\n", StringComparison.Ordinal)} | {row.Editor} | {row.ReadOnlyReason} | {row.Group} | {(row.Candidates is null ? "null" : "[" + string.Join("; ", row.Candidates) + "]")}";
}
