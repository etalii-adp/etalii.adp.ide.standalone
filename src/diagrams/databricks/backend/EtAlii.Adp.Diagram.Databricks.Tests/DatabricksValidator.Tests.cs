using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// The family's rules: each Requirement 12.1 finding with its file line and an actionable
/// sentence, and nothing at all for clean files.
/// </summary>
public class DatabricksValidatorTests
{
    private static async Task<IReadOnlyList<DiagramProblem>> Judge(string fixture)
    {
        var text = File.ReadAllText(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        return await JudgeText(text);
    }

    private static async Task<IReadOnlyList<DiagramProblem>> JudgeText(string text)
    {
        var validator = new DatabricksValidator(ServiceCollectionAddDatabricksExtension.JobOrigin);
        return await validator.ValidateAsync(
            new DiagramValidationRequest(text, "under-test", "", "", null),
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("job.yml")]
    [InlineData("pipeline.json")]
    public async Task ACleanFile_ReportsNothing(string fixture)
    {
        // Arrange & act & assert.
        Assert.Empty(await Judge(fixture));
    }

    [Fact]
    public async Task AFileThatDoesNotParse_ReportsThatAndOnlyThat()
    {
        // Arrange & act.
        var problems = await Judge("broken.yml");

        // Assert.
        // Consequences of an empty model would bury the one thing worth saying.
        var problem = Assert.Single(problems);
        Assert.Equal(DatabricksValidator.UnparseableRuleId, problem.RuleId);
        Assert.Equal(DiagramProblemSeverity.Error, problem.Severity);
        Assert.IsType<DiagramProblemLineLocation>(problem.Location);
    }

    [Fact]
    public async Task TheJobFindings_EachFire_WithTheirLines()
    {
        // Arrange & act.
        var problems = await Judge("findings-job.yml");

        // Assert.
        Assert.Equal(2, problems.Count(problem => problem.RuleId == "databricks.cycle"));
        var missing = Assert.Single(problems, problem => problem.RuleId == "databricks.missing-task");
        Assert.Contains("'nowhere'", missing.Message, StringComparison.Ordinal);
        var dangling = Assert.Single(problems, problem => problem.RuleId == "databricks.dangling-cluster");
        Assert.Contains("'warp_core'", dangling.Message, StringComparison.Ordinal);
        Assert.Single(problems, problem => problem.RuleId == "databricks.duplicate-task-key");
        Assert.Single(problems, problem => problem.RuleId == "databricks.task-key-missing");
        Assert.Equal(6, problems.Count);
        // Every finding points into the file: 1-based lines from the models' own ranges.
        Assert.All(problems, problem =>
            Assert.True(((DiagramProblemLineLocation)problem.Location!).Number >= 1));
    }

    [Fact]
    public async Task TheBundleFindings_DefaultTargetAndStrayOverride()
    {
        // Arrange & act.
        var problems = await Judge("findings-bundle.yml");

        // Assert.
        var noDefault = Assert.Single(problems, problem => problem.RuleId == "databricks.no-default-target");
        Assert.Equal(DiagramProblemSeverity.Warning, noDefault.Severity);
        var stray = Assert.Single(problems, problem => problem.RuleId == "databricks.override-of-undeclared");
        Assert.Contains("'phantom'", stray.Message, StringComparison.Ordinal);
        Assert.Equal(2, problems.Count);
    }

    [Fact]
    public async Task ASecondDefaultTarget_IsAnError()
    {
        // Arrange & act.
        var problems = await JudgeText(
            "bundle:\r\n  name: greedy\r\ntargets:\r\n  dev:\r\n    default: true\r\n  prod:\r\n    default: true\r\n");

        // Assert.
        var second = Assert.Single(problems, problem => problem.RuleId == "databricks.multiple-default-targets");
        Assert.Contains("'prod'", second.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOverride_WithIncludesInPlay_IsNotAccused()
    {
        // Arrange & act.
        // The overridden resource may be declared in an included file this rule cannot see
        // (Requirement 12.3: nothing workspace-dependent) - a rule that cannot know must not accuse.
        var problems = await JudgeText(
            "bundle:\r\n  name: split\r\ninclude:\r\n  - resources/*.yml\r\ntargets:\r\n  dev:\r\n    default: true\r\n    resources:\r\n      jobs:\r\n        elsewhere:\r\n          name: x\r\n");

        // Assert.
        Assert.DoesNotContain(problems, problem => problem.RuleId == "databricks.override-of-undeclared");
    }

    [Fact]
    public async Task ThePipelineFindings_EmptyLibrariesAndSchemaWithoutCatalog()
    {
        // Arrange & act.
        var problems = await Judge("findings-pipeline.json");

        // Assert.
        var empty = Assert.Single(problems, problem => problem.RuleId == "databricks.no-libraries");
        Assert.Equal(DiagramProblemSeverity.Error, empty.Severity);
        var schema = Assert.Single(problems, problem => problem.RuleId == "databricks.schema-without-catalog");
        Assert.Equal(DiagramProblemSeverity.Warning, schema.Severity);
    }

    [Fact]
    public void AddDatabricks_RegistersOneValidatorPerMimeType()
    {
        // Arrange.
        var services = new ServiceCollection();

        // Act.
        services.AddDatabricks();
        var provider = services.BuildServiceProvider();

        // Assert.
        var validators = provider.GetServices<IDiagramValidator>().ToList();
        Assert.Equal(
            new[] { "databricks/bundle", "databricks/job", "databricks/pipeline" },
            validators.Select(validator => validator.Origin.Key));
        Assert.NotNull(provider.GetService<IDatabricksDocumentStore>());
    }
}
