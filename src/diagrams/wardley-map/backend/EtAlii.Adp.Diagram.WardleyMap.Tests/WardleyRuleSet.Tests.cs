using EtAlii.Adp.Common;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

/// <summary>
/// One test per rule, each from a plain `.owm` string - which is the whole point of the rules
/// being a pure function from a model to problems (Requirement 14.9).
/// </summary>
public sealed class WardleyRuleSetTests
{
    private static IReadOnlyList<DiagramProblem> Validate(string text) =>
        WardleyRuleSet.Validate(WardleyParser.Parse(WardleyDocument.Parse(text)));

    private static DiagramProblem Single(IReadOnlyList<DiagramProblem> problems, string ruleId) =>
        problems.Single(problem => problem.RuleId == ruleId);

    [Fact]
    public void ACleanMapHasNothingToSay()
    {
        // Act.
        var problems = Validate("""
            anchor Business [0.95, 0.63]
            component Kettle [0.43, 0.35]
            Business->Kettle
            evolve Kettle 0.62

            """);

        // Assert.
        Assert.Empty(problems);
    }

    [Fact]
    public void ALinkToSomethingThatIsNotThere_IsAnError_AtTheLineThatSaysIt()
    {
        // Act. Requirement 14.2.
        var problems = Validate("""
            anchor Business [0.95, 0.63]
            component Kettle [0.43, 0.35]
            Kettle->Electricity

            """);

        // Assert. The line is where the fix is made: the name is either a typo to correct or a
        // component to add, and both happen in that statement.
        var problem = Single(problems, WardleyRuleSet.LinkTargetMissingRuleId);
        Assert.Equal(DiagramProblemSeverity.Error, problem.Severity);
        Assert.Contains("Electricity", problem.Message, StringComparison.Ordinal);
        Assert.Contains("Kettle->Electricity", problem.Message, StringComparison.Ordinal);
        Assert.Equal(3u, Assert.IsType<DiagramProblemLineLocation>(problem.Location).Number);
    }

    [Fact]
    public void ALinkWithBothEndsMissing_IsReportedTwice()
    {
        // Act. Two names to fix is two things to do.
        var problems = Validate("anchor Business [0.95, 0.63]\nAlpha->Beta\n");

        // Assert.
        Assert.Equal(2, problems.Count(problem => problem.RuleId == WardleyRuleSet.LinkTargetMissingRuleId));
    }

    [Fact]
    public void AnEvolveOnSomethingThatIsNotThere_IsAnError()
    {
        // Act. Requirement 14.2.
        var problems = Validate("anchor Business [0.95, 0.63]\nevolve Kettle 0.62\n");

        // Assert.
        var problem = Single(problems, WardleyRuleSet.EvolveTargetMissingRuleId);
        Assert.Equal(DiagramProblemSeverity.Error, problem.Severity);
        Assert.Equal(2u, Assert.IsType<DiagramProblemLineLocation>(problem.Location).Number);
    }

    [Fact]
    public void APipelineOnSomethingThatIsNotThere_IsAnError()
    {
        // Act. Requirement 14.2 - pipeline membership counts as a reference.
        var problems = Validate("""
            anchor Business [0.95, 0.63]
            pipeline Kettle
            {
              component Electric Kettle [0.63]
            }

            """);

        // Assert.
        var problem = Single(problems, WardleyRuleSet.PipelineParentMissingRuleId);
        Assert.Equal(DiagramProblemSeverity.Error, problem.Severity);
        Assert.Contains("Kettle", problem.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("component Kettle [1.4, 0.35]", "visibility")]
    [InlineData("component Kettle [0.4, -0.2]", "maturity")]
    public void ACoordinateOffTheScale_IsAnError(string statement, string axis)
    {
        // Act. Requirement 14.3 - the canvas clamps it, so the element would sit somewhere the
        // document does not claim, and that silent disagreement is what this reports.
        var problems = Validate($"anchor Business [0.95, 0.63]\n{statement}\n");

        // Assert.
        var problem = Single(problems, WardleyRuleSet.CoordinateOutOfRangeRuleId);
        Assert.Equal(DiagramProblemSeverity.Error, problem.Severity);
        Assert.Contains(axis, problem.Message, StringComparison.Ordinal);
        Assert.Equal(2u, Assert.IsType<DiagramProblemLineLocation>(problem.Location).Number);
    }

    [Fact]
    public void ACoordinateOffTheScale_IsCaughtOnEverythingTheFormatPositions()
    {
        // Act. Not only on components: a note, a region and an annotation are all placed by the
        // same bounded pair of numbers.
        var problems = Validate("""
            anchor Business [0.95, 0.63]
            component Kettle [0.43, 0.35]
            pipeline Kettle
            {
              component Electric Kettle [1.63]
            }
            evolve Kettle 1.2
            note Off the edge [0.2, 2.0]
            pioneers [0.30, 0.20, 1.55, 0.45]
            accelerator Regulation [0.5, -0.1]
            annotation 1 [0.48, 1.85] Too far

            """);

        // Assert.
        Assert.Equal(6, problems.Count(problem => problem.RuleId == WardleyRuleSet.CoordinateOutOfRangeRuleId));
    }

    [Fact]
    public void TwoComponentsSharingAName_IsAnError_AtTheElementTheyBothClaim()
    {
        // Act. Requirement 14.4.
        var problems = Validate("""
            anchor Business [0.95, 0.63]
            component Kettle [0.43, 0.35]
            component Kettle [0.10, 0.70]

            """);

        // Assert. An element location rather than a line, and not arbitrarily: identity is keyed
        // by name, so two statements with one name reconcile to ONE element and no single line
        // is "the" line. The message names both.
        var problem = Single(problems, WardleyRuleSet.DuplicateNameRuleId);
        Assert.Equal(DiagramProblemSeverity.Error, problem.Severity);
        Assert.Equal("Kettle", Assert.IsType<DiagramProblemElementLocation>(problem.Location).Id);
        Assert.Contains("lines 2, 3", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMapWithNoAnchor_IsAWarningAgainstTheFile()
    {
        // Act. Requirement 14.5 - there is no statement to point at, because the problem is the
        // statement that is not there.
        var problems = Validate("component Kettle [0.43, 0.35]\n");

        // Assert.
        var problem = Single(problems, WardleyRuleSet.AnchorMissingRuleId);
        Assert.Equal(DiagramProblemSeverity.Warning, problem.Severity);
        Assert.Null(problem.Location);
    }

    [Fact]
    public void AnEmptyMapIsNotToldOffForHavingNoAnchor()
    {
        // Act. A map under construction has no anchor yet, and being told off for that while
        // typing would be worse than useless.
        var problems = Validate("title Nothing yet\n");

        // Assert.
        Assert.Empty(problems);
    }

    [Fact]
    public void AUrlNothingDefines_IsAWarning()
    {
        // Act. Requirement 14.6, the half that needs no filesystem.
        var problems = Validate("""
            anchor Business [0.95, 0.63]
            component Kettle [0.43, 0.35] url(handbook)

            """);

        // Assert.
        var problem = Single(problems, WardleyRuleSet.UrlUndefinedRuleId);
        Assert.Equal(DiagramProblemSeverity.Warning, problem.Severity);
        Assert.Contains("handbook", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASemanticallyBrokenMapStillParses_AndKeepsEverythingElse()
    {
        // Act. Requirement 3.5 - a map with a broken link still opens, with the rest rendered.
        const string text = """
            anchor Business [0.95, 0.63]
            component Kettle [0.43, 0.35]
            Kettle->Nothing

            """;
        var map = WardleyParser.Parse(WardleyDocument.Parse(text));

        // Assert.
        Assert.NotEmpty(WardleyRuleSet.Validate(map));
        Assert.Equal(2, map.Components.Count);
        Assert.Single(map.Links);
    }

    [Fact]
    public void EveryRuleIdIsPrefixedWithThisModulesName()
    {
        // Act. Requirement 14.8 - a problem in a log names the module that owns it.
        var problems = Validate("""
            component Kettle [1.43, 0.35]
            component Kettle [0.10, 0.70]
            Kettle->Nothing
            evolve Nothing 0.5
            component Handbook [0.2, 0.2] url(missing)

            """);

        // Assert.
        Assert.NotEmpty(problems);
        Assert.All(problems, problem => Assert.StartsWith("wardley.", problem.RuleId, StringComparison.Ordinal));
    }

    [Fact]
    public void ValidationIsBoundedEvenOnALargeMap()
    {
        // Arrange. Requirement 14.9 - a pathological document must not hang a "Validate all",
        // so the rules are lookups rather than scans. A thousand components linked in a chain
        // is well past what this notation is used at.
        var text = string.Join(
            "\n",
            new[] { "anchor Business [0.95, 0.63]" }
                .Concat(Enumerable.Range(0, 1000).Select(index => $"component C{index} [0.5, 0.5]"))
                .Concat(Enumerable.Range(0, 999).Select(index => $"C{index}->C{index + 1}")));

        var map = WardleyParser.Parse(WardleyDocument.Parse(text + "\n"));

        // Act.
        var started = DateTime.UtcNow;
        var problems = WardleyRuleSet.Validate(map);
        var took = DateTime.UtcNow - started;

        // Assert. Generous on purpose: this is a guard against an accidental quadratic, not a
        // benchmark, and a tight bound would be a test that fails on a busy machine.
        Assert.Empty(problems);
        Assert.True(took < TimeSpan.FromSeconds(2), $"validation took {took}");
    }
}

/// <summary>
/// The one rule that cannot be a pure function: whether a `url` that looks like a file is a file
/// this project has (Requirement 14.6).
/// </summary>
public sealed class WardleyValidatorTests : IDisposable
{
    private readonly string _root = IoPath.Combine(IoPath.GetTempPath(), $"wardley-validator-{Guid.NewGuid():N}");
    private readonly WardleyValidator _validator = new();

    public WardleyValidatorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    [Fact]
    public void ItIsKeyedByThisModulesOrigin()
    {
        // Assert. Requirement 14.1 - core routes it by origin and never names the type.
        Assert.Equal(Diagram.WardleyMap.Origin, _validator.Origin);
    }

    [Fact]
    public async Task ASubmapNamingAMapThatIsNotInTheProject_IsAWarning()
    {
        // Act.
        var problems = await Validate("""
            anchor Business [0.95, 0.63]
            submap Supply [0.5, 0.5] url(supply)
            url supply [supply.owm]

            """);

        // Assert.
        var problem = Assert.Single(problems, candidate => candidate.RuleId == WardleyValidator.SubmapMissingRuleId);
        Assert.Equal(DiagramProblemSeverity.Warning, problem.Severity);
        Assert.Contains("supply.owm", problem.Message, StringComparison.Ordinal);
        Assert.Contains("Supply", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASubmapNamingAMapThatIsThere_IsSilent()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_root, "supply.owm"), "title Supply\n", TestContext.Current.CancellationToken);

        // Act.
        var problems = await Validate("""
            anchor Business [0.95, 0.63]
            submap Supply [0.5, 0.5] url(supply)
            url supply [supply.owm]

            """);

        // Assert.
        Assert.DoesNotContain(problems, problem => problem.RuleId == WardleyValidator.SubmapMissingRuleId);
    }

    [Fact]
    public async Task AnAddressOnTheInternet_IsLeftAlone()
    {
        // Act. Requirement 14.6 - the reference may be to something legitimately outside the
        // workspace, and warning about every one of those would make the panel useless.
        var problems = await Validate("""
            anchor Business [0.95, 0.63]
            component Handbook [0.5, 0.5] url(handbook)
            url handbook [https://example.com/handbook]

            """);

        // Assert.
        Assert.DoesNotContain(problems, problem => problem.RuleId == WardleyValidator.SubmapMissingRuleId);
    }

    [Fact]
    public async Task AnAddressWalkingOutOfTheProject_IsNotFollowed()
    {
        // Act. Containment is core's job everywhere else and this rule's here: a `..` is not a
        // reference this reports on, it is one it refuses to go looking for.
        var problems = await Validate("""
            anchor Business [0.95, 0.63]
            component Secrets [0.5, 0.5] url(secrets)
            url secrets [../../elsewhere/secrets.owm]

            """);

        // Assert.
        Assert.DoesNotContain(problems, problem => problem.RuleId == WardleyValidator.SubmapMissingRuleId);
    }

    [Fact]
    public async Task ItCarriesTheRuleSetsProblemsThrough_Unchanged()
    {
        // Act.
        var problems = await Validate("component Kettle [0.43, 0.35]\nKettle->Nothing\n");

        // Assert.
        Assert.Contains(problems, problem => problem.RuleId == WardleyRuleSet.LinkTargetMissingRuleId);
        Assert.Contains(problems, problem => problem.RuleId == WardleyRuleSet.AnchorMissingRuleId);
    }

    private async Task<IReadOnlyList<DiagramProblem>> Validate(string text)
    {
        var body = IoPath.Combine(_root, "map.owm");
        await File.WriteAllTextAsync(body, text);
        var request = new DiagramValidationRequest(text, "map", _root, body, null);
        return await _validator.ValidateAsync(request, TestContext.Current.CancellationToken);
    }
}
