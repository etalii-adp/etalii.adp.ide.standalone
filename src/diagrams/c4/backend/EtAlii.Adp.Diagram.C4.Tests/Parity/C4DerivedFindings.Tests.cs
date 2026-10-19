using Xunit;

namespace EtAlii.Adp.Diagram.C4.Tests.Parity;

/// <summary>
/// The findings derived from the six DISL definitions equal the ones the module's rule set gives
/// (c4-disl-fbl Requirement 3.3), finding for finding and in order: location, severity, code and
/// sentence, for every document of the parity corpus and for each of the six origins, so the six
/// validators give one list. <see cref="C4RuleSet"/> is the oracle.
/// </summary>
public class C4DerivedFindingsTests
{
    [Fact]
    public async Task EveryFinding_IsTheRuleSets_UnderEveryOrigin()
    {
        // Arrange.
        var readings = await C4Corpus.ReadingsAsync(TestContext.Current.CancellationToken);
        var differences = new List<string>();

        // Act.
        foreach (var reading in readings)
        {
            var expected = Lines(C4RuleSet.Validate(reading.Workspace));
            foreach ((ToolOrigin origin, _) in C4Corpus.Types)
            {
                var actual = Lines(C4Definitions.Problems(reading.Workspace, reading.File, origin));
                if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
                {
                    differences.Add($"{reading.Name} as {origin.Key}:\n  expected {string.Join("\n           ", expected)}\n  actual   {string.Join("\n           ", actual)}");
                }
            }
        }

        // Assert.
        Assert.True(differences.Count == 0, string.Join("\n", differences.Take(20)));
    }

    /// <summary>The corpus reaches every code the rule set has, so the comparison above is not vacuous.</summary>
    [Fact]
    public async Task TheCorpus_ReachesEveryCode()
    {
        // Arrange.
        var readings = await C4Corpus.ReadingsAsync(TestContext.Current.CancellationToken);

        // Act.
        var reached = readings.SelectMany(reading => C4RuleSet.Validate(reading.Workspace)).Select(problem => problem.RuleId).ToHashSet(StringComparer.Ordinal);

        // Assert.
        Assert.Equal(C4Rules.All.Order(StringComparer.Ordinal), reached.Order(StringComparer.Ordinal));
    }

    /// <summary>A finding as the transcript writes it: where, how severe, which rule, and the sentence.</summary>
    internal static IReadOnlyList<string> Lines(IEnumerable<DiagramProblem> problems) =>
        [.. problems.Select(problem => $"{Where(problem.Location)} | {problem.Severity} | {problem.RuleId} | {problem.Message}")];

    private static string Where(DiagramProblemLocation? location) => location switch
    {
        DiagramProblemLineLocation line => $"line {line.Number}",
        DiagramProblemElementLocation element => $"element {element.Id}",
        null => "nowhere",
        _ => location.ToString() ?? "",
    };
}
