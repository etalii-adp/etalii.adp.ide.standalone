using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.HelmChart.Tests;

/// <summary>
/// Every rule's fire AND non-fire case (Requirement 10) - the noise guards are as much the
/// specification as the findings are.
/// </summary>
public class HelmRuleSetTests
{
    private static HelmChart Read(string fixture) =>
        new HelmChartReader().Read(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", fixture));

    [Fact]
    public void TheWellFormedFixture_JudgesClean()
    {
        // Arrange & Act.
        var problems = HelmRuleSet.Judge(Read("well-formed"));

        // Assert.
        // Including that its Unvendored postgres dependency is NOT a finding (R5.2).
        Assert.Empty(problems);
    }

    [Fact]
    public void NotAChart_IsExactlyOneFolderAttributedFinding()
    {
        // Arrange & Act.
        var problems = HelmRuleSet.Judge(HelmChart.NotAChart);

        // Assert.
        var problem = Assert.Single(problems);
        Assert.Equal(HelmRules.NotAChart, problem.RuleId);
        Assert.Equal(DiagramProblemSeverity.Warning, problem.Severity);
        var location = Assert.IsType<DiagramProblemFileLocation>(problem.Location);
        Assert.Equal(".", location.RelativePath);
    }

    [Fact]
    public void TheBrokenFixture_ReportsTheMissingVersionAndTheUnreadableValues()
    {
        // Arrange & Act.
        var problems = HelmRuleSet.Judge(Read("broken"));

        // Assert.
        Assert.Contains(problems, problem => problem is { RuleId: HelmRules.MissingVersion, Severity: DiagramProblemSeverity.Error });
        var unreadable = Assert.Single(problems, problem => problem.RuleId == HelmRules.UnreadableYaml);
        var location = Assert.IsType<DiagramProblemFileLocation>(unreadable.Location);
        Assert.Equal("values.yaml", location.RelativePath);
        Assert.True(location.Line > 0);
        // And no name finding: the broken chart does declare a name.
        Assert.DoesNotContain(problems, problem => problem.RuleId == HelmRules.MissingName);
    }

    [Fact]
    public void TheUnconventionalFixture_ReportsLegacyNotSemVerAndTheUndeclaredArchive()
    {
        // Arrange & Act.
        var problems = HelmRuleSet.Judge(Read("unconventional"));

        // Assert.
        Assert.Contains(problems, problem => problem.RuleId == HelmRules.LegacyChart);
        Assert.Contains(problems, problem => problem.RuleId == HelmRules.VersionNotSemVer);
        var undeclared = Assert.Single(problems, problem => problem.RuleId == HelmRules.UndeclaredVendored);
        var location = Assert.IsType<DiagramProblemFileLocation>(undeclared.Location);
        Assert.Equal("charts/sealed-9.9.9.tgz", location.RelativePath);
        // And the library chart escapes the empty-templates rule (R1.2) - it has no
        // templates folder of its own and that is what a library chart is for.
        Assert.DoesNotContain(problems, problem => problem.RuleId == HelmRules.EmptyTemplates);
    }

    [Fact]
    public void AnApplicationChartWithNoTemplates_Warns_AndALibraryDoesNot()
    {
        // Arrange.
        var application = Minimal("application");
        var library = Minimal("library");

        // Act & Assert.
        Assert.Contains(HelmRuleSet.Judge(application), problem => problem.RuleId == HelmRules.EmptyTemplates);
        Assert.DoesNotContain(HelmRuleSet.Judge(library), problem => problem.RuleId == HelmRules.EmptyTemplates);
    }

    [Fact]
    public void LockRules_StaySilentWithoutALock()
    {
        // Arrange.
        // Dependencies declared, no lock anywhere: the lock appears when someone first runs
        // helm dependency update, and its absence is a state, not a mistake (R10.5).
        var chart = Minimal("application") with
        {
            Dependencies = [Dependency("redis")],
            Templates = [SomeTemplate()],
        };

        // Act & Assert.
        Assert.DoesNotContain(HelmRuleSet.Judge(chart), problem => problem.RuleId == HelmRules.LockDrift);
    }

    [Fact]
    public void LockDrift_FiresBothDirections()
    {
        // Arrange.
        var chart = Minimal("application") with
        {
            Dependencies = [Dependency("declared-only")],
            Templates = [SomeTemplate()],
            Lock = new LockFile("Chart.lock", [new LockEntry("locked-only", "1.0.0", 3)], null),
        };

        // Act.
        var problems = HelmRuleSet.Judge(chart).Where(problem => problem.RuleId == HelmRules.LockDrift).ToArray();

        // Assert.
        Assert.Equal(2, problems.Length);
        Assert.Contains(problems, problem => problem.Message.Contains("'declared-only'", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Message.Contains("'locked-only'", StringComparison.Ordinal));
        Assert.All(problems, problem =>
            Assert.Equal("Chart.lock", Assert.IsType<DiagramProblemFileLocation>(problem.Location).RelativePath));
    }

    [Fact]
    public void AMissingConditionPath_Warns_AndAPresentOneDoesNot()
    {
        // Arrange.
        var missing = Dependency("redis") with { Condition = "redis.enabled", State = ConditionState.Missing };
        var present = Dependency("postgres") with { Condition = "postgres.enabled", State = ConditionState.On };
        var chart = Minimal("application") with
        {
            Dependencies = [missing, present],
            Templates = [SomeTemplate()],
        };

        // Act.
        var problems = HelmRuleSet.Judge(chart);

        // Assert.
        var warning = Assert.Single(problems, problem => problem.RuleId == HelmRules.ConditionMissing);
        Assert.Contains("redis.enabled", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoDependenciesOnOneEffectiveName_IsAnError()
    {
        // Arrange.
        // The same chart mounted twice without distinct aliases: helm itself refuses this.
        var chart = Minimal("application") with
        {
            Dependencies = [Dependency("redis"), Dependency("redis") with { Line = 9 }],
            Templates = [SomeTemplate()],
        };

        // Act.
        var problems = HelmRuleSet.Judge(chart);

        // Assert.
        var collision = Assert.Single(problems, problem => problem.RuleId == HelmRules.NameCollision);
        Assert.Equal(DiagramProblemSeverity.Error, collision.Severity);
        Assert.Equal(9u, Assert.IsType<DiagramProblemFileLocation>(collision.Location).Line);
        // Aliased apart, the same pair is fine.
        var aliased = chart with
        {
            Dependencies = [Dependency("redis") with { Alias = "cache" }, Dependency("redis") with { Alias = "queue" }],
        };
        Assert.DoesNotContain(HelmRuleSet.Judge(aliased), problem => problem.RuleId == HelmRules.NameCollision);
    }

    private static HelmChart Minimal(string chartType) => new(
        IsChart: true,
        new ChartMetadata("sample", "1.0.0", null, "v2", chartType, string.Empty, false, 1, 3),
        MetadataFailure: null,
        Legacy: false,
        Values: [],
        Schema: null,
        Templates: [],
        Crds: null,
        Dependencies: [],
        Vendored: [],
        Lock: null);

    private static DependencyDeclaration Dependency(string name) =>
        new(name, null, "1.0.0", null, null, ConditionState.None, 5);

    private static TemplateFile SomeTemplate() =>
        new("templates/deployment.yaml", TemplateRole.Manifest, new TemplateFacts(["Deployment"], [], [], []));
}
