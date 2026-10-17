using Path = System.IO.Path;
using Xunit;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests.Parity;

/// <summary>
/// The findings derived from the DISL definition equal the hand-written rule set's
/// (azure-devops-pipeline-disl-fbl task 7), finding for finding and in order: severity, code, message and
/// location, for every document of the parity corpus and for the documents below, which reach what the
/// corpus does not.
/// </summary>
public class PipelineDerivedFindingsTests
{
    public static TheoryData<string> Documents() => [.. PipelineReadings.Paths()];

    /// <summary>Documents the corpus does not hold, each named for what it reaches.</summary>
    public static TheoryData<string, string> Reached() => new()
    {
        { "no stage at all", "trigger:\n  - main\n" },
        { "not YAML", "stages:\n  - stage: A\n   jobs: [\n" },
        { "two stages waiting for each other", "stages:\n" + Stage("A", "dependsOn: B") + Stage("B", "dependsOn: A") },
        { "three stages in a loop written out of order", "stages:\n" + Stage("C", "dependsOn: A") + Stage("A", "dependsOn: B") + Stage("B", "dependsOn: C") },
        { "the only start waits for a name that names nothing", "stages:\n" + Stage("A", "dependsOn: Missing") + Stage("B", "dependsOn: A") },
        { "display names on a loop and a dangling dependency", "stages:\n" + Stage("A", "displayName: Alpha\n    dependsOn: A") + Stage("B", "displayName: Beta\n    dependsOn: Nope") },
        { "an unnamed stage nothing can reach", "stages:\n" + Stage("A", "dependsOn: B") + Stage("B", "dependsOn: A") + "  - jobs:\n      - job: J\n        steps:\n          - script: echo\n" },
    };

    /// <summary>Job findings of both kinds in two stages, the one place the derived order is not the rule set's.</summary>
    private static readonly string JobFindingsInTwoStages =
        "stages:\n  - stage: S1\n    jobs:\n" + Job("J1", "dependsOn: J2") + Job("J2", "dependsOn: J1") + Job("J3", "dependsOn: Gone")
        + "  - stage: S2\n    jobs:\n" + Job("K1", "displayName: Kay\n        dependsOn: K1") + Job("K2", "dependsOn: Lost");

    private static string Stage(string name, string keys) =>
        $"  - stage: {name}\n    {keys}\n    jobs:\n      - job: Only\n        steps:\n          - script: echo\n";

    private static string Job(string name, string keys) =>
        $"      - job: {name}\n        {keys}\n        steps:\n          - script: echo\n";

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryFinding_IsTheHandWrittenRuleSets(string path) =>
        await AssertSameAsync(path, await File.ReadAllTextAsync(Path.Combine(PipelineCorpus.SourceFolder, path), TestContext.Current.CancellationToken));

    [Theory]
    [MemberData(nameof(Reached))]
    public async Task EveryFinding_IsTheHandWrittenRuleSets_WhereTheCorpusDoesNotReach(string name, string text) =>
        await AssertSameAsync(name, text);

    /// <summary>
    /// The rule set reports job findings stage by stage, a stage's dangling dependencies and then its
    /// cycles; DISL orders findings by rule, so the definition reports every dangling job dependency and
    /// then every job cycle. The findings are the same and only that order differs, which no corpus
    /// document reaches (recommended option (a) of the task 7 question to Peter, pending his answer;
    /// recorded for task 12).
    /// </summary>
    [Fact]
    public async Task JobFindingsInTwoStages_AreTheRuleSets_GroupedByKindRatherThanByStage()
    {
        var expected = HandWrittenPipeline.Findings(JobFindingsInTwoStages).Select(Text).ToList();
        var actual = (await new PipelineValidator().ValidateAsync(new DiagramValidationRequest(JobFindingsInTwoStages, "parity", "", "", null), TestContext.Current.CancellationToken)).Select(Text).ToList();

        Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
        Assert.NotEqual(expected, actual);
        Assert.Equal(
            expected.Where(line => line.Contains(PipelineRules.DanglingDependency, StringComparison.Ordinal))
                .Concat(expected.Where(line => line.Contains(PipelineRules.Cycle, StringComparison.Ordinal))),
            actual);
    }

    /// <summary>The documents reach every code the hand-written rule set has, so the comparison above is not vacuous.</summary>
    [Fact]
    public async Task TheDocuments_ReachEveryCode()
    {
        var texts = new List<string>();
        foreach (var path in PipelineReadings.Paths())
        {
            texts.Add(await File.ReadAllTextAsync(Path.Combine(PipelineCorpus.SourceFolder, path), TestContext.Current.CancellationToken));
        }

        var reached = texts.Concat(Reached().Select(row => row.Data.Item2))
            .SelectMany(HandWrittenPipeline.Findings)
            .Select(problem => problem.RuleId)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            new[]
            {
                PipelineRules.Cycle, PipelineRules.DanglingDependency, PipelineRules.NoStartingStage, PipelineRules.TemplateNotFollowed,
                PipelineRules.Unnamed, PipelineRules.Unreachable, PipelineValidator.UnparseableRuleId,
            }.Order(StringComparer.Ordinal),
            reached.Order(StringComparer.Ordinal));
    }

    private static async Task AssertSameAsync(string name, string text)
    {
        var expected = HandWrittenPipeline.Findings(text).Select(Text).ToList();
        var actual = (await new PipelineValidator().ValidateAsync(new DiagramValidationRequest(text, "parity", "", "", null), TestContext.Current.CancellationToken)).Select(Text).ToList();
        Assert.True(
            expected.SequenceEqual(actual, StringComparer.Ordinal),
            $"{name}:\nexpected {string.Join("\n         ", expected)}\nactual   {string.Join("\n         ", actual)}");
    }

    private static string Text(DiagramProblem problem) => $"{Location(problem.Location)} | {problem.Severity} | {problem.RuleId} | {problem.Message}";

    private static string Location(DiagramProblemLocation? location) => location switch
    {
        DiagramProblemLineLocation line => $"line {line.Number}",
        DiagramProblemElementLocation element => $"element {element.Id}",
        null => "nowhere",
        _ => location.ToString() ?? "",
    };
}
