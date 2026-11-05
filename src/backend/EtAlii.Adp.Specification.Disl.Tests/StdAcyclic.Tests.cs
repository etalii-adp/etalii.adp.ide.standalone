using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary><c>std.acyclic</c> over a relation type and its subtypes (DISL §4.9, §8.7): one finding per elementary cycle, with <c>detail.cycle</c>.</summary>
public class StdAcyclicTests
{
    private const string Metamodel = """
        "disl": "0.3",
        "metamodel": {
          "types": { "Box": {} },
          "relations": {
            "Owns": { "abstract": true, "acyclic": true, "source": "Box", "target": "Box", "allowSelfLoops": true },
            "Child": { "extends": "Owns", "allowSelfLoops": true },
            "Part": { "extends": "Owns", "acyclic": true, "allowSelfLoops": false },
            "Shows": { "source": "Box", "target": "Box" }
          }
        }
        """;

    private const string Configured = """
        ,
        "constraints": {
          "builtIn": {
            "std.acyclic": {
              "code": "box.cycle",
              "message": { "cel": "self.id + ': ' + detail.relationType + ' ' + detail.cycle.map(e, e.id).join(' -> ')" }
            }
          }
        }
        """;

    private static string Line(DislFinding finding) =>
        $"{finding.Line} | {finding.Code} | {finding.Severity} | {finding.Message} | {string.Join(",", finding.ElementIds)}";

    private static DislElement Node(DislDiagram diagram, string id, int line) =>
        diagram.Add(new DislElement(diagram, diagram.Specification.Metamodel.TypeOf("Box")!, id, null) { Line = line });

    private static void Relation(DislDiagram diagram, string type, string id, DislElement source, DislElement target, int line) =>
        diagram.Add(new DislElement(diagram, diagram.Specification.Metamodel.TypeOf(type)!, id, null)
        {
            Line = line, Source = source, Target = target, SourceId = source.Id, TargetId = target.Id,
        });

    private static IEnumerable<string> Acyclic(DislSpecification specification, DislDiagram diagram, DislConstraintOptions? options = null) =>
        ConstraintEvaluator.Evaluate(specification, diagram, options).Where(finding => finding.ConstraintId == "std.acyclic").Select(Line);

    [Fact]
    public void ASelfLoop_IsACycleOfOne()
    {
        var specification = Specifications.Loaded(Specifications.With(Metamodel + Configured));
        var diagram = new DislDiagram(specification);
        var a = Node(diagram, "a", 1);
        Relation(diagram, "Child", "loop", a, a, 2);

        Assert.Equal(["1 | box.cycle | error | a: Owns a | a"], Acyclic(specification, diagram));
    }

    [Fact]
    public void TwoCyclesSharingAMember_AreTwoFindings_InTheOrderOfDiagramCycles()
    {
        var specification = Specifications.Loaded(Specifications.With(Metamodel + Configured));
        var diagram = new DislDiagram(specification);
        var a = Node(diagram, "a", 1);
        var b = Node(diagram, "b", 2);
        var c = Node(diagram, "c", 3);
        Relation(diagram, "Child", "ab", a, b, 4);
        Relation(diagram, "Child", "ba", b, a, 5);
        Relation(diagram, "Child", "bc", b, c, 6);
        Relation(diagram, "Child", "cb", c, b, 7);

        Assert.Equal(["1 | box.cycle | error | a: Owns a -> b | a,b", "2 | box.cycle | error | b: Owns b -> c | b,c"], Acyclic(specification, diagram));
    }

    [Fact]
    public void ACycleThroughSubtypes_IsOneFinding_ForTheAcyclicSupertype()
    {
        var specification = Specifications.Loaded(Specifications.With(Metamodel + Configured));
        var diagram = new DislDiagram(specification);
        var a = Node(diagram, "a", 1);
        var b = Node(diagram, "b", 2);
        var c = Node(diagram, "c", 3);
        Relation(diagram, "Child", "ab", a, b, 4);
        Relation(diagram, "Part", "bc", b, c, 5);
        Relation(diagram, "Part", "ca", c, a, 6);

        Assert.Equal(["1 | box.cycle | error | a: Owns a -> b -> c | a,b,c"], Acyclic(specification, diagram));
    }

    [Fact]
    public void ASubtypeThatStatesAcyclicToo_IsCoveredByItsSupertype_AndReportedOnce()
    {
        var specification = Specifications.Loaded(Specifications.With(Metamodel + Configured));
        var diagram = new DislDiagram(specification);
        var a = Node(diagram, "a", 1);
        var b = Node(diagram, "b", 2);
        Relation(diagram, "Part", "ab", a, b, 3);
        Relation(diagram, "Part", "ba", b, a, 4);

        Assert.Equal(["1 | box.cycle | error | a: Owns a -> b | a,b"], Acyclic(specification, diagram));
    }

    [Fact]
    public void ALoopThatClosesOnlyThroughARelationOutsideTheType_GivesNone()
    {
        var specification = Specifications.Loaded(Specifications.With(Metamodel + Configured));
        var diagram = new DislDiagram(specification);
        var a = Node(diagram, "a", 1);
        var b = Node(diagram, "b", 2);
        Relation(diagram, "Child", "ab", a, b, 3);
        Relation(diagram, "Shows", "ba", b, a, 4);

        Assert.Empty(Acyclic(specification, diagram));
    }

    [Fact]
    public void ACycle_IsLocatedAtItsLeastId_WhileDetailCycleStartsAtItsFirstMemberInModelOrder()
    {
        var specification = Specifications.Loaded(Specifications.With(Metamodel + Configured));
        var diagram = new DislDiagram(specification);
        var c = Node(diagram, "c", 1);
        var a = Node(diagram, "a", 2);
        var b = Node(diagram, "b", 3);
        Relation(diagram, "Child", "ca", c, a, 4);
        Relation(diagram, "Child", "ab", a, b, 5);
        Relation(diagram, "Child", "bc", b, c, 6);

        Assert.Equal(["2 | box.cycle | error | a: Owns c -> a -> b | c,a,b"], Acyclic(specification, diagram));
    }

    [Fact]
    public void WithoutASetting_TheRuntimeWordsIt()
    {
        var specification = Specifications.Loaded(Specifications.With(Metamodel));
        var diagram = new DislDiagram(specification);
        var a = Node(diagram, "a", 1);
        var b = Node(diagram, "b", 2);
        Relation(diagram, "Child", "ab", a, b, 3);
        Relation(diagram, "Child", "ba", b, a, 4);

        Assert.Equal(["1 | std.acyclic | error | These Owns relations form a cycle: a -> b -> a. | a,b"], Acyclic(specification, diagram));
    }

    [Fact]
    public void TheMembersOfDetailCycle_AreFoundInItByPositionIn_WhenTheyAreNamedByTheirWrittenIds()
    {
        var specification = Specifications.Loaded(Specifications.With(Metamodel + """
            ,
            "constraints": {
              "builtIn": {
                "std.acyclic": { "message": { "cel": "detail.cycle.map(e, e.id + '@' + string(e.positionIn(detail.cycle))).join(' ')" } }
              }
            }
            """));
        var diagram = new DislDiagram(specification);
        var a = Node(diagram, "a", 1);
        var b = Node(diagram, "b", 2);
        Relation(diagram, "Child", "ab", a, b, 3);
        Relation(diagram, "Child", "ba", b, a, 4);

        var options = new DislConstraintOptions(WrittenId: element => element.Id.ToUpperInvariant());

        Assert.Equal(["1 | std.acyclic | error | A@0 B@1 | A,B"], Acyclic(specification, diagram, options));
    }
}
