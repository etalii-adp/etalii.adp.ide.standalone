using EtAlii.Adp.Context;
using Xunit;

namespace EtAlii.Adp.Diagram.WardleyMap.Tests.Parity;

/// <summary>
/// The palette and the context menus derived from the DISL definition equal the hand-written ones
/// (wardley-map-disl-fbl task 6), record for record: for every document of the parity corpus, every id the store
/// holds, the empty canvas and an unknown id, in a file that can be written and in one that cannot.
/// </summary>
public class WardleyDerivedMenusTests
{
    public static TheoryData<string> Documents() => WardleyCorpus.Names();

    [Fact]
    public void ThePalette_IsTheHandWrittenOne() => Assert.Equal(new HandWrittenWardleyToolbox().Items, new WardleyToolboxProvider().Items);

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryMenu_IsTheHandWrittenOne(string name)
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = await WardleyCorpus.OpenAsync(name, cancellationToken);

        // Act and assert.
        foreach (var target in session.Targets)
        {
            var expected = Text(await session.HandWrittenActions.DiscoverAsync(session.Target(target), cancellationToken));
            var actual = Text(await session.Actions.DiscoverAsync(session.Target(target), cancellationToken));
            Assert.True(expected == actual, $"{name} {target}:\nexpected {expected}\nactual   {actual}");

            var readOnlyExpected = Text(await session.ReadOnlyAsync(() => session.HandWrittenActions.DiscoverAsync(session.Target(target), cancellationToken).AsTask()));
            var readOnlyActual = Text(await session.ReadOnlyAsync(() => session.Actions.DiscoverAsync(session.Target(target), cancellationToken).AsTask()));
            Assert.True(readOnlyExpected == readOnlyActual, $"{name} {target} (read-only):\nexpected {readOnlyExpected}\nactual   {readOnlyActual}");
        }
    }

    /// <summary>A menu as text, every field of every entry of every group in order, a submenu's included.</summary>
    private static string Text(IReadOnlyList<ContextActionGroupDefinition>? groups) =>
        groups is null ? "none" : string.Join(" / ", groups.Select(group => string.Join(", ", group.Actions.Select(Text))));

    private static string Text(ContextActionDefinition action) =>
        $"{action.Id} | {action.Label} | {action.Icon} | {action.Shortcut} | {action.Available} | {action.UnavailableReason} | [{Text(action.Children)}]";
}
