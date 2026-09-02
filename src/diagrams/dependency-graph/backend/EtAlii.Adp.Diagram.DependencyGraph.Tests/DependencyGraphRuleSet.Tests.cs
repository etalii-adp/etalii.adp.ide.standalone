using Xunit;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// The mistakes a dependency graph hides, one test per rule, each from a plain YAML string with
/// no file, canvas or connection.
/// </summary>
public class DependencyGraphRuleSetTests
{
    private static IReadOnlyList<DiagramProblem> Judge(string yaml) =>
        DependencyGraphRuleSet.Judge(DependencyGraphParser.Parse(DependencyGraphDocument.Parse(yaml)));

    private static IEnumerable<DiagramProblem> Of(string yaml, string ruleId) =>
        Judge(yaml).Where(problem => problem.RuleId == ruleId);

    private const string Healthy = """
        dependencies: 1
        elements:
          - id: aaa
            label: API gateway
            x: 240
            row: 0
          - id: bbb
            label: Identity service
            x: 480
            row: 1
        relations:
          - id: ccc
            from: aaa
            to: bbb
            label: verifies tokens with
        """;

    [Fact]
    public void ACorrectGraph_HasNothingWrongWithIt()
    {
        // Arrange & act & assert.
        // The test that matters most: a rule that fires on a healthy file teaches the reader to
        // ignore the panel.
        Assert.Empty(Judge(Healthy));
    }

    [Fact]
    public void AnEmptyGraph_IsAlsoFine()
    {
        // Assert.
        // A fresh document from the factory must open with a clean panel.
        Assert.Empty(Judge("dependencies: 1\nelements: []\n"));
    }

    [Fact]
    public void ADanglingRelation_NamesTheMissingEnd()
    {
        // Arrange & act.
        var problem = Assert.Single(Of("""
            dependencies: 1
            elements:
              - id: aaa
                x: 0
                row: 0
            relations:
              - id: ccc
                from: aaa
                to: ghost
            """, DependencyGraphRules.DanglingRelation));

        // Assert.
        Assert.Contains("ghost", problem.Message, StringComparison.Ordinal);
        Assert.Equal("ccc", Assert.IsType<DiagramProblemElementLocation>(problem.Location).Id);
    }

    [Fact]
    public void ADanglingRelation_IsReportedForEitherEnd()
    {
        // Arrange & act & assert.
        // The dependent end matters as much as the dependency: an edge out of a node nobody
        // declared is as broken as one into it.
        Assert.Single(Of("""
            dependencies: 1
            elements:
              - id: aaa
                x: 0
                row: 0
            relations:
              - id: ccc
                from: ghost
                to: aaa
            """, DependencyGraphRules.DanglingRelation));
    }

    [Fact]
    public void ADuplicateId_IsReportedOnTheSecondDeclaration()
    {
        // Arrange & act.
        var problem = Assert.Single(Of("""
            dependencies: 1
            elements:
              - id: aaa
                x: 0
                row: 0
              - id: aaa
                x: 200
                row: 1
            """, DependencyGraphRules.DuplicateId));

        // Assert.
        // The second occurrence's line, not the first: the first is the one that wins on the
        // canvas, so the one worth pointing at is the one that will not.
        Assert.Equal(6u, Assert.IsType<DiagramProblemLineLocation>(problem.Location).Number);
    }

    [Fact]
    public void AMissingId_IsReportedWithALine()
    {
        // Arrange & act.
        var problem = Assert.Single(Of("""
            dependencies: 1
            elements:
              - label: Anonymous
                x: 0
                row: 0
            """, DependencyGraphRules.MissingId));

        // Assert.
        // No id means no element location, so the line is what the user gets.
        Assert.Equal(3u, Assert.IsType<DiagramProblemLineLocation>(problem.Location).Number);
    }

    [Fact]
    public void ANodeDependingOnItself_IsReported()
    {
        // Arrange & act.
        // Nothing in the application can create this - the connect command refuses it - so a
        // file that says it was edited by hand.
        var problem = Assert.Single(Of("""
            dependencies: 1
            elements:
              - id: aaa
                x: 0
                row: 0
            relations:
              - id: ccc
                from: aaa
                to: aaa
            """, DependencyGraphRules.SelfDependency));

        // Assert.
        Assert.Equal("ccc", Assert.IsType<DiagramProblemElementLocation>(problem.Location).Id);
    }

    [Fact]
    public void NoRuleFiresOnACoordinateOrARowThatWillNotRead()
    {
        // Arrange & act & assert.
        // The absence test the design asks for. The timeline warned about a value it could not
        // read because a date it could not read left the element nowhere; a coordinate that will
        // not read is simply zero, which is a place. Adding a rule here would be inventing a
        // requirement out of a fork's momentum.
        Assert.Empty(Judge("dependencies: 1\nelements:\n  - id: aaa\n    x: sideways\n    row: sideways\n"));
    }

    [Fact]
    public void NoRuleIdMentionsTime()
    {
        // Assert.
        // The deletion, pinned. The timeline's end-before-begin and unreadable-time rules went
        // with the dates they judged, and a fork that quietly reintroduced one would reintroduce
        // it under a name like this.
        var ruleIds = typeof(DependencyGraphRules)
            .GetFields()
            .Where(field => field is { IsLiteral: true, FieldType: { } type } && type == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

        Assert.NotEmpty(ruleIds);
        foreach (var ruleId in ruleIds)
        {
            // Only the part after the module's prefix is examined: "dependencies" contains
            // "end", which a substring check reads as a date rule and which no amount of
            // renaming will fix.
            var name = ruleId.Split('.')[^1];
            var words = name.Split('-');
            foreach (var word in new[] { "time", "date", "begin", "end", "duration", "precision" })
            {
                Assert.DoesNotContain(word, words, StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
