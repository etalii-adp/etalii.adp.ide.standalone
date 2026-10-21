using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary><c>std.multiplicity</c> from a relation end's <c>max</c> (DISL §4.9, §8.7): its findings, their <c>detail</c>, <c>code</c> and <c>message</c>.</summary>
public class StdMultiplicityTests
{
    private const string Metamodel = """
        "disl": "0.3",
        "metamodel": {
          "types": { "Box": {} },
          "relations": {
            "Holds": { "source": "Box", "target": { "types": ["Box"], "min": 0, "max": 1 } },
            "Grips": { "extends": "Holds" },
            "Points": { "source": { "types": ["Box"], "max": 1 }, "target": "Box" },
            "Free": { "source": "Box", "target": "Box" }
          }
        }
        """;

    private const string Configured = """
        ,
        "constraints": {
          "builtIn": {
            "std.multiplicity": {
              "code": { "cel": "detail.end == 'source' ? 'box.second-pointer' : 'box.second-holder'" },
              "message": { "cel": "self.id + ' ' + detail.end + ' ' + detail.relationType + ' ' + string(detail.count) + '/' + string(detail.max) + ' min ' + string(detail.?min.orValue(-1))" }
            }
          }
        }
        """;

    private static string Line(DislFinding finding) =>
        $"{finding.Line} | {finding.Code} | {finding.Severity} | {finding.Message} | {string.Join(",", finding.ElementIds)}";

    private static DislElement Node(DislDiagram diagram, string id, int line) =>
        diagram.Add(new DislElement(diagram, diagram.Specification.Metamodel.TypeOf("Box")!, id, null) { Line = line });

    private static void Relation(DislDiagram diagram, string type, string id, DislElement? source, DislElement? target, int line, string? targetId = null) =>
        diagram.Add(new DislElement(diagram, diagram.Specification.Metamodel.TypeOf(type)!, id, null)
        {
            Line = line, Source = source, Target = target, SourceId = source?.Id, TargetId = targetId ?? target?.Id,
        });

    private static IEnumerable<string> Multiplicity(DislSpecification specification, DislDiagram diagram) =>
        ConstraintEvaluator.Evaluate(specification, diagram).Where(finding => finding.ConstraintId == "std.multiplicity").Select(Line);

    [Fact]
    public void ATargetPastItsMax_IsOneFinding_AtTheRelationThatWentOver_WithDetailCodeAndMessage()
    {
        var specification = Specifications.Loaded(Specifications.With(Metamodel + Configured));
        var diagram = new DislDiagram(specification);
        var a = Node(diagram, "a", 1);
        var b = Node(diagram, "b", 2);
        var c = Node(diagram, "c", 3);
        var d = Node(diagram, "d", 4);
        Relation(diagram, "Holds", "h1", a, c, 5);
        Relation(diagram, "Holds", "h2", b, c, 6);
        Relation(diagram, "Holds", "h3", d, c, 7);
        Relation(diagram, "Holds", "h4", a, d, 8);

        Assert.Equal(["7 | box.second-holder | error | c target Holds 3/1 min 0 | c,h1,h2,h3"], Multiplicity(specification, diagram));
    }

    [Fact]
    public void ASourcePastItsMax_IsCountedOnItsOutgoingRelations()
    {
        var specification = Specifications.Loaded(Specifications.With(Metamodel + Configured));
        var diagram = new DislDiagram(specification);
        var a = Node(diagram, "a", 1);
        var b = Node(diagram, "b", 2);
        var c = Node(diagram, "c", 3);
        Relation(diagram, "Points", "p1", a, b, 4);
        Relation(diagram, "Points", "p2", a, c, 5);
        Relation(diagram, "Points", "p3", b, c, 6);

        Assert.Equal(["5 | box.second-pointer | error | a source Points 2/1 min -1 | a,p1,p2"], Multiplicity(specification, diagram));
    }

    [Fact]
    public void AnEndThatNamesNoElement_CountsForNothing()
    {
        var specification = Specifications.Loaded(Specifications.With(Metamodel + Configured));
        var diagram = new DislDiagram(specification);
        var a = Node(diagram, "a", 1);
        var b = Node(diagram, "b", 2);
        Relation(diagram, "Holds", "h1", a, null, 3, targetId: "ghost");
        Relation(diagram, "Holds", "h2", b, null, 4, targetId: "ghost");

        Assert.Empty(Multiplicity(specification, diagram));
    }

    [Fact]
    public void ASubtypeThatInheritsTheEnd_IsCountedWithItsSupertype_Once()
    {
        var specification = Specifications.Loaded(Specifications.With(Metamodel + Configured));
        var diagram = new DislDiagram(specification);
        var a = Node(diagram, "a", 1);
        var b = Node(diagram, "b", 2);
        var c = Node(diagram, "c", 3);
        Relation(diagram, "Holds", "h1", a, c, 4);
        Relation(diagram, "Grips", "g1", b, c, 5);

        Assert.Equal(["5 | box.second-holder | error | c target Holds 2/1 min 0 | c,h1,g1"], Multiplicity(specification, diagram));
    }

    [Fact]
    public void WithoutASetting_TheRuntimeWordsIt_AndAnEndWithoutMaxIsUnbounded()
    {
        var specification = Specifications.Loaded(Specifications.With(Metamodel));
        var diagram = new DislDiagram(specification);
        var a = Node(diagram, "a", 1);
        var b = Node(diagram, "b", 2);
        var c = Node(diagram, "c", 3);
        Relation(diagram, "Free", "f1", a, c, 4);
        Relation(diagram, "Free", "f2", b, c, 5);
        Relation(diagram, "Holds", "h1", a, b, 6);
        Relation(diagram, "Holds", "h2", c, b, 7);

        Assert.Equal(["7 | std.multiplicity | error | 'b' has 2 incoming Holds relations; it may have 1. | b,h1,h2"], Multiplicity(specification, diagram));
    }

    [Fact]
    public void ItsFindings_ComeBeforeTheEndpointFindings_AsTheTableOf87Orders()
    {
        var specification = Specifications.Loaded(Specifications.With(Metamodel.Replace("\"Free\": { \"source\": \"Box\", \"target\": \"Box\" }", "\"Free\": { \"source\": \"Box\", \"target\": \"Box\", \"allowSelfLoops\": false }", StringComparison.Ordinal)));
        var diagram = new DislDiagram(specification);
        var a = Node(diagram, "a", 1);
        var b = Node(diagram, "b", 2);
        Relation(diagram, "Free", "loop", a, a, 3);
        Relation(diagram, "Holds", "h1", a, b, 4);
        Relation(diagram, "Holds", "h2", a, b, 5);

        Assert.Equal(["std.multiplicity", "std.endpoints"], ConstraintEvaluator.Evaluate(specification, diagram).Select(finding => finding.ConstraintId));
    }
}
