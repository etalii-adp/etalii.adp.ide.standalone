using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.AnsibleStructure.Tests;

/// <summary>
/// One test per rule of Requirement 9.2 - and, carrying at least as much weight, the tests that
/// prove the rules stay quiet when nothing is wrong.
/// </summary>
public class AnsibleRuleSetTests
{
    private static readonly AnsibleProjectReader Reader = new();

    private static IReadOnlyList<DiagramProblem> Judge(string fixture) =>
        AnsibleRuleSet.Judge(Reader.Read(IoPath.Combine("Fixtures", fixture)));

    private static DiagramProblem[] Of(string ruleId) =>
        [.. Judge("broken").Where(problem => problem.RuleId == ruleId)];

    private static string PathOf(DiagramProblem problem) =>
        Assert.IsType<DiagramProblemFileLocation>(problem.Location).RelativePath;

    // ---- silence -----------------------------------------------------------------------------

    [Fact]
    public void TheBestPracticeProject_ValidatesClean()
    {
        // Act and assert.
        // If this ever fires, a rule has started judging a project that is simply correct.
        Assert.Empty(Judge("infrastructure"));
    }

    [Fact]
    public void AnUnrecognisedLayout_ProducesNothingAtAll()
    {
        // Act and assert.
        // Requirement 9.4, and the assertion the unconventional fixture exists for: a team that
        // keeps playbooks in plays/ has an unregistered convention, not a problem. Undrawn is
        // the right answer; a complaint would not be.
        Assert.Empty(Judge("unconventional"));
    }

    // ---- one test per rule -------------------------------------------------------------------

    [Fact]
    public void ARoleWithNoFolder_IsAnError_AtThePlaybookThatNamedIt()
    {
        // Act.
        var problems = Of(AnsibleRules.RoleMissing);

        // Assert.
        var fromPlaybook = Assert.Single(problems, problem => problem.Message.Contains("'absent-role'", StringComparison.Ordinal));
        Assert.Equal(DiagramProblemSeverity.Error, fromPlaybook.Severity);
        // The place to open is the file that named it, never the .adp.
        Assert.Equal("playbooks/deploy.yml", PathOf(fromPlaybook));
        Assert.True(Assert.IsType<DiagramProblemFileLocation>(fromPlaybook.Location).Line > 0);
    }

    [Fact]
    public void ARoleMissingFromAMetaDependency_IsTheSameRule_AtTheMetaFile()
    {
        // Act.
        var problems = Of(AnsibleRules.RoleMissing);

        // Assert.
        var fromMeta = Assert.Single(problems, problem => problem.Message.Contains("'base'", StringComparison.Ordinal));
        Assert.Contains("dependency", fromMeta.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("roles/web/meta/main.yml", PathOf(fromMeta));
    }

    [Fact]
    public void ADanglingImportPlaybook_IsAnError()
    {
        // Act.
        var problems = Of(AnsibleRules.DanglingImport);

        // Assert.
        var import = Assert.Single(problems, problem => problem.Message.Contains("does-not-exist.yml", StringComparison.Ordinal));
        Assert.Equal(DiagramProblemSeverity.Error, import.Severity);
        Assert.Contains("imported", import.Message, StringComparison.Ordinal);
        Assert.Equal("site.yml", PathOf(import));
    }

    [Fact]
    public void ADanglingIncludeTasks_IsTheSameRule_AtTheTaskFile()
    {
        // Act.
        var problems = Of(AnsibleRules.DanglingImport);

        // Assert.
        var include = Assert.Single(problems, problem => problem.Message.Contains("'tls.yml'", StringComparison.Ordinal));
        Assert.Contains("included", include.Message, StringComparison.Ordinal);
        Assert.Equal("roles/web/tasks/main.yml", PathOf(include));
    }

    [Fact]
    public void AHostsPatternNoInventoryDefines_IsAWarning()
    {
        // Act.
        var problems = Of(AnsibleRules.UnmatchedHosts);

        // Assert.
        var unmatched = Assert.Single(problems);
        Assert.Equal(DiagramProblemSeverity.Warning, unmatched.Severity);
        Assert.Contains("'cache'", unmatched.Message, StringComparison.Ordinal);
        Assert.Equal("playbooks/deploy.yml", PathOf(unmatched));
    }

    [Fact]
    public void AnUnparsableFile_IsAnError_InTheParsersOwnWords()
    {
        // Act.
        var problems = Of(AnsibleRules.UnreadableYaml);

        // Assert.
        var unreadable = Assert.Single(problems);
        Assert.Equal(DiagramProblemSeverity.Error, unreadable.Severity);
        Assert.Equal("unparsable.yml", PathOf(unreadable));
        // The parser's message, not ADP's paraphrase of it - and its line, carried as a number.
        Assert.True(Assert.IsType<DiagramProblemFileLocation>(unreadable.Location).Line > 0);
        Assert.DoesNotContain("Line:", unreadable.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyRole_IsAWarning_AtTheRoleFolder()
    {
        // Act.
        var problems = Of(AnsibleRules.EmptyRole);

        // Assert.
        var hollow = Assert.Single(problems);
        Assert.Equal(DiagramProblemSeverity.Warning, hollow.Severity);
        Assert.Contains("'hollow'", hollow.Message, StringComparison.Ordinal);
        Assert.Equal("roles/hollow", PathOf(hollow));
        // A folder has no line to point at, and 0 says so rather than pretending to be line 1.
        Assert.Equal(0u, Assert.IsType<DiagramProblemFileLocation>(hollow.Location).Line);
    }

    // ---- what must never fire ------------------------------------------------------------------

    [Fact]
    public void AnExpressionTarget_IsNeverReportedAsMissing()
    {
        // Act.
        var problems = Judge("broken");

        // Assert.
        // The single most important negative in this rule set: a rule that treated unknown as
        // wrong would fire on every parameterised role in every real repository.
        Assert.DoesNotContain(problems, problem => problem.Message.Contains("{{", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, problem => problem.Message.Contains("role_name", StringComparison.Ordinal));
    }

    [Fact]
    public void TheBrokenFixture_FiresEachRuleAndNothingElse()
    {
        // Act.
        var fired = Judge("broken").Select(problem => problem.RuleId).Distinct().Order(StringComparer.Ordinal).ToArray();

        // Assert.
        // Every rule has exactly one subject in that tree, so this is both "each rule works"
        // and "no rule invented a sixth problem".
        Assert.Equal(
            [
                AnsibleRules.DanglingImport,
                AnsibleRules.EmptyRole,
                AnsibleRules.RoleMissing,
                AnsibleRules.UnmatchedHosts,
                AnsibleRules.UnreadableYaml,
            ],
            fired);
    }

    [Fact]
    public void EveryProblem_NamesAFileThatExists()
    {
        // Act.
        var root = IoPath.Combine("Fixtures", "broken");
        var problems = Judge("broken");

        // Assert.
        // A location that points nowhere is worse than none: the panel would offer to reveal a
        // file and then fail to find it.
        Assert.All(problems, problem =>
        {
            var path = IoPath.Combine(root, PathOf(problem));
            Assert.True(File.Exists(path) || Directory.Exists(path), $"{PathOf(problem)} does not exist.");
        });
    }

    // ---- the guards that keep the rules from becoming noise ---------------------------------------

    [Theory]
    [InlineData("all")]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("*")]
    public void AnImplicitHostPattern_IsNeverUnmatched(string hosts)
    {
        // Arrange.
        // Ansible resolves these without any inventory entry, so reporting them would fire on
        // ordinary, correct playbooks - which is how a panel teaches people to stop reading it.
        var scratch = Tree(
            ("site.yml", $"---\n- name: A play\n  hosts: {hosts}\n"),
            ("inventories/production/hosts.yml", "---\nall:\n  children:\n    web:\n      hosts:\n        a.example.com:\n"));

        try
        {
            // Act and assert.
            Assert.DoesNotContain(AnsibleRuleSet.Judge(Reader.Read(scratch)), p => p.RuleId == AnsibleRules.UnmatchedHosts);
        }
        finally
        {
            TestFolder.TryDelete(scratch);
        }
    }

    [Fact]
    public void AProjectWithNoInventoryAtAll_IsNotWarnedAboutEveryPlay()
    {
        // Arrange.
        // "No inventory defines it" presupposes there are inventories to do the defining. A team
        // that keeps theirs outside the folder would otherwise be warned about every play.
        var scratch = Tree(("site.yml", "---\n- name: A play\n  hosts: web\n"));

        try
        {
            // Act and assert.
            Assert.DoesNotContain(AnsibleRuleSet.Judge(Reader.Read(scratch)), p => p.RuleId == AnsibleRules.UnmatchedHosts);
        }
        finally
        {
            TestFolder.TryDelete(scratch);
        }
    }

    [Fact]
    public void AGroupThatIsDefined_IsNotWarnedAbout()
    {
        // Arrange.
        var scratch = Tree(
            ("site.yml", "---\n- name: A play\n  hosts: web\n"),
            ("inventories/production/hosts.yml", "---\nall:\n  children:\n    web:\n      hosts:\n        a.example.com:\n"));

        try
        {
            // Act and assert.
            Assert.DoesNotContain(AnsibleRuleSet.Judge(Reader.Read(scratch)), p => p.RuleId == AnsibleRules.UnmatchedHosts);
        }
        finally
        {
            TestFolder.TryDelete(scratch);
        }
    }

    private static string Tree(params (string Path, string Content)[] files)
    {
        var root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        foreach (var (path, content) in files)
        {
            var full = IoPath.Combine(root, path);
            Directory.CreateDirectory(IoPath.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
        return root;
    }
}
