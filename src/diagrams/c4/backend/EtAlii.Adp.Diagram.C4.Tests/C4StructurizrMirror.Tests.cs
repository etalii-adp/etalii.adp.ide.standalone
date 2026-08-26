using Xunit;

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// The mirror table against the rules it describes. Its whole value is being total: a rule
/// added without a decision about its Structurizr counterpart is a rule that will surprise
/// someone running <c>structurizr inspect</c> over a model ADP called clean.
/// </summary>
public class C4StructurizrMirrorTests
{
    [Fact]
    public void EveryRule_IsEitherMirrored_OrRecordedAsAdpsOwn()
    {
        // Act.
        // Requirement 1.10: the table accounts for every id, so adding a rule forces the
        // decision rather than deferring it.
        var undecided = C4Rules.All
            .Where(rule => !C4StructurizrMirror.MirroredRules.ContainsKey(rule))
            .Where(rule => !C4StructurizrMirror.AdpOnlyRules.ContainsKey(rule))
            .ToArray();

        // Assert.
        Assert.Empty(undecided);
    }

    [Fact]
    public void NoRule_IsBothMirrored_AndRecordedAsAdpsOwn()
    {
        // Act.
        // The two halves are a partition, not two opinions. A rule in both would mean the table
        // says a Structurizr rule exists for it and also that none does.
        var both = C4StructurizrMirror.MirroredRules.Keys
            .Where(C4StructurizrMirror.AdpOnlyRules.ContainsKey)
            .ToArray();

        // Assert.
        Assert.Empty(both);
    }

    [Fact]
    public void TheTable_DescribesNoRuleThatDoesNotExist()
    {
        // Arrange.
        // The other direction: an id left in the table after its rule was renamed would quietly
        // stop being compared, and the comparison would still pass.
        var known = C4Rules.All.ToHashSet(StringComparer.Ordinal);

        // Act.
        var strangers = C4StructurizrMirror.MirroredRules.Keys
            .Concat(C4StructurizrMirror.AdpOnlyRules.Keys)
            .Where(rule => !known.Contains(rule))
            .ToArray();

        // Assert.
        Assert.Empty(strangers);
    }

    [Fact]
    public void EveryMirroredRule_NamesAtLeastOneStructurizrRule()
    {
        // Act and assert.
        // An empty list would read as "mirrored" while comparing against nothing.
        Assert.All(
            C4StructurizrMirror.MirroredRules,
            entry => Assert.NotEmpty(entry.Value));
    }

    [Fact]
    public void EveryReasonGiven_IsAReason()
    {
        // Act and assert.
        // Both tables exist to carry an explanation a reader can weigh. An empty string would
        // satisfy the type and tell them nothing.
        Assert.All(C4StructurizrMirror.AdpOnlyRules, entry => Assert.NotEmpty(entry.Value));
        Assert.All(C4StructurizrMirror.NotImplementedRules, entry => Assert.NotEmpty(entry.Value));
    }

    [Fact]
    public void NoStructurizrRule_IsBothMirrored_AndRecordedAsNotImplemented()
    {
        // Act.
        var both = C4StructurizrMirror.MirroredStructurizrRules
            .Where(C4StructurizrMirror.NotImplementedRules.ContainsKey)
            .ToArray();

        // Assert.
        Assert.Empty(both);
    }

    [Fact]
    public void TheFlattenedSet_MatchesTheTableItIsBuiltFrom()
    {
        // Act and assert.
        // MirroredStructurizrRules is what the reconciliation reads, so it drifting from the
        // table would silently shrink the comparison.
        Assert.Equal(
            C4StructurizrMirror.MirroredRules.Values.SelectMany(rules => rules).Distinct().Order(StringComparer.Ordinal),
            C4StructurizrMirror.MirroredStructurizrRules.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheSixRulesThisSpecAdded_AreAllMirrored()
    {
        // Arrange.
        // Requirement 1.1's list, named here so that dropping one from the table is a failure
        // rather than a quiet narrowing of what gets compared.
        string[] added =
        [
            C4Rules.MissingDeploymentDescription,
            C4Rules.MissingDeploymentTechnology,
            C4Rules.MissingInfrastructureDescription,
            C4Rules.MissingInfrastructureTechnology,
            C4Rules.DisconnectedElement,
            C4Rules.ElementNotOnAnyView,
        ];

        // Act and assert.
        Assert.All(added, rule => Assert.True(
            C4StructurizrMirror.MirroredRules.ContainsKey(rule),
            $"'{rule}' closes a gap against Structurizr and must say which rule it mirrors."));
    }
}
