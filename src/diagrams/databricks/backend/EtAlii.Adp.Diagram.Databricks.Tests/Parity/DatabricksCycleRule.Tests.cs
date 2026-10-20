using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks.Tests.Parity;

/// <summary>
/// The job definition's cycle rule (databricks-disl-fbl, task 4, finding A4) against the module's own finder, kept as the oracle since task 7: the
/// <c>CycleMembers</c> of <see cref="DatabricksRuleSet"/>, over every job of the corpus: the fixtures and the shipped
/// example. The rule is <c>inCycle</c> on a task or on one it is <c>reachable</c> from, for the first holder of a key;
/// the module removes every task whose dependencies are all gone and reports what remains.
/// </summary>
/// <remarks>
/// <para>
/// <b>The definition is the one the module embeds</b>, <c>definition/databricks-job.dis</c> as
/// <c>src/diagrams/tools/bundle-disl.sh</c> bundled it, loaded by <see cref="BundledDefinition.Load(System.Reflection.Assembly, string)"/>,
/// which refuses bytes that do not hash to the sha256 its provenance records.
/// </para>
/// <para>
/// <b>The model is built from the module's reading</b>, one task node per task with a key and one dependency per
/// <c>depends_on</c> entry, from the first task of the key it names (none when no task has it) to the task the entry is
/// written under. The second holder of a key is a node of its own, as the file holds it.
/// </para>
/// </remarks>
public class DatabricksCycleRuleTests
{
    private const string Cycle = "databricks.cycle";

    private static string Fixtures { get; } = IoPath.Combine(AppContext.BaseDirectory, "Fixtures");

    private static string Lakehouse { get; } = RepositoryPaths.Under("src", "diagrams", "databricks", "examples", "lakehouse");

    private static DislSpecification JobDefinition() => BundledDefinition.Load(typeof(Diagram).Assembly, "databricks-job.dis").Specification;

    /// <summary>Every file of the corpus, by a name a failure can show.</summary>
    public static TheoryData<string> Corpus()
    {
        var files = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(Fixtures).Where(file => IoPath.GetExtension(file) is ".yml" or ".json").Order(StringComparer.Ordinal))
        {
            files.Add(IoPath.GetFileName(file));
        }
        foreach (var file in Directory.EnumerateFiles(Lakehouse, "*.yml", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            files.Add("lakehouse/" + IoPath.GetRelativePath(Lakehouse, file).Replace('\\', '/'));
        }
        return files;
    }

    private static async Task<IReadOnlyList<JobModel>> Jobs(string name)
    {
        var path = name.StartsWith("lakehouse/", StringComparison.Ordinal)
            ? IoPath.Combine(Lakehouse, name["lakehouse/".Length..])
            : IoPath.Combine(Fixtures, name);
        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        return DatabricksModel.Outcome(LineDocument.Parse(text)).Model.Jobs;
    }

    /// <summary>The keys of the tasks the module reports on a circle, in the order it reports them.</summary>
    private static IReadOnlyList<string> ModuleCycle(JobModel job) =>
    [
        .. DatabricksRuleSet.Judge(new DatabricksModel(BundleModel.Empty, [job], []))
            .Where(problem => problem.RuleId == Cycle)
            .Select(problem => ((DiagramProblemLineLocation)problem.Location!).Number)
            .Select(line => job.Tasks.First(task => task.Lines.Start + 1 == line).Key),
    ];

    /// <summary>The keys of the tasks the definition's rule reports, in the order of the tasks in the file.</summary>
    private static IReadOnlyList<string> DefinitionCycle(DislSpecification specification, JobModel job)
    {
        var diagram = new DislDiagram(specification);
        var tasks = job.Tasks.Where(task => task.Key.Length > 0).ToList();
        var nodes = tasks.Select(task => diagram.AddNode("Task", "task:" + task.Key, new Dictionary<string, object?> { ["key"] = task.Key })).ToList();
        for (var index = 0; index < tasks.Count; index++)
        {
            foreach (var dependency in tasks[index].DependsOn)
            {
                var upstream = tasks.FindIndex(task => task.Key == dependency.TaskKey);
                diagram.AddRelation(
                    "Dependency", $"edge:{dependency.TaskKey}->{tasks[index].Key}", upstream < 0 ? null : nodes[upstream], nodes[index],
                    new Dictionary<string, object?> { ["upstreamKey"] = dependency.TaskKey });
            }
        }

        var reported = ConstraintEvaluator.Evaluate(specification, diagram)
            .Where(finding => finding.Code == Cycle)
            .SelectMany(finding => finding.ElementIds)
            .ToHashSet(StringComparer.Ordinal);
        return [.. tasks.Select(task => "task:" + task.Key).Where(reported.Contains).Distinct(StringComparer.Ordinal).Select(id => id["task:".Length..])];
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public async Task TheDefinitionsCycleRule_ReportsTheTasksTheModuleReports(string file)
    {
        // Arrange.
        var specification = JobDefinition();

        // Act.
        var jobs = await Jobs(file);

        // Assert.
        foreach (var job in jobs)
        {
            Assert.Equal(ModuleCycle(job).Order(StringComparer.Ordinal), DefinitionCycle(specification, job).Order(StringComparer.Ordinal));
        }
    }

    [Theory]
    [InlineData("cycle-downstream.yml", "after_circle,further_after,left,right")]
    [InlineData("findings-job.yml", "loop_a,loop_b")]
    [InlineData("duplicate-key-on-a-circle.yml", "")]
    [InlineData("job.yml", "")]
    public async Task TheComparison_HasSomethingToCompare(string file, string expected)
    {
        // Arrange.
        var specification = JobDefinition();

        // Act.
        var job = Assert.Single(await Jobs(file));

        // Assert.
        Assert.Equal(expected, string.Join(',', DefinitionCycle(specification, job).Order(StringComparer.Ordinal)));
    }
}
