using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.Databricks.Tests.Parity;

/// <summary>
/// The findings derived from the three DISL definitions equal the hand-written rule set's (databricks-disl-fbl task 7,
/// Requirements 3.3 and 3.4), finding for finding, in order: line, severity, code and sentence, under each tool type's
/// validator, for every document of the parity corpus and for the documents below, which reach what the corpus does not.
/// </summary>
public class DatabricksDerivedFindingsTests
{
    /// <summary>Documents the corpus does not hold, each named for what it reaches.</summary>
    public static TheoryData<string, string> Reached() => new()
    {
        {
            "two keys each written twice, the later first",
            "resources:\n  jobs:\n    twice:\n      tasks:\n        - task_key: a\n        - task_key: b\n        - task_key: b\n        - task_key: a\n"
        },
        {
            "two tasks without a key around one with a key",
            "resources:\n  jobs:\n    keyless:\n      tasks:\n        - notebook_task:\n            notebook_path: a\n        - task_key: kept\n          job_cluster_key: nowhere\n        - notebook_task:\n            notebook_path: b\n"
        },
        {
            "a dependency written without a key",
            "resources:\n  jobs:\n    unnamed:\n      tasks:\n        - task_key: a\n          depends_on:\n            - outcome: \"true\"\n"
        },
        {
            "two jobs, a bundle and two pipelines, each with findings",
            "bundle:\n  name: everything\n"
            + "targets:\n  dev:\n    default: true\n  prod:\n    default: true\n    resources:\n      jobs:\n        ghost: {}\n"
            + "resources:\n  jobs:\n    first:\n      tasks:\n        - task_key: a\n          depends_on:\n            - task_key: gone\n    second:\n      tasks:\n        - task_key: a\n          job_cluster_key: none\n"
            + "  pipelines:\n    one:\n      name: One\n      schema: s\n    two:\n      name: Two\n"
        },
        {
            "a task that depends on itself, twice",
            "resources:\n  jobs:\n    self:\n      tasks:\n        - task_key: a\n          depends_on:\n            - task_key: a\n            - task_key: a\n        - task_key: b\n          depends_on:\n            - task_key: a\n"
        },
        {
            "an override of an undeclared resource beside includes",
            "bundle:\n  name: included\ninclude:\n  - resources/*.yml\ntargets:\n  dev:\n    resources:\n      jobs:\n        elsewhere: {}\n"
        },
        {
            "an override of a declared resource and of an undeclared one",
            "bundle:\n  name: declared\nresources:\n  jobs:\n    here:\n      tasks: []\ntargets:\n  dev:\n    default: true\n    resources:\n      jobs:\n        here: {}\n        there: {}\n      pipelines:\n        here: {}\n"
        },
        {
            "one line of flow collections",
            "resources: {jobs: {flow: {tasks: [{task_key: a, depends_on: [{task_key: x}], job_cluster_key: c}, {task_key: b, depends_on: [{task_key: y}]}]}}}\n"
        },
    };

    public static TheoryData<string> Documents() => DatabricksParityCorpus.Documents();

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryFinding_IsTheHandWrittenRuleSets(string document)
    {
        // Arrange.
        var entry = await DatabricksParityCorpus.EntryAsync(document, TestContext.Current.CancellationToken);

        // Act and assert.
        await AssertSameAsync(document, entry.Document.Text);
    }

    [Theory]
    [MemberData(nameof(Reached))]
    public async Task EveryFinding_IsTheHandWrittenRuleSets_WhereTheCorpusDoesNotReach(string name, string text) =>
        await AssertSameAsync(name, text);

    /// <summary>The documents reach every code the hand-written rule set has, and the file that does not parse, so the comparison above is not vacuous.</summary>
    [Fact]
    public async Task TheDocuments_ReachEveryCode()
    {
        // Arrange.
        var texts = new List<string>();
        foreach (var document in Documents())
        {
            texts.Add((await DatabricksParityCorpus.EntryAsync(document.Data, TestContext.Current.CancellationToken)).Document.Text);
        }

        // Act.
        var reached = texts.Concat(Reached().Select(row => row.Data.Item2))
            .SelectMany(Expected)
            .Select(line => line.Split(" | ")[2])
            .ToHashSet(StringComparer.Ordinal);

        // Assert.
        Assert.Equal(
            [
                "databricks.cycle", "databricks.dangling-cluster", "databricks.duplicate-task-key", "databricks.missing-task",
                "databricks.multiple-default-targets", "databricks.no-default-target", "databricks.no-libraries",
                "databricks.override-of-undeclared", "databricks.schema-without-catalog", "databricks.task-key-missing",
                "databricks.unparseable",
            ],
            reached.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// A task without a key is reported and not read as a task (Requirement 4.3), so what the hand-written rule set
    /// also said of it - that it depends on a missing task and runs on a missing cluster, naming it <c>''</c> - is not
    /// derived. No document of the corpus reaches it; it is a change the requirements do not name, raised for the
    /// user's ruling, and this pins both sides of it.
    /// </summary>
    [Fact]
    public async Task ATaskWithoutAKey_IsNotJudgedForItsDependenciesOrItsCluster()
    {
        // Arrange.
        const string text = "resources:\n  jobs:\n    keyless:\n      tasks:\n        - job_cluster_key: nowhere\n          depends_on:\n            - task_key: nothing\n        - task_key: kept\n";
        const string keyMissing = "line 5 | Error | databricks.task-key-missing | A task has no task_key. Give it one, or nothing can depend on it.";

        // Act.
        var problems = await new DatabricksValidator(ServiceCollectionAddDatabricksExtension.JobOrigin).ValidateAsync(
            new DiagramValidationRequest(text, "parity", default, "parity.yml", null),
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(
            [
                keyMissing,
                "line 7 | Error | databricks.missing-task | '' depends on 'nothing', which is not a task in this job. Fix the key or remove the dependency.",
                "line 5 | Error | databricks.dangling-cluster | '' runs on cluster 'nowhere', which this job does not declare. Declare it under job_clusters, or clear the binding for serverless.",
            ],
            Expected(text));
        Assert.Equal([keyMissing], problems.Select(Text));
    }

    private static async Task AssertSameAsync(string name, string text)
    {
        var expected = Expected(text);
        foreach (var origin in DatabricksParityCorpus.Origins)
        {
            var problems = await new DatabricksValidator(origin).ValidateAsync(
                new DiagramValidationRequest(text, "parity", default, "parity.yml", null),
                TestContext.Current.CancellationToken);
            var actual = problems.Select(Text).ToList();
            Assert.True(
                expected.SequenceEqual(actual, StringComparer.Ordinal),
                $"{name} | {origin.Key}:\nexpected {string.Join("\n         ", expected)}\nactual   {string.Join("\n         ", actual)}");
        }
    }

    /// <summary>What the validator answered before the findings were derived: the parser's sentence for a file that does not parse, else the hand-written rule set's.</summary>
    private static List<string> Expected(string text)
    {
        var outcome = DatabricksModel.Outcome(LineDocument.Parse(text));
        IReadOnlyList<DiagramProblem> problems = outcome.IsUsable
            ? DatabricksRuleSet.Judge(outcome.Model)
            :
            [
                new DiagramProblem(
                    DiagramProblemSeverity.Error,
                    $"This is not configuration that can be read: {outcome.Unreadable}",
                    DatabricksValidator.UnparseableRuleId,
                    new DiagramProblemLineLocation((uint)outcome.Problems[0].Line + 1)),
            ];
        return [.. problems.Select(Text)];
    }

    private static string Text(DiagramProblem problem) =>
        $"{(problem.Location is DiagramProblemLineLocation line ? $"line {line.Number}" : "nowhere")} | {problem.Severity} | {problem.RuleId} | {problem.Message}";
}
