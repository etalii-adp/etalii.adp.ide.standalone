using EtAlii.Adp.Context;
using Xunit;

namespace EtAlii.Adp.Diagram.WardleyMap.Tests.Parity;

/// <summary>
/// The property rows derived from the DISL definition equal the hand-written ones (wardley-map-disl-fbl task 6), row
/// for row and candidate for candidate: for every document of the parity corpus, every id the store holds, the empty
/// canvas and an unknown id, in a file that can be written and in one that cannot.
/// </summary>
public class WardleyDerivedRowsTests
{
    public static TheoryData<string> Documents() => WardleyCorpus.Names();

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryRow_IsTheHandWrittenOne(string name)
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = await WardleyCorpus.OpenAsync(name, cancellationToken);

        // Act and assert.
        foreach (var target in session.Targets)
        {
            var expected = Text(await session.HandWrittenProperties.DescribeAsync(session.Target(target), cancellationToken));
            var actual = Text(await session.Properties.DescribeAsync(session.Target(target), cancellationToken));
            Assert.True(expected.SequenceEqual(actual), $"{name} {target}:\nexpected {string.Join("\n         ", expected)}\nactual   {string.Join("\n         ", actual)}");

            var readOnlyExpected = Text(await session.ReadOnlyAsync(() => session.HandWrittenProperties.DescribeAsync(session.Target(target), cancellationToken).AsTask()));
            var readOnlyActual = Text(await session.ReadOnlyAsync(() => session.Properties.DescribeAsync(session.Target(target), cancellationToken).AsTask()));
            Assert.True(
                readOnlyExpected.SequenceEqual(readOnlyActual),
                $"{name} {target} (read-only):\nexpected {string.Join("\n         ", readOnlyExpected)}\nactual   {string.Join("\n         ", readOnlyActual)}");
        }
    }

    private static List<string> Text(IReadOnlyList<ContextPropertyDefinition> rows) => [.. rows.Select(Text)];

    private static string Text(ContextPropertyDefinition row) =>
        $"{row.Id} | {row.Label} | {row.Value.Replace("\n", "\\n", StringComparison.Ordinal)} | {row.Editor} | {row.ReadOnlyReason} | {row.Group} | {(row.Candidates is null ? "null" : "[" + string.Join("; ", row.Candidates) + "]")}";
}
