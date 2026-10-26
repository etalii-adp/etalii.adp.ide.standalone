using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests;

/// <summary>The findings are one list by line across every code, ties kept in the order they arose (finding A9).</summary>
public sealed class SupplyChainFindingsTests
{
    [Fact]
    public void TheFindings_AreOrderedByLine_TiesAsTheyArose()
    {
        // Arrange.
        DislFinding[] findings = [Finding("b", 7), Finding("a", 3), Finding("c", 7), Finding("d", null), Finding("e", 3)];

        // Act.
        var ordered = SupplyChainFindings.ByLine(findings);

        // Assert.
        Assert.Equal(["d", "a", "e", "b", "c"], ordered.Select(finding => finding.Code));
    }

    [Fact]
    public void ADocument_IsReportedByLineAcrossEveryCode()
    {
        // Arrange: a dangling flow is defined after the self-flow, an empty group before both, an unknown key between.
        const string text =
            "supply-chain: 1\r\n"
            + "flows:\r\n"
            + "  - id: loop\r\n    from: a\r\n    to: a\r\n"
            + "groups:\r\n"
            + "  - id: lonely\r\n"
            + "nodes:\r\n"
            + "  - id: a\r\n    type: source\r\n    colour: red\r\n";

        // Act.
        var lines = SupplyChainValidator.Validate(SupplyChainParser.Parse(LineDocument.Parse(text))).Select(breach => (breach.Line, breach.RuleId));

        // Assert.
        Assert.Equal(
            [(2, SupplyChainRuleIds.SelfFlow), (6, SupplyChainRuleIds.EmptyGroup), (10, SupplyChainRuleIds.UnreadableEntry)],
            lines);
    }

    [Fact]
    public void AFinding_ReachesThePanelOnItsOneBasedLine_AndABreachOnItsZeroBasedOne()
    {
        // Arrange.
        var finding = new DislFinding(SupplyChainRuleIds.SelfFlow, "selfFlow", "error", "It loops.", [], 4);

        // Act.
        var problem = SupplyChainFindings.ToProblem(finding);
        var breach = SupplyChainFindings.ToBreach(finding);

        // Assert.
        Assert.Equal((4u, DiagramProblemSeverity.Error), (Assert.IsType<DiagramProblemLineLocation>(problem.Location).Number, problem.Severity));
        Assert.Equal(new SupplyChainBreach(SupplyChainRuleIds.SelfFlow, "It loops.", 3, false), breach);
    }

    private static DislFinding Finding(string code, int? line) => new(code, code, "warning", code, [], line);
}
