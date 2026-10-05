using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests.Parity;

/// <summary>
/// The palette, the context menus and the property rows derived from the DISL definition equal the
/// hand-written ones (runtime plan step S19a), record for record: for every document of the parity
/// corpus, every node, the empty id, a placement, parent-line gestures between every pair of nodes and
/// to and from a node that is not there, and an unknown id, readable and unreadable.
/// </summary>
public class AbmDerivedMenusTests
{
    public static TheoryData<string> Documents() => [.. Texts().Select(document => document.Name)];

    private static IEnumerable<(string Name, string Text)> Texts() => AbmDisl.Corpus().Append(("inline/forest", AbmDislModelTests.Forest));

    [Fact]
    public void ThePalette_IsTheHandWrittenOne() => Assert.Equal(HandWrittenAbm.Toolbox, new AbmToolboxProvider().Items);

    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryMenu_IsTheHandWrittenOne(string name)
    {
        // Arrange.
        var readable = AbmDocumentEntry.Read(Texts().Single(document => document.Name == name).Text);
        var unreadable = readable with { Unreadable = "parity: unreadable" };

        // Act and assert.
        foreach (var target in Targets(readable.Model))
        {
            foreach (var entry in new[] { readable, unreadable })
            {
                var expected = HandWrittenAbm.Menus(entry, target);
                var actual = AbmDefinition.Menus(entry, target);
                Assert.True(
                    expected.Select(group => group.Actions).SequenceEqual(actual.Select(group => group.Actions), GroupComparer.Instance),
                    $"{name} {target} (unreadable: {!entry.IsUsable}):\nexpected {Text(expected)}\nactual   {Text(actual)}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryRow_IsTheHandWrittenOne(string name)
    {
        // Arrange.
        var readable = AbmDocumentEntry.Read(Texts().Single(document => document.Name == name).Text);
        var unreadable = readable with { Unreadable = "parity: unreadable" };

        // Act and assert.
        foreach (var target in Targets(readable.Model))
        {
            foreach (var entry in new[] { readable, unreadable })
            {
                var expected = HandWrittenAbm.Rows(entry, target).Select(Row).ToList();
                var actual = AbmDefinition.Rows(entry, target).Select(Row).ToList();
                Assert.True(
                    expected.SequenceEqual(actual),
                    $"{name} {target} (unreadable: {!entry.IsUsable}):\nexpected {string.Join("\n         ", expected)}\nactual   {string.Join("\n         ", actual)}");
            }
        }
    }

    private static IEnumerable<string> Targets(AbmModel model)
    {
        var ids = model.Nodes.Select(node => node.Id).ToList();
        var gestures = ids.SelectMany(from => ids.Select(to => GestureIds.Relation(from, to)))
            .Concat([GestureIds.Relation("9.9", ids.FirstOrDefault() ?? "1"), GestureIds.Relation(ids.FirstOrDefault() ?? "1", "9.9")]);
        return ids.Concat(["", GestureIds.Placement(240, 160), "child:1.1", "parity:unknown"]).Concat(gestures);
    }

    private static string Text(IReadOnlyList<ContextActionGroupDefinition> groups) =>
        string.Join(" / ", groups.Select(group => string.Join(", ", group.Actions)));

    private static string Row(ContextPropertyDefinition row) =>
        $"{row.Id} | {row.Label} | {row.Value.Replace("\n", "\\n", StringComparison.Ordinal)} | {row.Editor} | {row.ReadOnlyReason} | {row.Group} | {(row.Candidates is null ? "null" : "[" + string.Join("; ", row.Candidates) + "]")}";

    private sealed class GroupComparer : IEqualityComparer<IReadOnlyList<ContextActionDefinition>>
    {
        public static GroupComparer Instance { get; } = new();

        public bool Equals(IReadOnlyList<ContextActionDefinition>? x, IReadOnlyList<ContextActionDefinition>? y) => x!.SequenceEqual(y!);

        public int GetHashCode(IReadOnlyList<ContextActionDefinition> obj) => obj.Count;
    }
}
