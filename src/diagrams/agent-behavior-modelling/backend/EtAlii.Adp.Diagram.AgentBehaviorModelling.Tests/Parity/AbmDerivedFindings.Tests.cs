using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests.Parity;

/// <summary>
/// The findings derived from the DISL definition equal the hand-written ones (runtime plan step S19b),
/// breach for breach and in the same order: code, message, line and severity, for every document of the
/// parity corpus and for documents that break each rule, alone and together.
/// </summary>
public class AbmDerivedFindingsTests
{
    public static TheoryData<string> Documents() => [.. Texts().Select(document => document.Name)];

    private static IEnumerable<(string Name, string Text)> Texts() => AbmDisl.Corpus().Append(("inline/forest", AbmDislModelTests.Forest)).Concat(Breaking);

    /// <summary>Documents that break the rules, each named for what it breaks.</summary>
    private static readonly (string Name, string Text)[] Breaking =
    [
        ("breaks/no-heading", "# Notes\n\nProse only.\n"),
        ("breaks/no-list", "# Agent\n\n## Behavior\n\nNot a list.\n"),
        ("breaks/several-roots", "## Behavior\n- **Do:** A\n- **Check:** B\n- **Do in order:** C\n  - **Do:** D\n"),
        ("breaks/retry-zero", "## Behavior\n- **Retry up to 0 times:** Test\n  - **Do:** A\n"),
        ("breaks/retry-zero-no-child", "## Behavior\n- **Retry up to 0 times:** Test\n"),
        ("breaks/leaf-with-children", "## Behavior\n- **Ask the user:** Which?\n  - **Do:** A\n  - **Do:** B\n"),
        ("breaks/decorator-none", "## Behavior\n- **Only while:** Ready\n"),
        ("breaks/decorator-two", "## Behavior\n- **Ask approval before:** Post\n  - **Do:** A\n  - **Do:** B\n"),
        ("breaks/empty-composite", "## Behavior\n- **Do together:** Nothing\n"),
        ("breaks/no-keyword", "## Behavior\n- **Do in order:** Work\n  - A plain item\n  - **Do:** B\n  - Another plain item that is long enough to be shortened when the message names it, surely\n"),
        ("breaks/everything", "## Behavior\n- An item\n  - **Delegate:** Child\n    - Grandchild\n- **Repeat until:** Done\n- **Try in order:** Empty\n- **Retry up to 0 times:** Again\n  - **Do:** A\n  - **Do:** B\n"),
    ];

    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryFinding_IsTheHandWrittenOne(string name)
    {
        // Arrange.
        var text = Texts().Single(document => document.Name == name).Text;

        // Act.
        var expected = AbmRuleSet.Breaches(AbmParser.Parse(LineDocument.Parse(text)));
        var actual = AbmValidator.Validate(AbmBody.Parse(text));

        // Assert.
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TheBreakingDocuments_BreakEveryRule()
    {
        // Act.
        var codes = Breaking.SelectMany(document => AbmValidator.Validate(AbmBody.Parse(document.Text))).Select(breach => breach.RuleId).ToHashSet();

        // Assert.
        Assert.Equal(
            new[] { AbmRuleIds.NoKeyword, AbmRuleIds.LeafWithChildren, AbmRuleIds.DecoratorChildren, AbmRuleIds.EmptyComposite, AbmRuleIds.SeveralRoots, AbmRuleIds.NoAttempts, AbmRuleIds.NoBehavior }.Order(),
            codes.Order());
    }
}
