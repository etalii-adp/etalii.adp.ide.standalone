using EtAlii.Adp.Specification.Fbl;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// A derived id held twice (DISL §11.5.2, §11.5.4): the first holder keeps it, and the later one is reported once,
/// through <c>std.duplicateId</c> with its settings and on its own line.
/// </summary>
public class DerivedIdHeldTwiceTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "disl": "0.3",
        "metamodel": {
          "types": { "Box": { "attributes": { "name": { "type": "string" } } } },
          "relations": { "Link": { "source": "Box", "target": "Box", "allowParallel": true } } },
        "constraints": { "builtIn": { "std.duplicateId": { "code": "test.twice", "severity": "error",
          "message": { "cel": "'The link ' + detail.id + ' is written ' + string(detail.count) + ' times.'" } } } },
        "persistence": {
          "ids": { "types": { "Link": { "strategy": "derived", "expression": "self.source.id + '->' + self.target.id" } } },
          "typeMap": { "Item": { "as": "Box" }, "Arrow": { "as": "Link", "attributes": { "from": "source", "to": "target" } } } }
        """));

    private static FblElement Element(string id, string type, int line, IReadOnlyDictionary<string, object?> attributes, bool stored = true) =>
        new(id, stored, type, type.ToLowerInvariant(), false, attributes, null, null, null, null, new Span(0, 0), line);

    private static DislModel Model() => DislModelBuilder.From(new FblModel(
    [
        Element("a", "Item", 2, new Dictionary<string, object?>()),
        Element("b", "Item", 3, new Dictionary<string, object?>()),
        Element("arrow@5", "Arrow", 5, new Dictionary<string, object?> { ["from"] = "a", ["to"] = "b" }, stored: false),
        Element("arrow@8", "Arrow", 8, new Dictionary<string, object?> { ["from"] = "b", ["to"] = "a" }, stored: false),
        Element("arrow@11", "Arrow", 11, new Dictionary<string, object?> { ["from"] = "a", ["to"] = "b" }, stored: false),
    ], [], false), Specification);

    [Fact]
    public void TheBuilder_LeavesTheDuplicateToTheConstraints()
    {
        // Act.
        var model = Model();

        // Assert: both links keep the computed id, the first holder first, and the builder reports nothing of it.
        Assert.Equal(["a->b", "b->a", "a->b"], model.Diagram.Relations.Select(relation => relation.Id));
        Assert.DoesNotContain(model.Findings, finding => finding.Code == FindingCodes.DuplicateId);
    }

    [Fact]
    public void TheLaterHolder_IsReportedOnce_OnItsLine_WithTheSettings()
    {
        // Arrange.
        var model = Model();

        // Act.
        var findings = ConstraintEvaluator.Evaluate(Specification, model.Diagram,
            new DislConstraintOptions(ReaderFindings: [.. model.Findings.Select(finding => new DislReaderFinding(finding.Code, new Dictionary<string, object?> { ["reason"] = finding.Message }, null))]));

        // Assert.
        var duplicate = Assert.Single(findings, finding => finding.ConstraintId == "std.duplicateId");
        Assert.Equal(("test.twice", "error", "The link a->b is written 2 times.", 11), (duplicate.Code, duplicate.Severity, duplicate.Message, duplicate.Line));
    }
}
