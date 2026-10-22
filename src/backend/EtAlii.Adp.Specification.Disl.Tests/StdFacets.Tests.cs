using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// <c>std.facets</c> from an attribute's <c>min</c> and <c>max</c> (DISL §4.3, §8.7): a stored number outside
/// them is kept and reported, with <c>detail</c>, <c>code</c> and <c>message</c>.
/// </summary>
public class StdFacetsTests
{
    private const string Metamodel = """
        "disl": "0.3",
        "metamodel": {
          "types": {
            "Box": { "attributes": {
              "width": { "type": "number", "min": 80 },
              "count": { "type": "int", "max": 10 },
              "label": { "type": "string" }
            } },
            "Note": { "extends": "Box", "attributes": { "height": { "type": "number", "min": 48, "max": 400 } } }
          }
        }
        """;

    private const string Configured = """
        ,
        "constraints": {
          "builtIn": {
            "std.facets": {
              "severity": "warning",
              "code": { "cel": "'box.' + detail.facet" },
              "message": { "cel": "self.id + ' ' + detail.attribute + ' ' + detail.facet + ' ' + string(detail.limit)" }
            }
          }
        }
        """;

    private static string Line(DislFinding finding) =>
        $"{finding.Line} | {finding.Code} | {finding.Severity} | {finding.Message} | {string.Join(",", finding.ElementIds)}";

    private static DislElement Node(DislDiagram diagram, string type, string id, int line, Dictionary<string, object?> attributes) =>
        diagram.Add(new DislElement(diagram, diagram.Specification.Metamodel.TypeOf(type)!, id, attributes) { Line = line });

    private static IEnumerable<string> Facets(DislSpecification specification, DislDiagram diagram) =>
        ConstraintEvaluator.Evaluate(specification, diagram).Where(finding => finding.ConstraintId == "std.facets").Select(Line);

    [Fact]
    public void AStoredNumberOutsideItsBounds_IsOneFindingPerBound_AtTheElement_WithDetailCodeAndMessage()
    {
        var specification = Specifications.Loaded(Specifications.With(Metamodel + Configured));
        var diagram = new DislDiagram(specification);
        Node(diagram, "Box", "a", 1, new() { ["width"] = 12.5, ["count"] = 11L });
        Node(diagram, "Box", "b", 2, new() { ["width"] = 80.0, ["count"] = 10L });

        Assert.Equal(["1 | box.min | warning | a width min 80 | a", "1 | box.max | warning | a count max 10 | a"], Facets(specification, diagram));
    }

    [Fact]
    public void AnInheritedAttribute_IsCheckedBeforeTheSubtypesOwn()
    {
        var specification = Specifications.Loaded(Specifications.With(Metamodel + Configured));
        var diagram = new DislDiagram(specification);
        Node(diagram, "Note", "n", 3, new() { ["width"] = 10.0, ["height"] = 30.0 });

        Assert.Equal(["3 | box.min | warning | n width min 80 | n", "3 | box.min | warning | n height min 48 | n"], Facets(specification, diagram));
    }

    [Fact]
    public void AnUnsetAttribute_IsNotReported()
    {
        var specification = Specifications.Loaded(Specifications.With(Metamodel + Configured));
        var diagram = new DislDiagram(specification);
        Node(diagram, "Box", "a", 1, new() { ["label"] = "x" });

        Assert.Empty(Facets(specification, diagram));
    }

    [Fact]
    public void WithoutASetting_TheRuntimeWordsIt_AsAnError()
    {
        var specification = Specifications.Loaded(Specifications.With(Metamodel));
        var diagram = new DislDiagram(specification);
        Node(diagram, "Box", "a", 1, new() { ["width"] = 12.5 });

        Assert.Equal(["1 | std.facets | error | 'a' has width 12.5; the minimum is 80. | a"], Facets(specification, diagram));
    }
}
