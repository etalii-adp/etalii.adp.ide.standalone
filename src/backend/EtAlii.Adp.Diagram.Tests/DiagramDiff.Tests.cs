using Xunit;

namespace EtAlii.Adp.Diagram.Tests;

/// <summary>
/// The shared change-detecting diff (backend-centralization R4.1, R4.4, R4.5).
/// </summary>
public class DiagramDiffTests
{
    private const string Node = "test/sample+node";
    private const string Frame = "test/sample+frame";
    private const string TypeUrl = "type.googleapis.com/test.Sample";

    public static TheoryData<string, DiagramElement[], DiagramElement[], string[], string[]> Changes => new()
    {
        // name, before, after, expected removed ids, expected added ids
        { "add", [Element("a")], [Element("a"), Element("b")], [], ["b"] },
        { "remove", [Element("a"), Element("b")], [Element("a")], ["b"], [] },
        { "move", [Element("a")], [Element("a", x: 40)], [], ["a"] },
        { "retype", [Element("a")], [Element("a", type: Frame)], [], ["a"] },
        { "payload change", [Element("a", payload: [1, 2])], [Element("a", payload: [1, 3])], [], ["a"] },
        { "nothing changed", [Element("a"), Element("b")], [Element("a"), Element("b")], [], [] },
    };

    [Theory]
    [MemberData(nameof(Changes))]
    public void EachKindOfChange_ArrivesAsTheDeltaItIs(
        string name,
        DiagramElement[] before,
        DiagramElement[] after,
        string[] removed,
        string[] added)
    {
        var deltas = DiagramDiff.Between(before, after);

        Assert.True(removed.SequenceEqual(RemovedIds(deltas)), $"{name}: removed [{string.Join(",", RemovedIds(deltas))}]");
        Assert.True(added.SequenceEqual(AddedIds(deltas)), $"{name}: added [{string.Join(",", AddedIds(deltas))}]");
    }

    [Fact]
    public void AnUnchangedElement_RenderedIntoAFreshArray_IsNotResent()
    {
        // R4.4 says payload BYTES. Every render serializes into a new array, and ReadOnlyMemory's
        // own equality compares which array it points into - so a record comparison would call an
        // unchanged element changed on every render, and each change would resend everything.
        var before = Element("a", payload: [7, 8, 9]);
        var after = Element("a", payload: [7, 8, 9]);
        Assert.NotEqual(before, after); // The trap is real: the records themselves differ.

        Assert.Empty(DiagramDiff.Between([before], [after]));
    }

    [Fact]
    public void ADiffThatRemovesAndAdds_RemovesFirst()
    {
        // R4.5, settled by the design. The client folds an add as an upsert keyed on id, so
        // removing first can never delete what the same batch just added.
        var deltas = DiagramDiff.Between([Element("gone")], [Element("new")]);

        Assert.Collection(
            deltas,
            first => Assert.Equal(["gone"], Assert.IsType<DiagramRemoveDelta>(first).ElementIds),
            second => Assert.Equal("new", Assert.Single(Assert.IsType<DiagramAddDelta>(second).Elements).Id));
    }

    [Fact]
    public void AChangedElement_ArrivesInItsNewState()
    {
        var deltas = DiagramDiff.Between([Element("a", x: 1)], [Element("a", x: 2)]);

        var added = Assert.Single(Assert.IsType<DiagramAddDelta>(Assert.Single(deltas)).Elements);
        Assert.Equal(2, added.X);
    }

    private static IEnumerable<string> RemovedIds(IReadOnlyList<DiagramDelta> deltas) =>
        deltas.OfType<DiagramRemoveDelta>().SelectMany(delta => delta.ElementIds);

    private static IEnumerable<string> AddedIds(IReadOnlyList<DiagramDelta> deltas) =>
        deltas.OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements).Select(element => element.Id);

    private static DiagramElement Element(string id, double x = 10, string type = Node, byte[]? payload = null) =>
        new(id, x, 20, type, TypeUrl, payload ?? [1]);
}
