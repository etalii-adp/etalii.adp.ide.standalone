using EtAlii.Adp.Diagram.C4.Tests.Parity;
using Xunit;

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// The empty canvas offers "Add person…" and "Add software system…", ending in an ellipsis as every
/// other label that asks for a value does (c4-disl-fbl Requirement 3.8, finding A14). Before the fix the
/// two ended in a literal <c>&amp;</c>, in the module's own menu and in the definitions that reproduced it.
/// </summary>
/// <remarks>
/// Held over every target the derived menus are compared on: under each view of every document of the
/// parity corpus, and with no view under each of the six origins, so a definition that kept the
/// <c>&amp;</c> is named by its view's kind.
/// </remarks>
public class C4EmptyCanvasLabelsTests
{
    private static readonly (string Id, string Label)[] Expected =
    [
        ("c4.add-person", "Add person…"),
        ("c4.add-softwaresystem", "Add software system…"),
    ];

    [Fact]
    public async Task TheEmptyCanvasAdds_EndInAnEllipsis()
    {
        // Arrange.
        var readings = await C4Corpus.ReadingsAsync(TestContext.Current.CancellationToken);
        var differences = new List<string>();
        var canvases = 0;

        // Act.
        foreach ((string document, string file, C4View? view, ToolOrigin origin, string target, C4Workspace workspace) in C4Corpus.Targets(readings))
        {
            if (target.Length != 0)
            {
                continue;
            }

            canvases++;
            var offered = C4Definitions.Menus(workspace, file, view, origin, target)
                .SelectMany(group => group.Actions)
                .ToDictionary(action => action.Id, action => action.Label, StringComparer.Ordinal);
            foreach ((string id, string label) in Expected)
            {
                var actual = offered.GetValueOrDefault(id);
                if (!string.Equals(actual, label, StringComparison.Ordinal))
                {
                    differences.Add($"{document}, view {view?.Key ?? "(none)"} as {origin.Key}: {id} is {(actual is null ? "not offered" : $"\"{actual}\"")}, expected \"{label}\"");
                }
            }
        }

        // Assert.
        Assert.True(canvases > 0, "The corpus gave no empty canvas to look at.");
        Assert.True(differences.Count == 0, $"{differences.Count} of {canvases * Expected.Length} empty-canvas labels differ:\n{string.Join("\n", differences.Take(12))}");
    }
}
