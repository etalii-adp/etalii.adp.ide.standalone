using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests.Parity;

/// <summary>
/// The palette and the context menus derived from the DISL definition equal the hand-written ones
/// (runtime plan step S10), record for record: for every document of the parity corpus, every element
/// id, the empty id, a placement, a connect gesture and an unknown id, readable and read-only.
/// </summary>
public class GhgDerivedMenusTests
{
    public static TheoryData<string> Documents() => [.. GhgDisl.Texts().Select(document => document.Path)];

    [Fact]
    public void ThePalette_IsTheHandWrittenOne() => Assert.Equal(HandWrittenGhg.Toolbox, new GhgToolboxProvider().Items);

    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryMenu_IsTheHandWrittenOne(string path)
    {
        // Arrange.
        var readable = GhgDocumentEntry.Read(GhgDisl.Texts().Single(document => document.Path == path).Text);
        var readOnly = readable with { Unreadable = "parity: read-only" };

        // Act and assert.
        foreach (var target in Targets(readable.Model))
        {
            foreach (var entry in new[] { readable, readOnly })
            {
                var expected = HandWrittenGhg.Menus(entry, target);
                var actual = GhgDefinition.Menus(entry, target);
                Assert.True(
                    expected.Select(group => group.Actions).SequenceEqual(actual.Select(group => group.Actions), GroupComparer.Instance),
                    $"{path} {target} (read-only: {!entry.IsUsable}):\nexpected {Text(expected)}\nactual   {Text(actual)}");
            }
        }
    }

    /// <summary>The targets the parity transcript records menus for.</summary>
    private static IEnumerable<string> Targets(GhgModel model)
    {
        var trends = model.Trends.Select(trend => trend.Id).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        return model.Trends.Select(trend => trend.Id)
            .Concat(model.Triggers.Select(trigger => trigger.Id))
            .Concat(model.Notes.Select(note => note.Id))
            .Concat(model.Influences.Select(influence => influence.Id))
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Concat(["", GestureIds.Placement(240, 160), trends.Count >= 2 ? GestureIds.Relation(trends[0], trends[1]) : GestureIds.Relation("a", "b"), "parity:unknown"]);
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
