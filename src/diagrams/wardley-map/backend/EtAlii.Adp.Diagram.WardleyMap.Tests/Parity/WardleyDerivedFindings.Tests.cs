using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.WardleyMap.Tests.Parity;

/// <summary>
/// The findings derived from the DISL definition equal the hand-written validator's (wardley-map-disl-fbl task 7),
/// finding for finding, in order: severity, code, sentence and location, for every document of the parity corpus, with
/// the ids its store holds and the project its request names, and for the documents below, which reach what the corpus
/// does not.
/// </summary>
public class WardleyDerivedFindingsTests
{
    /// <summary>Documents the corpus does not hold, each named for what it reaches.</summary>
    public static TheoryData<string, string> Reached() => new()
    {
        { "links whose ends name nothing", "anchor User [0.9, 0.5]\ncomponent Tea [0.5, 0.5]\nUser->Kettle\nNobody->Tea\nGhost+>Spirit\n" },
        { "an evolve and a pipeline for nothing", "anchor User [0.9, 0.5]\ncomponent Tea [0.5, 0.5]\nevolve Coffee 0.8\npipeline Coffee\n{\n  component Bean [0.4]\n}\n" },
        { "a map without an anchor", "component Tea [0.5, 0.5]\ncomponent Cup [0.4, 0.6]\nTea->Cup\n" },
        { "a legacy pipeline off the map in both axes", "anchor User [0.9, 0.5]\ncomponent Tea [0.5, 0.5]\npipeline Tea [-0.1, 1.4]\n" },
        { "a repeated name with every other finding", "component Tea [1.5, 0.5]\ncomponent Tea [0.5, 0.5]\nTea->Cup\nevolve Cup 0.4\nnot a statement\n" },
    };

    public static TheoryData<string> Documents() => WardleyCorpus.Names();

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryFinding_IsTheHandWrittenValidators(string name)
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = await WardleyCorpus.OpenAsync(name, cancellationToken);
        var request = new DiagramValidationRequest(await File.ReadAllTextAsync(session.Body, cancellationToken), "map", ProjectRoot.From(session.Folder), session.Body, null);

        // Act.
        var expected = Text(new HandWrittenWardleyValidator(session.Store).Validate(request));
        var actual = Text(await new WardleyValidator(session.Store).ValidateAsync(request, cancellationToken));

        // Assert.
        AssertSame(name, expected, actual);
    }

    [Theory]
    [MemberData(nameof(Reached))]
    public async Task EveryFinding_IsTheHandWrittenValidators_WhereTheCorpusDoesNotReach(string name, string text)
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = await WardleyCorpus.OpenAsync(WardleyCorpus.Names().First(), cancellationToken);
        var request = new DiagramValidationRequest(text, "map", ProjectRoot.From(session.Folder), "", null);

        // Act.
        var expected = Text(new HandWrittenWardleyValidator(session.Store).Validate(request));
        var actual = Text(await new WardleyValidator(session.Store).ValidateAsync(request, cancellationToken));

        // Assert.
        AssertSame(name, expected, actual);
    }

    /// <summary>The documents reach every code the tool gives, so the comparisons above are not vacuous.</summary>
    [Fact]
    public async Task TheDocuments_ReachEveryCode()
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        var reached = new HashSet<string>(StringComparer.Ordinal);

        // Act.
        foreach (var name in WardleyCorpus.Names().Select(row => row.Data))
        {
            using var session = await WardleyCorpus.OpenAsync(name, cancellationToken);
            var request = new DiagramValidationRequest(await File.ReadAllTextAsync(session.Body, cancellationToken), "map", ProjectRoot.From(session.Folder), session.Body, null);
            reached.UnionWith(new HandWrittenWardleyValidator(session.Store).Validate(request).Select(problem => problem.RuleId));
        }
        foreach (var text in Reached().Select(row => row.Data.Item2))
        {
            var body = LineDocument.Parse(text);
            var outcome = WardleyParser.Outcome(body);
            reached.UnionWith(HandWrittenWardleyRuleSet.Validate(outcome.Model, []).Select(problem => problem.RuleId));
        }

        // Assert.
        Assert.Equal(
            [
                HandWrittenWardleyRuleSet.AnchorMissingRuleId, HandWrittenWardleyRuleSet.CoordinateOutOfRangeRuleId, HandWrittenWardleyRuleSet.DuplicateNameRuleId,
                HandWrittenWardleyRuleSet.EvolveTargetMissingRuleId, WardleyValidator.LinePassedOverRuleId, HandWrittenWardleyRuleSet.LinkTargetMissingRuleId,
                HandWrittenWardleyRuleSet.PipelineParentMissingRuleId, WardleyValidator.SubmapMissingRuleId, HandWrittenWardleyRuleSet.UrlUndefinedRuleId,
            ],
            reached.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// No finding the tool does not give today (Q4): over the documents a built-in of the evaluator would otherwise report
    /// on (a repeated name, a coordinate off the map, an element evolved twice, a pipeline never closed), the derived
    /// findings hold nothing the hand-written validator does not, no built-in's own code and no second sentence for one
    /// line. A repeated name cannot raise <c>std.duplicateId</c> even when it is switched on, because every holder has an
    /// id of its own; the definition's settings of its built-ins are what this holds to.
    /// </summary>
    [Theory]
    [InlineData("same-key-twice.owm")]
    [InlineData("out-of-range.owm")]
    [InlineData("evolve-twice.owm")]
    [InlineData("unclosed-pipeline.owm")]
    public async Task NoFinding_TheToolDoesNotGiveToday(string fixture)
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        var name = WardleyCorpus.Names().Select(row => row.Data).Single(candidate => candidate.EndsWith("/Fixtures/" + fixture, StringComparison.Ordinal));
        using var session = await WardleyCorpus.OpenAsync(name, cancellationToken);
        var request = new DiagramValidationRequest(await File.ReadAllTextAsync(session.Body, cancellationToken), "map", ProjectRoot.From(session.Folder), session.Body, null);

        // Act.
        var expected = Text(new HandWrittenWardleyValidator(session.Store).Validate(request));
        var actual = Text(await new WardleyValidator(session.Store).ValidateAsync(request, cancellationToken));

        // Assert.
        Assert.Empty(actual.Except(expected, StringComparer.Ordinal));
    }

    private static void AssertSame(string name, List<string> expected, List<string> actual) =>
        Assert.True(
            expected.SequenceEqual(actual, StringComparer.Ordinal),
            $"{name}:\nexpected {string.Join("\n         ", expected)}\nactual   {string.Join("\n         ", actual)}");

    private static List<string> Text(IReadOnlyList<DiagramProblem> problems) =>
        [.. problems.Select(problem => $"{problem.Severity} | {problem.RuleId} | {problem.Message} | {problem.Location}")];
}
