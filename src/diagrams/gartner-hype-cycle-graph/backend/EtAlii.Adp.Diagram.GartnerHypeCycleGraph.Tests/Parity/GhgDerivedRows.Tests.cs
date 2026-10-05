using EtAlii.Adp.Context;
using Xunit;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests.Parity;

/// <summary>
/// The property rows derived from the DISL definition equal the hand-written ones (runtime plan step
/// S11), row for row and candidate for candidate: for every document of the parity corpus, every
/// element id, the empty id and an unknown id, readable and read-only.
/// </summary>
public class GhgDerivedRowsTests
{
    public static TheoryData<string> Documents() => [.. GhgDisl.Texts().Select(document => document.Path)];

    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryRow_IsTheHandWrittenOne(string path)
    {
        // Arrange.
        var readable = GhgDocumentEntry.Read(GhgDisl.Texts().Single(document => document.Path == path).Text);
        var readOnly = readable with { Unreadable = "parity: read-only" };

        // Act and assert.
        foreach (var target in Targets(readable.Model))
        {
            foreach (var entry in new[] { readable, readOnly })
            {
                var expected = HandWrittenGhg.Rows(entry, target).Select(Text).ToList();
                var actual = GhgDefinition.Rows(entry, target).Select(Text).ToList();
                Assert.True(
                    expected.SequenceEqual(actual),
                    $"{path} {target} (read-only: {!entry.IsUsable}):\nexpected {string.Join("\n         ", expected)}\nactual   {string.Join("\n         ", actual)}");
            }
        }
    }

    /// <summary>The targets the parity transcript records rows for.</summary>
    private static IEnumerable<string> Targets(GhgModel model) =>
        model.Trends.Select(trend => trend.Id)
            .Concat(model.Triggers.Select(trigger => trigger.Id))
            .Concat(model.Notes.Select(note => note.Id))
            .Concat(model.Influences.Select(influence => influence.Id))
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Concat(["", "parity:unknown"]);

    private static string Text(ContextPropertyDefinition row) =>
        $"{row.Id} | {row.Label} | {row.Value.Replace("\n", "\\n", StringComparison.Ordinal)} | {row.Editor} | {row.ReadOnlyReason} | {row.Group} | {(row.Candidates is null ? "null" : "[" + string.Join("; ", row.Candidates) + "]")}";
}
