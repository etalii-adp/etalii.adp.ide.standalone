using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests;

/// <summary>
/// Requirement 5.5 at the seam: a document that breaks the rules reaches the Errors and Warnings
/// panel, not only the rule set's own tests.
/// </summary>
/// <remarks>
/// <b>The registration case is the one this file exists for.</b> The rules were tested from the
/// start through <see cref="FdgRuleSet"/>, and every one of those tests stayed green while no
/// validator was registered and nothing a person could see reported anything. A module whose
/// validator is not in the container is indistinguishable, from the rule tests, from one that
/// reports correctly.
/// </remarks>
public class FdgValidatorTests
{
    private static DiagramValidationRequest RequestFor(string text) => new(
        text, "graph", "/project", "/project/graph.fdg", "/project/graph.adp");

    private static async Task<IReadOnlyList<DiagramProblem>> ProblemsIn(string fixture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        return await new FdgValidator().ValidateAsync(RequestFor(text), TestContext.Current.CancellationToken);
    }

    public static TheoryData<string, string> EveryRule => new()
    {
        { "rule-forbidden-link.fdg", FdgRuleIds.ForbiddenLink },
        { "rule-self-link.fdg", FdgRuleIds.SelfLink },
        { "rule-second-parent.fdg", FdgRuleIds.SecondParent },
        { "rule-second-shows.fdg", FdgRuleIds.SecondShows },
        { "rule-ownership-cycle.fdg", FdgRuleIds.OwnershipCycle },
        { "rule-dangling-reference.fdg", FdgRuleIds.DanglingReference },
        { "rule-duplicate-id.fdg", FdgRuleIds.DuplicateId },
        { "rule-unreadable-entry.fdg", FdgRuleIds.UnreadableEntry },
    };

    /// <summary>The module's one registration call puts this validator in the container, for this origin.</summary>
    [Fact]
    public void AddFunctionalDecompositionGraph_RegistersTheValidator()
    {
        var services = new ServiceCollection();
        services.AddFunctionalDecompositionGraph();
        using var provider = services.BuildServiceProvider();

        var validator = Assert.Single(provider.GetServices<IDiagramValidator>());

        Assert.IsType<FdgValidator>(validator);
        Assert.Equal(Diagram.FunctionalDecompositionGraph.Origin, validator.Origin);
    }

    /// <summary>Each breach fixture reaches the panel under its own rule id, and only that one.</summary>
    [Theory]
    [MemberData(nameof(EveryRule))]
    public async Task EachRuleFixture_IsReportedUnderItsRuleId(string fixture, string ruleId)
    {
        var problems = await ProblemsIn(fixture);

        Assert.NotEmpty(problems);
        Assert.All(problems, problem => Assert.Equal(ruleId, problem.RuleId));
        Assert.All(problems, problem => Assert.False(string.IsNullOrWhiteSpace(problem.Message)));
    }

    /// <summary>An unreadable entry keeps its lines, so it warns; a breach of the notation is an error.</summary>
    [Theory]
    [MemberData(nameof(EveryRule))]
    public async Task TheSeverity_FollowsTheRule(string fixture, string ruleId)
    {
        var expected = ruleId == FdgRuleIds.UnreadableEntry
            ? DiagramProblemSeverity.Warning
            : DiagramProblemSeverity.Error;

        var problems = await ProblemsIn(fixture);

        Assert.All(problems, problem => Assert.Equal(expected, problem.Severity));
    }

    /// <summary>The line is one-based, as the panel counts: the narrow element's entry starts on line 3.</summary>
    [Fact]
    public async Task TheLocation_IsTheBreachsLineCountedFromOne()
    {
        var problems = await ProblemsIn("rule-unreadable-entry.fdg");

        var problem = Assert.Single(problems);
        var line = Assert.IsType<DiagramProblemLineLocation>(problem.Location);
        Assert.Equal(3u, line.Number);
    }

    /// <summary>The control: a document the notation permits, navigation loop included, reports nothing.</summary>
    [Fact]
    public async Task TheCleanDocument_ReportsNothing()
    {
        Assert.Empty(await ProblemsIn("rules-clean.fdg"));
    }

    /// <summary>Requirement 11.4 through the seam: the field-service example reports nothing.</summary>
    [Fact]
    public async Task TheExample_ReportsNothing()
    {
        var text = await File.ReadAllTextAsync(FieldServiceExample.Path, TestContext.Current.CancellationToken);

        var problems = await new FdgValidator().ValidateAsync(RequestFor(text), TestContext.Current.CancellationToken);

        Assert.Empty(problems);
    }

    /// <summary>Text that is not YAML at all still yields problems rather than an exception.</summary>
    [Fact]
    public async Task ADocumentThatIsNotYaml_IsReportedNotThrown()
    {
        var problems = await ProblemsIn("not-yaml.fdg");

        Assert.NotEmpty(problems);
    }
}
