using Xunit;

namespace EtAlii.Adp.Diagram.C4.Tests.Parity;

/// <summary>
/// The context menus derived from the six DISL definitions equal the ones the module wrote by hand
/// (c4-disl-fbl Requirements 3.1 and 3.2): for every document of the parity corpus, under every view it
/// declares and the origin of that view's kind, for every element and relationship of the model, every
/// line the view draws, the empty canvas and an unknown id; and with no view, under each of the six
/// origins. A difference names the document, the view and the element.
/// </summary>
public class C4DerivedMenusTests
{
    [Fact]
    public async Task EveryMenu_IsTheHandWrittenOne()
    {
        // Arrange.
        var readings = await C4Corpus.ReadingsAsync(TestContext.Current.CancellationToken);
        var differences = new List<string>();

        // Act.
        foreach ((string document, string file, C4View? view, ToolOrigin origin, string target, C4Workspace workspace) in C4Corpus.Targets(readings))
        {
            var expected = HandWrittenC4.Menus(workspace, target).MenuLines();
            var actual = C4Definitions.Menus(workspace, file, view, origin, target).MenuLines();
            if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
            {
                differences.Add($"{document}, view {view?.Key ?? "(none)"} as {origin.Key}, {TranscriptText.Quoted(target)}:\n  expected {string.Join(" / ", expected)}\n  actual   {string.Join(" / ", actual)}");
            }
        }

        // Assert.
        Assert.True(differences.Count == 0, string.Join("\n", differences.Take(20)));
    }
}
