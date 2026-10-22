using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// A relation type's <c>allowSelfLoops</c> defaults to <c>false</c> (DISL §4.9): a relation type that does not state
/// it forbids a loop, both as a finding of <c>std.endpoints</c> (§8.7) and as a refusal of a connect gesture (§8.4).
/// </summary>
public class SelfLoopDefaultTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "disl": "0.3",
        "metamodel": {
          "types": { "Box": {} },
          "relations": {
            "Unstated": { "source": "Box", "target": "Box" },
            "Allowed": { "source": "Box", "target": "Box", "allowSelfLoops": true }
          }
        },
        "constraints": { "builtIn": { "std.endpoints": { "message": { "cel": "detail.violation" } } } }
        """));

    private static string Violations(string relationType)
    {
        var diagram = new DislDiagram(Specification);
        var box = diagram.AddNode("Box", "a");
        diagram.AddRelation(relationType, "loop", box, box);
        return string.Join(",", ConstraintEvaluator.Evaluate(Specification, diagram)
            .Where(finding => finding.ConstraintId == "std.endpoints")
            .Select(finding => finding.Message));
    }

    private static string Refusal(string relationType)
    {
        var diagram = new DislDiagram(Specification);
        var box = diagram.AddNode("Box", "a");
        return GestureConstraintEvaluator.Connect(Specification, diagram, relationType, box, box, null)?.Message ?? "";
    }

    [Fact]
    public void ALoopOfARelationTypeThatDoesNotStateAllowSelfLoops_IsReportedAsASelfLoop() =>
        Assert.Equal("selfLoop", Violations("Unstated"));

    [Fact]
    public void ALoopOfARelationTypeThatAllowsSelfLoops_IsNotReported() =>
        Assert.Equal("", Violations("Allowed"));

    [Fact]
    public void ConnectingAnElementToItself_WithARelationTypeThatDoesNotStateAllowSelfLoops_IsRefused() =>
        Assert.Equal("A Unstated cannot connect an element to itself.", Refusal("Unstated"));

    [Fact]
    public void ConnectingAnElementToItself_WithARelationTypeThatAllowsSelfLoops_IsNotRefused() =>
        Assert.Equal("", Refusal("Allowed"));
}
