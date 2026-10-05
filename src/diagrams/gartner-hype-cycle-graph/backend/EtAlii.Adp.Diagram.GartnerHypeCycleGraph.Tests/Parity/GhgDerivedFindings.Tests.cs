using Xunit;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests.Parity;

/// <summary>
/// The findings derived from the DISL definition equal the hand-written rule set's (runtime plan step
/// S12), finding for finding, in order: code, message and line, for every document of the parity
/// corpus and for the documents below, which reach what the corpus does not.
/// </summary>
public class GhgDerivedFindingsTests
{
    private const string Header = "gartner-hypecycle-graph: 1\r\n";

    private static readonly string Trends =
        "trends:\r\n"
        + "  - id: a\r\n    name: A\r\n    start: 1900-01\r\n    stop: 1920-01\r\n    row: 0\r\n    phases: 4\r\n"
        + "  - id: b\r\n    name: B\r\n    start: 1910-01\r\n    stop: 1930-01\r\n    row: 1\r\n    phases: 4\r\n";

    private static readonly string Trigger = "triggers:\r\n  - id: t\r\n    name: T\r\n    date: 1899-01\r\n    row: 2\r\n";

    /// <summary>Documents the corpus does not hold, each named for what it reaches.</summary>
    public static TheoryData<string, string> Reached() => new()
    {
        { "three influences one way", Header + Trends + "influences:\r\n" + Influence("ab1", "a", "b") + Influence("ab2", "a", "b") + Influence("ab3", "a", "b") },
        { "a duplicated influence into itself", Header + Trends + "influences:\r\n" + Influence("aa1", "a", "a") + Influence("aa2", "a", "a") },
        { "a duplicated influence into a trigger", Header + Trends + Trigger + "influences:\r\n" + Influence("at1", "a", "t") + Influence("at2", "a", "t") },
        { "two ids each held twice", Header + Trends + "  - id: b\r\n    name: B2\r\n    start: 1940-01\r\n    stop: 1950-01\r\n    phases: 4\r\n  - id: a\r\n    name: A2\r\n    start: 1960-01\r\n    stop: 1970-01\r\n    phases: 4\r\ninfluences: []\r\n" },
        { "a later holder of an id that breaks a rule", Header + Trends + "  - id: a\r\n    name: A2\r\n    start: 1960-01\r\n    stop: 1950-01\r\n    phases: 9\r\ninfluences: []\r\n" },
        { "a trend without an id that breaks a rule", Header + Trends + "  - name: Nobody\r\n    start: 1960-01\r\n    stop: 1950-01\r\n    phases: 4\r\ninfluences: []\r\n" },
        { "a trend without dates and with an unknown key", Header + "trends:\r\n  - id: a\r\n    name: A\r\n    colour: red\r\n    row: x\r\n    phases: 4\r\n  - id: b\r\n    name: B\r\n    start: 1900-01\r\n    phases: 4\r\ninfluences: []\r\n" },
        { "dangling ends both ways", Header + Trends + "influences:\r\n" + Influence("ax", "x", "y") + Influence("ay", "a", "y") },
    };

    public static TheoryData<string> Documents() => [.. GhgDisl.Texts().Select(document => document.Path)];

    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryFinding_IsTheHandWrittenRuleSets(string path) =>
        AssertSame(path, GhgDisl.Texts().Single(document => document.Path == path).Text);

    [Theory]
    [MemberData(nameof(Reached))]
    public void EveryFinding_IsTheHandWrittenRuleSets_WhereTheCorpusDoesNotReach(string name, string text) => AssertSame(name, text);

    /// <summary>The corpus reaches every code the hand-written rule set has, so the comparison above is not vacuous.</summary>
    [Fact]
    public void TheDocuments_ReachEveryCode()
    {
        var reached = GhgDisl.Texts().Select(document => document.Text).Concat(Reached().Select(row => row.Data.Item2))
            .SelectMany(text => GhgRuleSet.Breaches(GhgParser.Parse(text)))
            .Select(breach => breach.RuleId)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(GhgRuleIds.All.Order(StringComparer.Ordinal), reached.Order(StringComparer.Ordinal));
    }

    private static void AssertSame(string name, string text)
    {
        var expected = GhgRuleSet.Breaches(GhgParser.Parse(text)).Select(breach => $"line {breach.Line + 1} | {breach.RuleId} | {breach.Message}").ToList();
        var actual = GhgValidator.Validate(GhgBody.Parse(text)).Select(breach => $"line {breach.Line + 1} | {breach.RuleId} | {breach.Message}").ToList();
        Assert.True(
            expected.SequenceEqual(actual, StringComparer.Ordinal),
            $"{name}:\nexpected {string.Join("\n         ", expected)}\nactual   {string.Join("\n         ", actual)}");
    }

    private static string Influence(string id, string from, string to) =>
        $"  - id: {id}\r\n    from: {from}\r\n    from-phase: peak\r\n    from-edge: top\r\n    from-at: 0.5\r\n    to: {to}\r\n    to-phase: slope\r\n    to-edge: bottom\r\n    to-at: 0.5\r\n";
}
