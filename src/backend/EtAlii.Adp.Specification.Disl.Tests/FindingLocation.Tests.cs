using EtAlii.Adp.Specification.Cel;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// Where a finding is and what it is about (DISL §8.2, §8.6): an element's <c>location()</c> (§12.2),
/// a rule's <c>location</c> and <c>subject</c>, and a file that does not parse silencing only the
/// findings located in it.
/// </summary>
public class FindingLocationTests
{
    private const string Metamodel = """
        "metamodel": {
          "types": {
            "Box": { "attributes": { "name": { "type": "string" }, "size": { "type": "int" } } }
          }
        }
        """;

    private static string Line(DislFinding finding) =>
        $"{finding.File ?? "-"}:{finding.Line?.ToString() ?? "-"} | {finding.Code} | {finding.Message} | {string.Join(",", finding.ElementIds)} | {finding.Subject ?? "-"}";

    private static DislElement Box(DislDiagram diagram, string id, string? file, int? line, IReadOnlyDictionary<string, object?>? attributes = null) =>
        diagram.Add(new DislElement(diagram, diagram.Specification.Metamodel.TypeOf("Box")!, id, attributes) { File = file, Line = line });

    private static DislSpecification WithRules(string rules) => Specifications.Loaded(Specifications.With(Metamodel + $$"""
        ,
        "constraints": { "rules": [ {{rules}} ] }
        """));

    [Fact]
    public void AnElementsLocation_IsTheFileAndLineItWasReadAt_OrNone()
    {
        // Arrange.
        var specification = Specifications.Loaded(Specifications.With(Metamodel));
        var diagram = new DislDiagram(specification) { File = "main.yaml" };
        var placed = Box(diagram, "placed", "charts/a.yaml", 4);
        var wholeFile = Box(diagram, "wholeFile", "b.yaml", null);
        var primary = Box(diagram, "primary", null, 9);
        var unplaced = Box(new DislDiagram(specification), "unplaced", null, null);
        var program = specification.Environment(DislContexts.Element).Compile("self.location()");

        // Act.
        object? LocationOf(DislElement self) => program.Evaluate(new Dictionary<string, object?> { ["self"] = self });

        // Assert.
        Assert.Equal(CelOptional.Of(new CelMap { ["file"] = "charts/a.yaml", ["line"] = 4L }), LocationOf(placed));
        Assert.Equal(CelOptional.Of(new CelMap { ["file"] = "b.yaml" }), LocationOf(wholeFile));
        Assert.Equal(CelOptional.Of(new CelMap { ["file"] = "main.yaml", ["line"] = 9L }), LocationOf(primary));
        Assert.Equal(CelOptional.None, LocationOf(unplaced));
    }

    [Fact]
    public void ARuleWithoutALocation_IsLocatedAtItsTarget()
    {
        // Arrange.
        var specification = WithRules("""{ "id": "named", "scope": "Box", "rule": "self.name != ''", "message": "No name." }""");
        var diagram = new DislDiagram(specification);
        Box(diagram, "a", "values.yaml", 3, new Dictionary<string, object?> { ["name"] = "" });
        Box(diagram, "b", null, 5, new Dictionary<string, object?> { ["name"] = "" });

        // Act.
        var findings = ConstraintEvaluator.Evaluate(specification, diagram);

        // Assert.
        Assert.Equal(["values.yaml:3 | named | No name. | a | -", "-:5 | named | No name. | b | -"], findings.Select(Line));
    }

    [Fact]
    public void ARulesLocationAndSubject_AreEvaluatedPerFinding_AndPerItem()
    {
        // Arrange.
        var specification = WithRules("""
            { "id": "elsewhere", "scope": "Box", "rule": "false", "message": "Elsewhere.",
              "location": "{'file': 'Chart.yaml', 'line': self.size}", "subject": "'key ' + self.name" },
            { "id": "nowhere", "scope": "Box", "rule": "false", "message": "Nowhere.", "location": "null" },
            { "id": "perItem", "scope": "Box", "forEach": "[1, 2]", "rule": "false", "message": "Item.",
              "location": "item == 1 ? {'file': 'lock.yaml', 'line': 7} : {'file': 'lock.yaml'}" }
            """);
        var diagram = new DislDiagram(specification);
        Box(diagram, "a", "values.yaml", 3, new Dictionary<string, object?> { ["name"] = "x", ["size"] = 12L });

        // Act.
        var findings = ConstraintEvaluator.Evaluate(specification, diagram);

        // Assert.
        Assert.Equal(
        [
            "Chart.yaml:12 | elsewhere | Elsewhere. | a | key x",
            "-:- | nowhere | Nowhere. | a | -",
            "lock.yaml:7 | perItem | Item. | a | -",
            "lock.yaml:- | perItem | Item. | a | -",
        ], findings.Select(Line));
    }

    [Fact]
    public void AnUnparseableFileOfAFolder_SilencesOnlyTheFindingsLocatedInIt()
    {
        // Arrange.
        var specification = WithRules("""
            { "id": "never", "scope": "Box", "rule": "false", "message": "Never." },
            { "id": "pointsAtA", "scope": "Box", "when": "self.id == 'b'", "rule": "false", "message": "About a.", "location": "{'file': 'a.yaml', 'line': 1}" }
            """);
        var diagram = new DislDiagram(specification);
        Box(diagram, "a", "a.yaml", 2);
        Box(diagram, "b", "b.yaml", 2);

        // Act.
        var findings = ConstraintEvaluator.Evaluate(specification, diagram, new DislConstraintOptions(ReaderFindings: [DislReaderFinding.NotParsed("tab", 1, "a.yaml")]));

        // Assert.
        Assert.Equal(["a.yaml:1 | std.unparseable | tab |  | -", "b.yaml:2 | never | Never. | b | -"], findings.Select(Line));
    }

    [Fact]
    public void AnUnparseablePrimaryFile_StillStopsEveryConstraint()
    {
        // Arrange.
        var specification = WithRules("""{ "id": "never", "scope": "Box", "rule": "false", "message": "Never." }""");
        var diagram = new DislDiagram(specification) { File = "main.yaml" };
        Box(diagram, "a", "other.yaml", 2);

        // Act.
        var named = ConstraintEvaluator.Evaluate(specification, diagram, new DislConstraintOptions(ReaderFindings: [DislReaderFinding.NotParsed("tab", 1, "main.yaml")]));
        var unnamed = ConstraintEvaluator.Evaluate(specification, diagram, new DislConstraintOptions(ReaderFindings: [DislReaderFinding.NotParsed("tab", 1)]));

        // Assert.
        Assert.Equal(["main.yaml:1 | std.unparseable | tab |  | -"], named.Select(Line));
        Assert.Equal(["-:1 | std.unparseable | tab |  | -"], unnamed.Select(Line));
    }
}
