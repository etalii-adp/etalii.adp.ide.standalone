using EtAlii.Adp.Specification.Cel;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// An attribute typed by a constrained primitive (DISL §4.4, a data type with <c>base</c>) has that data type's
/// <c>min</c> and <c>max</c>, so <c>std.facets</c> reports a stored number outside them (§8.7); the attribute's own
/// bounds win where it states them.
/// </summary>
public class DataTypeFacetsTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "disl": "0.3",
        "metamodel": {
          "dataTypes": { "Fraction": { "base": "number", "min": 0, "max": 1 } },
          "types": { "Dot": { "attributes": {
            "across": { "type": "Fraction" },
            "up": { "type": "Fraction", "max": 2 } } } }
        }
        """));

    private static IEnumerable<string> Facets(params (string Id, double Across, double Up)[] dots)
    {
        var diagram = new DislDiagram(Specification);
        var line = 1;
        foreach ((string id, double across, double up) in dots)
        {
            diagram.Add(new DislElement(diagram, Specification.Metamodel.TypeOf("Dot")!, id, new Dictionary<string, object?> { ["across"] = across, ["up"] = up }) { Line = line++ });
        }
        return ConstraintEvaluator.Evaluate(Specification, diagram).Where(finding => finding.ConstraintId == "std.facets").Select(finding => $"{finding.Line} | {finding.Message}");
    }

    [Fact]
    public void ANumberOutsideItsDataTypesBounds_IsReported() =>
        Assert.Equal(["1 | 'a' has across 1.2; the maximum is 1.", "2 | 'b' has across -0.5; the minimum is 0."], Facets(("a", 1.2, 0), ("b", -0.5, 0), ("c", 1, 0)));

    [Fact]
    public void TheAttributesOwnBound_WinsOverItsDataTypes() =>
        Assert.Equal(["2 | 'b' has up 2.5; the maximum is 2."], Facets(("a", 0, 1.5), ("b", 0, 2.5)));

    private static readonly DislSpecification Structs = Specifications.Loaded(Specifications.With("""
        "disl": "0.3",
        "metamodel": {
          "dataTypes": {
            "Fraction": { "base": "number", "min": 0, "max": 1 },
            "Extent": { "fields": { "across": { "type": "Fraction" }, "up": { "type": "Fraction", "max": 2 }, "name": { "type": "string" } } } },
          "types": { "Box": { "attributes": { "extent": { "type": "Extent" } } } }
        }
        """));

    [Fact]
    public void ANumberOutsideItsBounds_InAStructField_IsReportedByItsDottedPath()
    {
        var diagram = new DislDiagram(Structs);
        var extent = new CelMap { ["across"] = 1.2, ["up"] = 2.5, ["name"] = "x" };
        diagram.Add(new DislElement(diagram, Structs.Metamodel.TypeOf("Box")!, "a", new Dictionary<string, object?> { ["extent"] = extent }) { Line = 1 });
        var findings = ConstraintEvaluator.Evaluate(Structs, diagram).Where(finding => finding.ConstraintId == "std.facets").ToList();
        Assert.Equal(["1 | 'a' has extent.across 1.2; the maximum is 1.", "1 | 'a' has extent.up 2.5; the maximum is 2."], findings.Select(finding => $"{finding.Line} | {finding.Message}"));
    }
}
