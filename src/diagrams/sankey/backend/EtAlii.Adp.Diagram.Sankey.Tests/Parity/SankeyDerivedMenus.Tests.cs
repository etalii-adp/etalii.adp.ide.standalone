using EtAlii.Adp.Context;
using Xunit;

namespace EtAlii.Adp.Diagram.Sankey.Tests.Parity;

/// <summary>
/// The palette and the context menus derived from the DISL definition equal the hand-written ones (sankey-disl-fbl
/// task 6), record for record: for every document of the parity corpus, every node and flow id, the empty id, a
/// placement, a connect gesture and an unknown id.
/// </summary>
public class SankeyDerivedMenusTests
{
    public static TheoryData<string> Documents() => SankeyCorpus.Names();

    [Fact]
    public void ThePalette_IsTheHandWrittenOne() => Assert.Equal(HandWrittenSankey.Toolbox, SankeyDefinition.Toolbox);

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryMenu_IsTheHandWrittenOne(string name)
    {
        // Arrange.
        var entry = await SankeyCorpus.ReadAsync(name, TestContext.Current.CancellationToken);

        // Act and assert.
        foreach (var target in SankeyCorpus.Targets(entry.Model))
        {
            var expected = HandWrittenSankey.Menus(entry, target);
            var actual = SankeyDefinition.Menus(entry, target);
            Assert.True(
                expected.Select(group => group.Actions).SequenceEqual(actual.Select(group => group.Actions), GroupComparer.Instance),
                $"{name} {target}:\nexpected {Text(expected)}\nactual   {Text(actual)}");
        }
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryRemovalConfirmation_IsTheHandWrittenOne(string name)
    {
        // Arrange.
        var entry = await SankeyCorpus.ReadAsync(name, TestContext.Current.CancellationToken);

        // Act and assert.
        foreach (var target in SankeyCorpus.Targets(entry.Model))
        {
            Assert.Equal(HandWrittenSankey.RemovalConfirmation(entry.Model, target), SankeyDefinition.RemovalConfirmation(entry.Model, target));
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
