using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests;

public class AbmRuleSetTests
{
    private static IReadOnlyList<AbmBreach> Breaches(string text) => AbmRuleSet.Breaches(AbmParser.Parse(LineDocument.Parse(text)));

    [Theory]
    [InlineData("## Behavior\n- **Do:** A\n  - **Do:** B\n", AbmRuleIds.LeafWithChildren, true)]
    [InlineData("## Behavior\n- **Only while:** Ready\n", AbmRuleIds.DecoratorChildren, true)]
    [InlineData("## Behavior\n- **Ask approval before:** Post\n  - **Do:** A\n  - **Do:** B\n", AbmRuleIds.DecoratorChildren, true)]
    [InlineData("## Behavior\n- **Retry up to 0 times:** Test\n  - **Do:** A\n", AbmRuleIds.NoAttempts, true)]
    [InlineData("## Behavior\n- **Do in order:** Nothing\n", AbmRuleIds.EmptyComposite, false)]
    [InlineData("## Behavior\n- **Do:** A\n- **Do:** B\n", AbmRuleIds.SeveralRoots, false)]
    [InlineData("## Behavior\n- A\n", AbmRuleIds.NoKeyword, false)]
    public void EachRule_ReportsItsBreach(string text, string ruleId, bool isError)
    {
        // Act.
        var breach = Assert.Single(Breaches(text));

        // Assert.
        Assert.Equal(ruleId, breach.RuleId);
        Assert.Equal(isError, breach.IsError);
    }

    [Theory]
    [InlineData("# Notes\n")]
    [InlineData("## Behavior\n\nNot a list.\n")]
    public void AFileWithNoTree_IsInformation_NotAProblem(string text)
    {
        // Act.
        var breach = Assert.Single(Breaches(text));

        // Assert.
        Assert.Equal(AbmRuleIds.NoBehavior, breach.RuleId);
        Assert.True(breach.IsInformation);
    }
}
