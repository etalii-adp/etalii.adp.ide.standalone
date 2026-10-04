using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests;

/// <summary>The rules, each reported on the line it breaks, and none reported for a clean document.</summary>
public sealed class SupplyChainValidatorTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void EveryRule_IsReportedOnTheBrokenDocument()
    {
        // Act.
        var breaches = SupplyChainValidator.Validate(SupplyChainParser.Parse(LineDocument.Parse(File.ReadAllText(Fixture("rules-broken.supply")))));

        // Assert.
        Assert.Equal(
            [
                SupplyChainRuleIds.DanglingFlow, SupplyChainRuleIds.DuplicateId, SupplyChainRuleIds.EmptyGroup,
                SupplyChainRuleIds.MissingId, SupplyChainRuleIds.SelfFlow, SupplyChainRuleIds.UnknownGroup,
            ],
            breaches.Select(breach => breach.RuleId).Distinct().Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ABreach_ReachesThePanel_OnItsOneBasedLine()
    {
        // Arrange.
        var text = await File.ReadAllTextAsync(Fixture("rules-broken.supply"), TestContext.Current.CancellationToken);
        var selfFlowLine = text.Split('\n').ToList().FindIndex(line => line.Contains("id: loop", StringComparison.Ordinal)) + 1;

        // Act.
        var problems = await new SupplyChainValidator().ValidateAsync(new DiagramValidationRequest(text, "rules-broken", "", Fixture("rules-broken.supply"), null), TestContext.Current.CancellationToken);

        // Assert.
        var selfFlow = Assert.Single(problems, problem => problem.RuleId == SupplyChainRuleIds.SelfFlow);
        Assert.Equal(DiagramProblemSeverity.Error, selfFlow.Severity);
        Assert.Equal((uint)selfFlowLine, Assert.IsType<DiagramProblemLineLocation>(selfFlow.Location).Number);
    }

    public static TheoryData<string> CleanDocuments => [Fixture("lf-line-endings.supply"), SupplyChainExamples.Automotive, SupplyChainExamples.GpuMemory];

    [Theory]
    [MemberData(nameof(CleanDocuments))]
    public void ACleanDocument_ReportsNothing(string path)
    {
        // Act.
        var breaches = SupplyChainValidator.Validate(SupplyChainParser.Parse(LineDocument.Parse(File.ReadAllText(path))));

        // Assert.
        Assert.Empty(breaches);
    }
}
