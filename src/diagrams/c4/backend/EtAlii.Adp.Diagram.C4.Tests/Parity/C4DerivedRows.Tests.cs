using EtAlii.Adp.Context;
using Xunit;

namespace EtAlii.Adp.Diagram.C4.Tests.Parity;

/// <summary>
/// The property rows derived from the six DISL definitions equal the ones the module wrote by hand
/// (c4-disl-fbl Requirements 3.1 and 3.2), row for row: for every document of the parity corpus, under
/// every view it declares and the origin of that view's kind, for every element and relationship of the
/// model, every line the view draws, the empty canvas and an unknown id; and with no view, under each of
/// the six origins. A difference names the document, the view and the element.
/// </summary>
public class C4DerivedRowsTests
{
    [Fact]
    public async Task EveryRow_IsTheHandWrittenOne()
    {
        // Arrange.
        var readings = await C4Corpus.ReadingsAsync(TestContext.Current.CancellationToken);
        var differences = new List<string>();

        // Act.
        foreach ((string document, string file, C4View? view, ToolOrigin origin, string target, C4Workspace workspace) in C4Corpus.Targets(readings))
        {
            var expected = HandWrittenC4.Rows(workspace, target).Select(Text).ToList();
            var actual = C4Definitions.Rows(workspace, file, view, origin, target).Select(Text).ToList();
            if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
            {
                differences.Add($"{document}, view {view?.Key ?? "(none)"} as {origin.Key}, {TranscriptText.Quoted(target)}:\n  expected {string.Join("\n           ", expected)}\n  actual   {string.Join("\n           ", actual)}");
            }
        }

        // Assert.
        Assert.True(differences.Count == 0, string.Join("\n", differences.Take(20)));
    }

    private static string Text(ContextPropertyDefinition row) =>
        $"{row.Id} | {row.Label} | {TranscriptText.Quoted(row.Value)} | {row.Editor} | {row.ReadOnlyReason} | {row.Group} | {(row.Candidates is null ? "null" : "[" + string.Join("; ", row.Candidates) + "]")}";
}
