using Xunit;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests.Parity;

/// <summary>
/// The findings derived from the DISL definition equal the hand-written validator's (supply-chain-disl-fbl, task 6),
/// finding for finding and in order: line, code, message and severity, for every document of the parity corpus.
/// </summary>
public class SupplyChainDerivedFindingsTests
{
    public static TheoryData<string> Documents() => SupplyChainCorpus.Names();

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryFinding_IsTheHandWrittenValidators(string name)
    {
        // Arrange.
        var model = await SupplyChainCorpus.ReadAsync(name, TestContext.Current.CancellationToken);

        // Act.
        var expected = HandWrittenSupplyChain.Breaches(model).Select(Line).ToList();
        var actual = SupplyChainValidator.Validate(model).Select(Line).ToList();

        // Assert.
        Assert.True(expected.SequenceEqual(actual, StringComparer.Ordinal), $"{name}:\nexpected {string.Join("\n         ", expected)}\nactual   {string.Join("\n         ", actual)}");
    }

    /// <summary>The corpus reaches every code the hand-written validator has, so the comparison above is not vacuous.</summary>
    [Fact]
    public async Task TheCorpus_ReachesEveryCode()
    {
        // Arrange.
        var reached = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in Documents())
        {
            var model = await SupplyChainCorpus.ReadAsync(row.Data, TestContext.Current.CancellationToken);
            reached.UnionWith(HandWrittenSupplyChain.Breaches(model).Select(breach => breach.RuleId));
        }

        // Assert.
        Assert.Equal(
            [
                SupplyChainRuleIds.DanglingFlow, SupplyChainRuleIds.DuplicateId, SupplyChainRuleIds.EmptyGroup, SupplyChainRuleIds.MissingId,
                SupplyChainRuleIds.SelfFlow, SupplyChainRuleIds.UnknownGroup, SupplyChainRuleIds.UnreadableEntry,
            ],
            reached.Order(StringComparer.Ordinal));
    }

    private static string Line(SupplyChainBreach breach) =>
        $"line {breach.Line + 1} | {breach.RuleId} | {(breach.IsWarning ? "warning" : "error")} | {breach.Message}";
}
