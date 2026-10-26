using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests;

/// <summary>What the parser could not read reaches the definition with its sentence unchanged, on its one-based line.</summary>
public sealed class SupplyChainReaderFindingsTests
{
    [Fact]
    public void AnUnknownKey_IsHandedOnWithItsSentenceAndLine()
    {
        // Arrange.
        var model = SupplyChainParser.Parse(LineDocument.Parse("supply-chain: 1\r\nnodes:\r\n  - id: a\r\n    type: source\r\n    colour: red\r\n"));
        var problem = Assert.Single(model.Problems);

        // Act.
        var finding = Assert.Single(SupplyChainReaderFindings.Of(model));

        // Assert.
        Assert.Equal(problem.Message, finding.Detail["reason"]);
        Assert.Equal(5, finding.Line);
    }

    [Fact]
    public void AnUnknownKey_IsReportedAsUnreadable()
    {
        // Act.
        var breach = Assert.Single(SupplyChainValidator.Validate(SupplyChainParser.Parse(LineDocument.Parse("supply-chain: 1\r\nnodes:\r\n  - id: a\r\n    type: source\r\n    colour: red\r\n"))));

        // Assert.
        Assert.Equal((SupplyChainRuleIds.UnreadableEntry, 4, true), (breach.RuleId, breach.Line, breach.IsWarning));
    }
}
