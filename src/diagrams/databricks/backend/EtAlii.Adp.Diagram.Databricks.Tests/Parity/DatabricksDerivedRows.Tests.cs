using EtAlii.Adp.Context;
using Xunit;

namespace EtAlii.Adp.Diagram.Databricks.Tests.Parity;

/// <summary>
/// The property rows derived from the three DISL definitions equal the hand-written ones (databricks-disl-fbl task 6,
/// Requirements 3.1, 3.2 and 3.5), row for row, read-only reasons and the two rows every selection wears included: for
/// every document of the parity corpus, every reading the transcript records, and every element id that reading draws,
/// the empty id, a placement, two connect gestures and an unknown id, readable and read-only.
/// </summary>
public class DatabricksDerivedRowsTests
{
    public static TheoryData<string> Documents() => DatabricksParityCorpus.Documents();

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryRow_IsTheHandWrittenOne(string document)
    {
        // Arrange.
        using var corpus = new DatabricksParityCorpus();
        var readable = await DatabricksParityCorpus.EntryAsync(document, TestContext.Current.CancellationToken);
        var readOnly = readable with { Unreadable = "parity: read-only" };
        var compared = 0;

        // Act and assert.
        foreach ((string label, ToolOrigin origin, string? registration) in await corpus.OpeningsAsync(readable.Model, TestContext.Current.CancellationToken))
        {
            foreach (var elementId in DatabricksParityCorpus.Targets(readable.Model, origin, registration))
            {
                var target = DatabricksParityCorpus.Target(elementId, origin, registration);
                foreach (var entry in new[] { readable, readOnly })
                {
                    var expected = HandWrittenDatabricks.Rows(target, entry).Select(Text).ToList();
                    var actual = DatabricksDefinition.Rows(target, entry).Select(Text).ToList();
                    Assert.True(
                        expected.SequenceEqual(actual, StringComparer.Ordinal),
                        $"{document} | {label} | {elementId} (read-only: {!entry.IsUsable}):\nexpected {string.Join("\n         ", expected)}\nactual   {string.Join("\n         ", actual)}");
                    compared += expected.Count;
                }
            }
        }

        Assert.True(compared > 0, $"{document}: no row had anything to compare.");
    }

    private static string Text(ContextPropertyDefinition row) =>
        $"{row.Id} | {row.Label} | {row.Value.Replace("\n", "\\n", StringComparison.Ordinal)} | {row.Editor} | {row.ReadOnlyReason} | {row.Group} | {(row.Candidates is null ? "null" : "[" + string.Join("; ", row.Candidates) + "]")}";
}
