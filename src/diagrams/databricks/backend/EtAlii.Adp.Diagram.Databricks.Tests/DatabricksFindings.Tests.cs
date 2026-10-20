using System.Text;
using System.Text.Json.Nodes;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// The findings of a file joined from the three definitions (databricks-disl-fbl task 7, Requirement 3.3): the file's
/// whichever reading asked (finding A7), in the tool's order (finding A8), and why that order is code.
/// </summary>
public class DatabricksFindingsTests
{
    /// <summary>The codes a job's findings carry, in the tool's order within a task and after.</summary>
    private static readonly string[] _jobCodes =
    [
        "databricks.task-key-missing", "databricks.missing-task", "databricks.dangling-cluster", "databricks.duplicate-task-key", "databricks.cycle",
    ];

    private static async Task<string> FixtureAsync(string name) =>
        await File.ReadAllTextAsync(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name), TestContext.Current.CancellationToken);

    private static async Task<IReadOnlyList<string>> ValidateAsync(string text, ToolOrigin origin, string? registration = null)
    {
        var problems = await new DatabricksValidator(origin).ValidateAsync(
            new DiagramValidationRequest(text, "under-test", default, "under-test.yml", registration),
            TestContext.Current.CancellationToken);
        return [.. problems.Select(problem => $"{(problem.Location is DiagramProblemLineLocation line ? $"line {line.Number}" : "nowhere")} | {problem.RuleId} | {problem.Message}")];
    }

    [Fact]
    public async Task AFileWithTwoJobsAndTwoPipelines_GivesTheSameFindings_UnderEveryReading()
    {
        // Arrange.
        var text = await FixtureAsync("two-jobs-two-pipelines.yml");
        IReadOnlyList<string> expected =
        [
            "line 10 | databricks.dangling-cluster | 'ingest' runs on cluster 'missing_cluster', which this job does not declare. Declare it under job_clusters, or clear the binding for serverless.",
            "line 27 | databricks.missing-task | 'report' depends on 'absent', which is not a task in this job. Fix the key or remove the dependency.",
            "line 46 | databricks.no-default-target | No target says default: true, so every deploy must name one. Mark one target as the default.",
            "line 38 | databricks.schema-without-catalog | The pipeline 'Pipe B' sets a schema but no catalog, so the schema has nowhere to live. Set catalog too.",
        ];

        // Act.
        var readings = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["bundle"] = await ValidateAsync(text, ServiceCollectionAddDatabricksExtension.BundleOrigin),
            ["job"] = await ValidateAsync(text, ServiceCollectionAddDatabricksExtension.JobOrigin),
            ["job, resource job_b"] = await ValidateAsync(text, ServiceCollectionAddDatabricksExtension.JobOrigin, "job_b.adp"),
            ["pipeline"] = await ValidateAsync(text, ServiceCollectionAddDatabricksExtension.PipelineOrigin),
            ["pipeline, resource pipe_a"] = await ValidateAsync(text, ServiceCollectionAddDatabricksExtension.PipelineOrigin, "pipe_a.adp"),
        };

        // Assert.
        Assert.All(readings, reading => Assert.Equal(expected, reading.Value));
    }

    [Fact]
    public async Task TheFindingsOfAJob_GoPerTask_ThenTheDuplicates_ThenTheCircle()
    {
        // Arrange.
        var text = await FixtureAsync("finding-order.yml");

        // Act.
        var findings = await ValidateAsync(text, ServiceCollectionAddDatabricksExtension.JobOrigin);

        // Assert.
        Assert.Equal(
            [
                "line 11 | databricks.task-key-missing",
                "line 16 | databricks.missing-task",
                "line 17 | databricks.missing-task",
                "line 13 | databricks.dangling-cluster",
                "line 22 | databricks.missing-task",
                "line 33 | databricks.dangling-cluster",
                "line 25 | databricks.duplicate-task-key",
                "line 28 | databricks.cycle",
                "line 33 | databricks.cycle",
            ],
            findings.Select(finding => string.Join(" | ", finding.Split(" | ").Take(2))));
    }

    /// <summary>
    /// A second task of one key on a circle (finding A2, task 4): its <c>depends_on</c> belongs to it and not to the first
    /// holder of the key, so the circle the file does not hold is not reported, and the duplicate is.
    /// </summary>
    [Fact]
    public async Task ASecondTaskOfOneKey_KeepsItsOwnDependencies()
    {
        // Arrange.
        var text = await FixtureAsync("duplicate-key-on-a-circle.yml");

        // Act.
        var findings = await ValidateAsync(text, ServiceCollectionAddDatabricksExtension.JobOrigin);

        // Assert.
        Assert.Equal(["line 14 | databricks.duplicate-task-key | The task key 'twin' is used more than once. Every task needs its own."], findings);
    }

    /// <summary>
    /// Finding A8: <c>constraints.order</c> sorts by code and then by line (DISL 0.3 §8.6), so no order of the job's
    /// five codes gives the tool's, which interleaves three of them per task; nor does DISL's order without one. The
    /// fixture of task 1 for A8 is the evidence, which is why <see cref="DatabricksFindings"/> orders them in code. The
    /// tool's order is the hand-written rule set's, the oracle.
    /// </summary>
    [Fact]
    public async Task ConstraintsOrderAlone_DoesNotGiveTheToolsOrder()
    {
        // Arrange.
        var text = await FixtureAsync("finding-order.yml");
        var today = Parity.DatabricksRuleSet.Judge(DatabricksModel.Read(LineDocument.Parse(text)))
            .Select(problem => $"line {((DiagramProblemLineLocation)problem.Location!).Number} | {problem.RuleId}")
            .ToList();
        var root = JsonNode.Parse(BundledDefinition.Load(typeof(Diagram).Assembly, "databricks-job.dis").Specification.Root.GetRawText())!;
        var orders = Permutations(_jobCodes).Select(order => (IReadOnlyList<string>?)order).Append(null).ToList();

        // Act.
        var derived = new List<IReadOnlyList<string>>();
        foreach (var order in orders)
        {
            derived.Add(OrderedBy(root, order, text));
        }

        // Assert.
        Assert.Equal(121, derived.Count);
        Assert.Equal(_jobCodes.Order(StringComparer.Ordinal), today.Select(finding => finding.Split(" | ")[1]).Distinct().Order(StringComparer.Ordinal));
        Assert.All(derived, findings => Assert.Equal(today.Order(StringComparer.Ordinal), findings.Order(StringComparer.Ordinal)));
        Assert.DoesNotContain(derived, findings => findings.SequenceEqual(today, StringComparer.Ordinal));
    }

    /// <summary>The job definition's findings over <paramref name="text"/>'s one job, with <c>constraints.order</c> set to <paramref name="order"/> (none for null).</summary>
    private static List<string> OrderedBy(JsonNode root, IReadOnlyList<string>? order, string text)
    {
        var copy = root.DeepClone();
        var constraints = copy["constraints"]!.AsObject();
        constraints.Remove("order");
        if (order is not null)
        {
            constraints["order"] = new JsonArray([.. order.Select(code => (JsonNode?)JsonValue.Create(code))]);
        }

        var specification = DislLoader.Load(Encoding.UTF8.GetBytes(copy.ToJsonString()), DatabricksFreeKey.Plugins()).Specification!;
        var job = Assert.Single(DatabricksModel.Read(LineDocument.Parse(text)).Jobs);
        var keyless = job.Tasks.Where(task => task.Key.Length == 0).Select(task => DislReaderFinding.Unreadable("no task_key", task.Lines.Start + 1)).ToList();
        var diagram = DatabricksDisl.Job(job, specification, "finding-order.yml").Diagram;
        return [.. ConstraintEvaluator.Evaluate(specification, diagram, new DislConstraintOptions(ReaderFindings: keyless)).Select(finding => $"line {finding.Line} | {finding.Code}")];
    }

    private static IEnumerable<string[]> Permutations(string[] items) =>
        items.Length <= 1
            ? [items]
            : items.SelectMany((item, index) => Permutations([.. items.Take(index), .. items.Skip(index + 1)]).Select(rest => (string[])[item, .. rest]));
}
