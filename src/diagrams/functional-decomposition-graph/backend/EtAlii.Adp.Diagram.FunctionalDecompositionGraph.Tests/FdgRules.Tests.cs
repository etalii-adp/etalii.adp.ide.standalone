using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests;

/// <summary>
/// Requirement 5's table, and the eight things a document can get wrong.
/// </summary>
/// <remarks>
/// <para>
/// <b>One fixture per rule id, each carrying exactly that breach.</b> Exactly-one is what makes
/// each case evidence: a fixture tripping two rules cannot say which rule its test proved, and a
/// rule whose fixture also breaks another is indistinguishable from a rule that never fires at
/// all. So every case below asserts both that its rule fired AND that nothing else did.
/// </para>
/// <para>
/// <b><c>rules-clean.fdg</c> is the control</b>, and it deliberately contains a navigation loop.
/// If it ever reports anything, a rule has started firing on a document the notation permits -
/// which is a worse failure than a rule that misses, because it accuses the author.
/// </para>
/// </remarks>
public class FdgRulesTests
{
    private static FdgModel Load(string name) =>
        FdgParser.Parse(LineDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name))));

    private static IReadOnlyList<FdgBreach> BreachesIn(string fixture) => FdgRuleSet.Breaches(Load(fixture));

    public static TheoryData<string, string> EveryRule => new()
    {
        { "rule-forbidden-link.fdg", FdgRuleIds.ForbiddenLink },
        { "rule-self-link.fdg", FdgRuleIds.SelfLink },
        { "rule-second-parent.fdg", FdgRuleIds.SecondParent },
        { "rule-second-shows.fdg", FdgRuleIds.SecondShows },
        { "rule-ownership-cycle.fdg", FdgRuleIds.OwnershipCycle },
        { "rule-dangling-reference.fdg", FdgRuleIds.DanglingReference },
        { "rule-duplicate-id.fdg", FdgRuleIds.DuplicateId },
        { "rule-unreadable-entry.fdg", FdgRuleIds.UnreadableEntry },
    };

    /// <summary>Each fixture reports its own rule, and only its own.</summary>
    [Theory]
    [MemberData(nameof(EveryRule))]
    public void EachFixture_ReportsItsOwnRuleAndNoOther(string fixture, string ruleId)
    {
        var breaches = BreachesIn(fixture);

        Assert.Contains(breaches, breach => breach.RuleId == ruleId);
        Assert.All(breaches, breach => Assert.Equal(ruleId, breach.RuleId));
    }

    /// <summary>Every breach names the elements involved, which is what the panel points at.</summary>
    [Theory]
    [MemberData(nameof(EveryRule))]
    public void EachBreach_NamesWhatItIsAbout(string fixture, string ruleId)
    {
        var breaches = BreachesIn(fixture).Where(breach => breach.RuleId == ruleId).ToList();

        Assert.NotEmpty(breaches);
        Assert.All(breaches, breach => Assert.False(string.IsNullOrWhiteSpace(breach.Message)));
        // The unreadable-entry rule carries the parser's own problems, which are about a line
        // rather than an element, so it is the one rule that may name none.
        if (ruleId != FdgRuleIds.UnreadableEntry)
        {
            Assert.All(breaches, breach => Assert.NotEmpty(breach.Elements));
        }
    }

    /// <summary>The control: a document using the notation correctly reports nothing at all.</summary>
    [Fact]
    public void TheCleanDocument_ReportsNothing()
    {
        Assert.Empty(BreachesIn("rules-clean.fdg"));
    }

    /// <summary>
    /// THE SABOTAGE THIS RULE WAS WRITTEN AGAINST: a Shows loop is NOT an ownership cycle.
    /// </summary>
    /// <remarks>
    /// The clean fixture contains a real navigation round trip - page shows detail, detail's action
    /// shows page. A cycle check that included <c>shows</c> would report it, which is reporting the
    /// notation working: Requirement 5.4 says an Action may show its own page or any page above it,
    /// and names the four relations the rule covers, <b>not Shows</b>. Asserted on the cycle
    /// function directly rather than only through the rule set, so it cannot pass because some
    /// other filter happened to drop the finding.
    /// </remarks>
    [Fact]
    public void AShowsLoop_IsNotACycle()
    {
        var model = Load("rules-clean.fdg");

        // The loop really is in the document - otherwise this case proves nothing.
        var shows = model.Connections.Where(connection => connection.Type == FdgConnectionTypes.Shows).ToList();
        Assert.Equal(2, shows.Count);
        Assert.Contains(shows, connection => connection is { From: "open", To: "detail" });
        Assert.Contains(shows, connection => connection is { From: "back", To: "page" });

        Assert.Empty(FdgOwnership.CyclesIn(model));
    }

    /// <summary>And an ownership loop IS one, so the exemption is not simply "never report".</summary>
    [Fact]
    public void AnOwnershipLoop_IsReportedOnceWithItsMembers()
    {
        var model = Load("rule-ownership-cycle.fdg");

        var cycles = FdgOwnership.CyclesIn(model);

        var cycle = Assert.Single(cycles);
        Assert.Equal(["f1", "f2"], cycle);
    }

    /// <summary>
    /// The one-parent limits do not prevent a loop by themselves, which is why the cycle rule
    /// exists. The fixture's two Functions have exactly one parent each and break no cardinality.
    /// </summary>
    [Fact]
    public void TheOwnershipLoop_BreaksNoCardinalityLimit()
    {
        var breaches = BreachesIn("rule-ownership-cycle.fdg");

        Assert.DoesNotContain(breaches, breach => breach.RuleId == FdgRuleIds.SecondParent);
        Assert.Contains(breaches, breach => breach.RuleId == FdgRuleIds.OwnershipCycle);
    }

    /// <summary>The table is the five relations the requirement lists, and nothing else.</summary>
    [Fact]
    public void TheTable_IsExactlyTheFiveRelations()
    {
        Assert.Equal(
            ["ui-child", "owns-action", "owns-data", "owns-function", "shows"],
            FdgRelations.All.Select(relation => relation.Id));

        // Four own; `shows` does not, which is what the cycle rule reads.
        Assert.Equal(4, FdgRelations.All.Count(relation => relation.IsOwnership));
        Assert.False(FdgRelations.ById(FdgConnectionTypes.Shows)!.IsOwnership);
    }

    /// <summary>
    /// A Comment is refused by construction: it is in no relation's sources and is no relation's
    /// target, so every link to or from one is forbidden without a Comment clause anywhere.
    /// </summary>
    [Fact]
    public void NoRelation_AdmitsAComment()
    {
        // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Local
        // Reason: Used in a test case which is acceptable.
        Assert.All(FdgRelations.All, relation =>
        {
            Assert.DoesNotContain(FdgElementTypes.Comment, relation.Sources);
            Assert.NotEqual(FdgElementTypes.Comment, relation.Target);
        });
    }

    /// <summary>The cardinality limits are the ones the requirement's table states.</summary>
    [Fact]
    public void TheLimits_AreTheOnesTheTableStates()
    {
        Assert.All(
            FdgRelations.All.Where(relation => relation.IsOwnership),
            relation => Assert.Equal(1, relation.MaxIntoTarget));

        var shows = FdgRelations.ById(FdgConnectionTypes.Shows)!;
        Assert.Equal(1, shows.MaxFromSource);
        // A UI Element may be shown by any number of Actions: the request set no limit there.
        Assert.Null(shows.MaxIntoTarget);
    }

    /// <summary>A document that breaks a rule still opens and still reads every entry.</summary>
    [Fact]
    public void ADocumentThatBreaksARule_StillOpens()
    {
        var model = Load("rule-ownership-cycle.fdg");

        Assert.Equal(["f1", "f2"], model.Elements.Select(element => element.Id));
        Assert.Equal(2, model.Connections.Count);
    }
}
