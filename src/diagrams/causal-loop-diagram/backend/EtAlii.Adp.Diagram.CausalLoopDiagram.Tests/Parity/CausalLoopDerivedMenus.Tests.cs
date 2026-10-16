using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests.Parity;

/// <summary>
/// The palette and the context menus derived from the DISL definition equal the hand-written ones
/// (causal-loop-disl-fbl Requirements 3.1 and 3.2), record for record: for every document of the parity
/// corpus, every element id, the empty id, a placement, a connect gesture and an unknown id, readable and
/// read-only - the targets the parity transcript records.
/// </summary>
public class CausalLoopDerivedMenusTests
{
    public static TheoryData<string> Documents() => [.. CausalLoopTranscript.Corpus()];

    [Fact]
    public void ThePalette_IsTheHandWrittenOne() => Assert.Equal(HandWrittenCausalLoop.Toolbox, CausalLoopDefinition.Definition.Toolbox);

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryMenu_IsTheHandWrittenOne(string relative)
    {
        // Arrange.
        var readable = await CausalLoopEntries.ReadAsync(relative, TestContext.Current.CancellationToken);
        var readOnly = readable with { Unreadable = "parity: read-only" };

        // Act and assert.
        foreach (var target in CausalLoopEntries.Targets(readable.Model, withGestures: true))
        {
            foreach (var entry in new[] { readable, readOnly })
            {
                var expected = HandWrittenCausalLoop.Menus(entry, target);
                var actual = CausalLoopDefinition.Menus(entry, target);
                Assert.True(
                    expected.Select(group => group.Actions).SequenceEqual(actual.Select(group => group.Actions), GroupComparer.Instance),
                    $"{relative} {target} (read-only: {!entry.IsUsable}):\nexpected {Text(expected)}\nactual   {Text(actual)}");
            }
        }
    }

    private static string Text(IReadOnlyList<ContextActionGroupDefinition> groups) =>
        string.Join(" / ", groups.Select(group => string.Join(", ", group.Actions)));

    private sealed class GroupComparer : IEqualityComparer<IReadOnlyList<ContextActionDefinition>>
    {
        public static GroupComparer Instance { get; } = new();

        public bool Equals(IReadOnlyList<ContextActionDefinition>? x, IReadOnlyList<ContextActionDefinition>? y) => x!.SequenceEqual(y!);

        public int GetHashCode(IReadOnlyList<ContextActionDefinition> obj) => obj.Count;
    }
}

/// <summary>An entry of a corpus document as the store makes one, and the targets the parity transcript records for it.</summary>
internal static class CausalLoopEntries
{
    /// <remarks>The model is the hand-written parser's, the one the hand-written oracle read; the derived side reads the body.</remarks>
    public static async Task<DocumentEntry<CausalLoopBody, CausalLoopModel>> ReadAsync(string relative, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(Path.Combine(CausalLoopTranscript.SourceFolder, relative), cancellationToken);
        return new DocumentEntry<CausalLoopBody, CausalLoopModel>(CausalLoopBody.Parse(text), CausalLoopParser.Parse(CausalLoopDocument.Parse(text)).Model);
    }

    public static IEnumerable<string> Targets(CausalLoopModel model, bool withGestures)
    {
        var elements = model.Variables.Select(variable => $"variable:{variable.Id}")
            .Concat(model.Links.Select(link => link.Id))
            .Concat(model.Loops.Select(loop => loop.Id))
            .Distinct(StringComparer.Ordinal);
        if (!withGestures) return elements.Concat(["", CausalLoopTranscript.Unknown]);

        var variableIds = model.Variables.Select(variable => variable.Id).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var relation = variableIds.Count >= 2 ? GestureIds.Relation(variableIds[1], variableIds[0]) : GestureIds.Relation("a", "b");
        return elements.Concat(["", GestureIds.Placement(240, 160), relation, CausalLoopTranscript.Unknown]);
    }
}
