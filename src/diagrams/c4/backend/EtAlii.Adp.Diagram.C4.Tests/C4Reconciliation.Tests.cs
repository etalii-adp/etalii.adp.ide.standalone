using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// ADP's verdict against Structurizr's, on every fixture, with no JDK anywhere.
/// </summary>
/// <remarks>
/// <para>
/// This is the guarantee the whole spec turns on. ADP takes a position on what a good C4 model
/// is, and so does Structurizr; where both have an opinion they must agree, or a model ADP calls
/// clean turns up a page of findings the first time a colleague runs Structurizr over it.
/// </para>
/// <para>
/// Only the mirrored rules are compared, in both directions. A rule that is ADP's alone, or one
/// of Structurizr's that ADP deliberately does not implement, is a decision recorded in
/// <see cref="C4StructurizrMirror"/> rather than a disagreement - and the totality tests in
/// <c>C4StructurizrMirrorTests</c> are what stop a rule slipping out of the comparison by having
/// no entry at all.
/// </para>
/// <para>
/// Rule ids only. ADP words its problems for the person editing the document and Structurizr
/// words its for the person inspecting a workspace; requiring the sentences to match would be
/// requiring ADP to write Structurizr's.
/// </para>
/// <para>
/// One limit, stated rather than discovered later: the comparison runs at the granularity of
/// ADP's rules, which is one per question where Structurizr has one per element kind. So a
/// verdict whose <c>model.person.description</c> was somehow replaced by
/// <c>model.container.description</c> would still reconcile, because both are
/// <c>c4.missing-description</c> to ADP. There is no information in ADP's output to tell them
/// apart, and inventing four rules to gain that resolution would be letting the test shape the
/// product. Dropping a finding of a rule ADP names separately does fail, which is checked.
/// </para>
/// </remarks>
public class C4ReconciliationTests
{
    /// <summary>Every fixture with a recorded verdict, which is every fixture in the corpus.</summary>
    public static TheoryData<string> Corpus() => C4DocumentTests.Corpus();

    private static C4Workspace Parse(string fixture) =>
        C4Parser.Parse(C4Document.Parse(File.ReadAllText(IoPath.Combine("Fixtures", fixture))));

    [Theory]
    [MemberData(nameof(Corpus))]
    public void AdpAndStructurizr_AgreeOnEveryMirroredRule(string fixture)
    {
        // Arrange.
        var verdict = C4Verdict.Read(fixture);

        // Compared in ADP's vocabulary rather than Structurizr's, because the mapping is
        // many-to-one in that direction and one-to-many in the other. Structurizr names a rule
        // per element kind and ADP names one per question, so `c4.missing-description` answers
        // for four of Structurizr's. Expanding ADP's one report into all four would claim ADP
        // had reported a missing component description because it reported a missing person
        // description - a disagreement invented by the comparison rather than found by it.
        var adp = C4RuleSet.Validate(Parse(fixture))
            .Select(problem => problem.RuleId)
            .Where(C4StructurizrMirror.MirroredRules.ContainsKey)
            .ToHashSet(StringComparer.Ordinal);

        var structurizr = verdict.RuleIds
            .Select(AdpRuleMirroring)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        // Act.
        // Both directions. Missing means ADP is silent about something Structurizr reports;
        // extra means ADP reports something Structurizr does not, on a rule that claims to
        // mirror it. Either is a defect in one of them.
        var adpIsSilentAbout = structurizr.Except(adp).Order(StringComparer.Ordinal).ToArray();
        var adpAloneReports = adp.Except(structurizr).Order(StringComparer.Ordinal).ToArray();

        // Assert.
        Assert.True(
            adpIsSilentAbout.Length == 0 && adpAloneReports.Length == 0,
            $"'{fixture}' (verdict recorded by structurizr-cli {verdict.CliVersion}): " +
            $"Structurizr reports [{string.Join(", ", adpIsSilentAbout)}] where ADP is silent, and " +
            $"ADP reports [{string.Join(", ", adpAloneReports)}] where Structurizr is. " +
            $"Only mirrored rules are compared, so this is a real disagreement rather than a difference of scope. " +
            C4Verdict.RegenerationHint);
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void EveryFixture_HasARecordedVerdict(string fixture)
    {
        // Act and assert.
        // Stated separately from the comparison so that adding a fixture without recording a
        // verdict fails as the omission it is, rather than as a puzzling comparison failure.
        Assert.True(
            C4Verdict.Exists(fixture),
            $"'{fixture}' is in the corpus but has no recorded Structurizr verdict. {C4Verdict.RegenerationHint}");
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void EveryVerdict_NamesTheCliThatProducedIt(string fixture)
    {
        // Act.
        var verdict = C4Verdict.Read(fixture);

        // Assert.
        // Requirement 3.3. A baseline that cannot say which version produced it cannot be judged
        // stale, and a baseline nobody can judge stale is a fiction.
        Assert.NotEmpty(verdict.CliVersion);
    }

    [Fact]
    public void TheWorkedExample_IsWhereThisSpecStarted()
    {
        // Arrange.
        // The 26 findings that showed the narrowings were wrong. Pinned as a number because it is
        // the fact the correction rests on, and because a reader of the corrected documents in
        // c4-diagrams should be able to run one test and see it.
        var verdict = C4Verdict.Read("big-bank-plc.dsl");

        // Act and assert, step by step.
        Assert.Equal(26, verdict.Findings.Count);
        Assert.Contains("model.deploymentnode.description", verdict.RuleIds);
        Assert.Contains("model.relationship.technology", verdict.RuleIds);
    }

    [Fact]
    public void ACorruptedVerdict_FailsRatherThanBeingSkipped()
    {
        // Arrange.
        // A verdict that cannot be read is the reconciliation quietly guaranteeing nothing, which
        // is the exact failure the baseline exists to prevent.
        //
        // Read through a path rather than by moving the current directory: that is shared with
        // every test running in parallel, and moving it made two unrelated fixture tests fail
        // at random until this was changed.
        var path = IoPath.Combine(IoPath.GetTempPath(), IoPath.GetRandomFileName() + ".inspect.txt");
        try
        {
            File.WriteAllText(path, "this file has no header and no pipes");

            // Act and assert, step by step.
            var problem = Assert.Throws<FormatException>(() => C4Verdict.ReadFile(path, "corrupt.dsl"));
            Assert.Contains("header", problem.Message, StringComparison.Ordinal);
            Assert.Contains("structurizr inspect", problem.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AMissingVerdict_FailsNamingTheFile()
    {
        // Act and assert.
        var problem = Assert.Throws<FileNotFoundException>(() => C4Verdict.Read("no-such-fixture.dsl"));
        Assert.Contains("no-such-fixture", problem.Message, StringComparison.Ordinal);
        Assert.Contains("structurizr inspect", problem.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The ADP rule that mirrors <paramref name="structurizrRuleId"/>, or <c>null</c> where ADP
    /// deliberately implements none - which <see cref="C4StructurizrMirror.NotImplementedRules"/>
    /// records, so a gap here is a decision rather than an oversight.
    /// </summary>
    private static string? AdpRuleMirroring(string structurizrRuleId) =>
        C4StructurizrMirror.MirroredRules
            .Where(entry => entry.Value.Contains(structurizrRuleId, StringComparer.Ordinal))
            .Select(entry => entry.Key)
            .FirstOrDefault();
}
