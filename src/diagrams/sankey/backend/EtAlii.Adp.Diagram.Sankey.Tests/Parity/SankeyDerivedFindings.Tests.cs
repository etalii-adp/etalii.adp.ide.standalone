using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.Sankey.Tests.Parity;

/// <summary>
/// The findings derived from the DISL definition equal the hand-written rules' (sankey-disl-fbl task 7), finding for
/// finding and in order: line, code, severity and sentence, for every document of the parity corpus and for the
/// documents below, which reach what the corpus does not.
/// </summary>
public class SankeyDerivedFindingsTests
{
    private const string Header = "sankey: 1\r\n";

    /// <summary>Documents the corpus does not hold, each named for what it reaches.</summary>
    public static TheoryData<string, string> Reached() => new()
    {
        { "a flow written before the nodes it names", Header + "flows:\r\n  - from: a\r\n    to: b\r\n    value: 1\r\n    color: mauve\r\nnodes:\r\n  - id: a\r\n  - id: b\r\n    color: beige\r\n" },
        { "a node and a flow holding one id", Header + "nodes:\r\n  - id: a\r\n  - id: b\r\n  - id: a->b\r\nflows:\r\n  - from: a\r\n    to: b\r\n    value: 1\r\n" },
        { "one flow written three times", Header + "nodes:\r\n  - id: a\r\n  - id: b\r\nflows:\r\n  - from: a\r\n    to: b\r\n    value: 1\r\n  - from: a\r\n    to: b\r\n    value: 2\r\n  - from: a\r\n    to: b\r\n" },
        { "a flow back to the first column", Header + "nodes:\r\n  - id: a\r\n  - id: b\r\n  - id: c\r\nflows:\r\n  - from: a\r\n    to: b\r\n    value: 1\r\n  - from: b\r\n    to: c\r\n    value: 1\r\n  - from: c\r\n    to: a\r\n    value: 1\r\n" },
        { "a stated column behind its source", Header + "nodes:\r\n  - id: a\r\n    column: 3\r\n  - id: b\r\n    column: 1\r\nflows:\r\n  - from: a\r\n    to: b\r\n    value: 1\r\n" },
        { "a self-flow without a value and with an unknown colour", Header + "nodes:\r\n  - id: a\r\nflows:\r\n  - from: a\r\n    to: a\r\n    color: mauve\r\n" },
        { "a text that is not YAML", "sankey: 1\r\nnodes: [a, \r\n" },
    };

    public static TheoryData<string> Documents() => SankeyCorpus.Names();

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryFinding_IsTheHandWrittenRules(string name)
    {
        var entry = await SankeyCorpus.ReadAsync(name, TestContext.Current.CancellationToken);
        AssertSame(name, entry.Document.Text);
    }

    [Theory]
    [MemberData(nameof(Reached))]
    public void EveryFinding_IsTheHandWrittenRules_WhereTheCorpusDoesNotReach(string name, string text) => AssertSame(name, text);

    /// <summary>The documents reach every code the hand-written rules have, so the comparison above is not vacuous.</summary>
    [Fact]
    public async Task TheDocuments_ReachEveryCode()
    {
        var texts = new List<string>();
        foreach (var name in SankeyCorpus.Names().Select(row => row.Data))
        {
            texts.Add((await SankeyCorpus.ReadAsync(name, TestContext.Current.CancellationToken)).Document.Text);
        }

        var reached = texts.Concat(Reached().Select(row => row.Data.Item2))
            .SelectMany(text => HandWrittenSankey.Breaches(SankeyParser.Parse(LineDocument.Parse(text))))
            .Select(breach => breach.RuleId)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(SankeyRuleIds.All.Order(StringComparer.Ordinal), reached.Order(StringComparer.Ordinal));
    }

    private static void AssertSame(string name, string text)
    {
        var body = LineDocument.Parse(text);
        var expected = HandWrittenSankey.Breaches(SankeyParser.Parse(body))
            .Select(breach => $"line {breach.Line + 1} | {breach.RuleId} | {(breach.IsWarning ? "warning" : "error")} | {breach.Message}")
            .ToList();
        var actual = SankeyDefinition.Findings(SankeyParser.Outcome(body))
            .Select(finding => $"line {finding.Line} | {finding.Code} | {finding.Severity} | {finding.Message}")
            .ToList();
        Assert.True(
            expected.SequenceEqual(actual, StringComparer.Ordinal),
            $"{name}:\nexpected {string.Join("\n         ", expected)}\nactual   {string.Join("\n         ", actual)}");
    }
}
