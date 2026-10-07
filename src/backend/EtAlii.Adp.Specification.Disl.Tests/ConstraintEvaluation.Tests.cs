using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>The findings of a diagram (DISL §8.2, §8.6, §8.7) and the refusals of a gesture (§8.4).</summary>
public class ConstraintEvaluationTests
{
    private const string Metamodel = """
        "metamodel": {
          "types": {
            "Box": { "attributes": { "name": { "type": "string" }, "size": { "type": "int" } } },
            "Lid": { "attributes": { "name": { "type": "string" } } }
          },
          "relations": {
            "Link": { "source": "Box", "target": "Box", "directed": true, "allowSelfLoops": false, "allowParallel": false }
          }
        }
        """;

    private static string Line(DislFinding finding) =>
        $"{finding.Line} | {finding.Code} | {finding.Severity} | {finding.Message} | {string.Join(",", finding.ElementIds)}";

    private static DislElement Node(DislDiagram diagram, string type, string id, int line, IReadOnlyDictionary<string, object?>? attributes = null) =>
        diagram.Add(new DislElement(diagram, diagram.Specification.Metamodel.TypeOf(type)!, id, attributes) { Line = line });

    private static DislElement Relation(DislDiagram diagram, string id, DislElement? source, DislElement? target, int line, string? sourceId = null, string? targetId = null) =>
        diagram.Add(new DislElement(diagram, diagram.Specification.Metamodel.TypeOf("Link")!, id, null)
        {
            Line = line, Source = source, Target = target, SourceId = sourceId ?? source?.Id, TargetId = targetId ?? target?.Id,
        });

    [Fact]
    public void ALiveRule_ReportsEachElementItsScopeTakes_WithItsCodeSeverityAndMessage()
    {
        var specification = Specifications.Loaded(Specifications.With(Metamodel + """
            ,
            "constraints": {
              "defaults": { "severity": "warning" },
              "rules": [
                { "id": "named", "code": "box.unnamed", "scope": "Box", "rule": "self.name != ''", "message": { "cel": "'`' + self.id + '` has no name.'" } },
                { "id": "small", "scope": "Box", "when": "has(self.size)", "rule": "self.size < 10", "severity": "error", "message": "Too big." },
                { "id": "off", "scope": "Box", "enabled": false, "rule": "false", "message": "Never." },
                { "id": "saveOnly", "scope": "Box", "timing": ["save"], "rule": "false", "message": "Only on save." }
              ]
            }
            """));
        var diagram = new DislDiagram(specification);
        Node(diagram, "Box", "a", 2, new Dictionary<string, object?> { ["name"] = "", ["size"] = 12L });
        Node(diagram, "Box", "b", 5, new Dictionary<string, object?> { ["name"] = "B" });
        Node(diagram, "Lid", "c", 7, new Dictionary<string, object?> { ["name"] = "" });

        var findings = ConstraintEvaluator.Evaluate(specification, diagram);

        Assert.Equal(["2 | box.unnamed | warning | `a` has no name. | a", "2 | small | error | Too big. | a"], findings.Select(Line));
    }

    [Fact]
    public void TheBuiltIns_ReportEndpointsReferencesAndDuplicateIds_InTheSpecificationsOrder()
    {
        var specification = Specifications.Loaded(Specifications.With(Metamodel + """
            ,
            "constraints": {
              "builtIn": {
                "std.endpoints": { "message": { "cel": "detail.violation + ' ' + self.id" } },
                "std.references": { "message": { "cel": "detail.end + ' ' + detail.missingId" } },
                "std.duplicateId": { "message": { "cel": "detail.id + ' x' + string(detail.count)" } }
              }
            }
            """));
        var diagram = new DislDiagram(specification);
        var a = Node(diagram, "Box", "a", 1);
        var b = Node(diagram, "Box", "b", 2);
        var lid = Node(diagram, "Lid", "lid", 3);
        Node(diagram, "Box", "a@4", 4);
        Node(diagram, "Box", "a@5", 5);
        Relation(diagram, "loop", a, a, 6);
        Relation(diagram, "ab1", a, b, 7);
        Relation(diagram, "ab2", a, b, 8);
        Relation(diagram, "toLid", a, lid, 9);
        Relation(diagram, "dangling", null, b, 10, sourceId: "gone");
        var written = new Dictionary<string, string>(StringComparer.Ordinal) { ["a@4"] = "a", ["a@5"] = "a" };

        var findings = ConstraintEvaluator.Evaluate(specification, diagram, new DislConstraintOptions(WrittenId: element => written.GetValueOrDefault(element.Id, element.Id)));

        Assert.Equal(
        [
            "4 | std.duplicateId | warning | a x3 | a",
            "5 | std.duplicateId | warning | a x3 | a",
            "6 | std.endpoints | error | selfLoop loop | loop",
            "8 | std.endpoints | error | parallel ab2 | ab2",
            "9 | std.endpoints | error | target toLid | toLid",
            "10 | std.references | error | source gone | dangling",
        ], findings.Select(Line));
    }

    [Fact]
    public void OncePerGroupAndXOrder_ReportAGroupOnceAndSortByCodeThenLine()
    {
        var specification = Specifications.Loaded(Specifications.With(Metamodel + """
            ,
            "constraints": {
              "order": ["dup", "par"],
              "builtIn": {
                "std.endpoints": { "code": "par", "message": "parallel", "oncePerGroup": "second" },
                "std.duplicateId": { "code": "dup", "message": { "cel": "detail.id" }, "oncePerGroup": "last" }
              },
              "rules": [ { "id": "late", "code": "early", "scope": "Box", "rule": "false", "message": "first in no order" } ]
            }
            """));
        var diagram = new DislDiagram(specification);
        var a = Node(diagram, "Box", "a", 1);
        var b = Node(diagram, "Box", "b", 2);
        Relation(diagram, "ab1", a, b, 3);
        Relation(diagram, "ab2", a, b, 4);
        Relation(diagram, "ab3", a, b, 5);
        Node(diagram, "Box", "b@6", 6);
        Node(diagram, "Box", "b@7", 7);
        var written = new Dictionary<string, string>(StringComparer.Ordinal) { ["b@6"] = "b", ["b@7"] = "b" };

        var findings = ConstraintEvaluator.Evaluate(specification, diagram, new DislConstraintOptions(WrittenId: element => written.GetValueOrDefault(element.Id, element.Id)));

        Assert.Equal(
        [
            "7 | dup | warning | b | b",
            "4 | par | error | parallel | ab2",
            "1 | early | error | first in no order | a",
            "2 | early | error | first in no order | b",
            "6 | early | error | first in no order | b",
            "7 | early | error | first in no order | b",
        ], findings.Select(Line));
    }

    [Fact]
    public void AnUnparseableFile_HasNoOtherFinding_AndAnUnreadableEntryIsTheReaders()
    {
        var specification = Specifications.Loaded(Specifications.With(Metamodel + """
            ,
            "constraints": {
              "builtIn": {
                "std.unreadableEntry": { "code": "unreadable", "message": { "cel": "'cannot read: ' + detail.reason" } },
                "std.unparseable": { "severity": "warning", "code": "unparseable" }
              },
              "rules": [ { "id": "never", "scope": "Box", "rule": "false", "message": "no" } ]
            }
            """));
        var diagram = new DislDiagram(specification);
        Node(diagram, "Box", "a", 3);

        var read = ConstraintEvaluator.Evaluate(specification, diagram, new DislConstraintOptions(ReaderFindings: [DislReaderFinding.Unreadable("odd key", 2)]));
        var unparsed = ConstraintEvaluator.Evaluate(specification, diagram, new DislConstraintOptions(ReaderFindings: [DislReaderFinding.NotParsed("tab", 1)]));

        Assert.Equal(["2 | unreadable | warning | cannot read: odd key | ", "3 | never | error | no | a"], read.Select(Line));
        Assert.Equal("1 | unparseable | warning", string.Join(" | ", Line(Assert.Single(unparsed)).Split(" | ").Take(3)));
    }

    [Fact]
    public void AGestureRefusal_IsEveryPreventingRuleOfItsKindThatDoesNotHold_InDeclarationOrder()
    {
        var specification = Specifications.Loaded(Specifications.With(Metamodel + """
            ,
            "constraints": {
              "rules": [
                { "id": "named", "kind": "change", "scope": "Box", "rule": "attribute != 'name' || newValue != ''", "message": "A box needs a name." },
                { "id": "short", "kind": "change", "scope": "Box", "enforcement": "prevent", "rule": "attribute != 'name' || size(newValue) < 4", "message": { "cel": "oldValue + ' to ' + newValue + ' is too long.'" } },
                { "id": "reported", "kind": "change", "scope": "Box", "enforcement": "report", "rule": "false", "message": "Not a refusal." },
                { "id": "sized", "kind": "change", "scope": "Box", "when": "has(self.size)", "rule": "attribute != 'size' || newValue > self.size", "message": "Only bigger." },
                { "id": "wide", "kind": "placement", "scope": "Box", "rule": "gesture != 'resize' || newBounds.width >= oldBounds.width", "message": "Only wider." }
              ]
            }
            """));
        var diagram = new DislDiagram(specification);
        var box = Node(diagram, "Box", "a", 1, new Dictionary<string, object?> { ["name"] = "Ann" });
        var lid = Node(diagram, "Lid", "l", 2, new Dictionary<string, object?> { ["name"] = "Lid" });

        static string Refused(IReadOnlyList<DislFinding> refusals) => string.Join(" / ", refusals.Select(refusal => $"{refusal.ConstraintId}: {refusal.Message}"));

        Assert.Equal("", Refused(GestureConstraintEvaluator.Change(specification, box, "name", "Bo")));
        Assert.Equal("named: A box needs a name.", Refused(GestureConstraintEvaluator.Change(specification, box, "name", "")));
        Assert.Equal("short: Ann to Bartholomew is too long.", Refused(GestureConstraintEvaluator.Change(specification, box, "name", "Bartholomew")));
        Assert.Equal("", Refused(GestureConstraintEvaluator.Change(specification, lid, "name", "")));
        Assert.Equal("", Refused(GestureConstraintEvaluator.Change(specification, box, "size", 1L)));
        var bounds = new Dictionary<string, object?> { ["width"] = 10.0 };
        Assert.Equal("wide: Only wider.", Refused(GestureConstraintEvaluator.Placement(specification, box, "resize", bounds, new Dictionary<string, object?> { ["width"] = 5.0 })));
        Assert.Equal("", Refused(GestureConstraintEvaluator.Placement(specification, box, "move", bounds, new Dictionary<string, object?> { ["width"] = 5.0 })));
    }

    [Fact]
    public void AGestureRuleThatCannotBeEvaluated_Refuses()
    {
        var specification = Specifications.Loaded(Specifications.With(Metamodel + """
            ,
            "constraints": { "rules": [ { "id": "sized", "kind": "change", "scope": "Box", "rule": "3 / (newValue - 3) > 0", "message": "Cannot divide." } ] }
            """));
        var diagram = new DislDiagram(specification);
        var box = Node(diagram, "Box", "a", 1);

        Assert.Equal("Cannot divide.", Assert.Single(GestureConstraintEvaluator.Change(specification, box, "size", 3L)).Message);
    }
}
