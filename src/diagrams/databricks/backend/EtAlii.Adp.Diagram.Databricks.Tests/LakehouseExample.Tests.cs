using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// The shipped lakehouse example: every config parses into the reading its registration names,
/// exercises what the readme promises, and reports zero findings - validation findings on the
/// example are defects (databricks-diagrams Requirement 13.5). Registration resolution itself
/// is the central ExampleRegistrationTests' sweep, which this set joined by existing.
/// </summary>
public class LakehouseExampleTests
{
    /// <summary>The example folder, found by walking up from the test binary.</summary>
    private static string Lakehouse { get; } = Locate();

    private static string Locate()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = IoPath.Combine(directory.FullName, "src", "diagrams", "databricks", "examples", "lakehouse");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("The lakehouse example was not found above the test binary.");
    }

    private static DatabricksDocumentEntry Load(string relativePath)
    {
        var document = DatabricksDocument.Parse(File.ReadAllText(IoPath.Combine(Lakehouse, relativePath)));
        var root = DatabricksYaml.Root(document);
        return new DatabricksDocumentEntry(
            document,
            BundleParser.Parse(root, document),
            JobParser.Parse(root, document),
            PipelineParser.Parse(root, document),
            "",
            0);
    }

    [Theory]
    [InlineData("databricks.yml")]
    [InlineData("resources/nightly-ingest.yml")]
    public async Task EveryConfig_ValidatesClean(string relativePath)
    {
        // Arrange.
        var text = await File.ReadAllTextAsync(IoPath.Combine(Lakehouse, relativePath), TestContext.Current.CancellationToken);
        var validator = new DatabricksValidator(ServiceCollectionAddDatabricksExtension.BundleOrigin);

        // Act.
        var problems = await validator.ValidateAsync(
            new DiagramValidationRequest(text, relativePath, "", "", null),
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(problems);
    }

    [Fact]
    public void TheBundleReading_CarriesTheResourceTargetsAndOverrides()
    {
        // Arrange & act.
        var bundle = Load("databricks.yml").Bundle;

        // Assert.
        Assert.Equal("lakehouse", bundle.Name);
        var resource = Assert.Single(bundle.Resources);
        Assert.Equal(("pipelines", "bronze_to_gold"), (resource.Kind, resource.Key));
        Assert.Equal(2, bundle.Targets.Count);
        Assert.True(bundle.Targets.Single(target => target.Name == "dev").IsDefault);
        // The reference edges of Requirement 3.3: prod overrides the pipeline AND the included
        // job - two override entries, one of which points into an included file.
        var prod = bundle.Targets.Single(target => target.Name == "prod");
        Assert.Equal(2, prod.Overrides.Count);
    }

    [Fact]
    public void ThePipelineReading_IsWhatTheResourceHeaderSelects()
    {
        // Arrange & act.
        var entry = Load("databricks.yml");
        var pipeline = Assert.Single(entry.Pipelines);

        // Assert.
        Assert.Equal("bronze_to_gold", pipeline.Key);
        Assert.Equal(3, pipeline.Libraries.Count);
        Assert.Equal("gold", pipeline.Schema);
        Assert.True(pipeline.Serverless);
        Assert.Single(pipeline.Notifications);
    }

    [Fact]
    public void TheJobReading_ExercisesTheRequirement13Shapes()
    {
        // Arrange & act.
        var job = Assert.Single(Load("resources/nightly-ingest.yml").Jobs);

        // Assert.
        // Four task types, an outcome pair off the condition, and a non-default run_if.
        Assert.Equal(
            new[] { "notebook", "condition", "notebook", "python", "sql" },
            job.Tasks.Select(task => task.Type));
        Assert.Equal("true", job.Tasks.Single(task => task.Key == "publish").DependsOn.Single().Outcome);
        var alert = job.Tasks.Single(task => task.Key == "alert_on_empty");
        Assert.Equal("false", alert.DependsOn.Single().Outcome);
        Assert.Equal("AT_LEAST_ONE_FAILED", alert.RunIf);
        Assert.Single(job.Clusters);
        Assert.Equal("0 0 1 * * ?", job.Schedule);
    }
}
