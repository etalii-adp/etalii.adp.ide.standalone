using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// The built-ins' refusal of a connect gesture (DISL §8.4, step 3): the first of <c>std.endpoints</c>,
/// <c>std.multiplicity</c> and <c>std.acyclic</c> that refuses, worded by its <c>refusal</c> with
/// <c>violation</c> bound, then the declared <c>connect</c> constraints.
/// </summary>
public class ConnectRefusalTests
{
    private const string Metamodel = """
        "disl": "0.3",
        "metamodel": {
          "types": {
            "Part": { "abstract": true, "attributes": { "name": { "type": "string" }, "sort": { "type": "string", "readOnly": true } } },
            "Box": { "extends": "Part", "attributes": { "sort": { "type": "string", "fixed": "box" } } },
            "Bag": { "extends": "Part", "attributes": { "sort": { "type": "string", "fixed": "bag" } } },
            "Note": { "attributes": { "text": { "type": "text" } } }
          },
          "relations": {
            "Holds": { "abstract": true, "acyclic": true },
            "HoldsBox": { "extends": "Holds", "allowSelfLoops": false, "source": ["Box", "Bag"], "target": { "types": ["Box"], "max": 1 } },
            "HoldsBag": { "extends": "Holds", "source": ["Box", "Bag"], "target": { "types": ["Bag"], "max": 1 } },
            "Points": { "source": { "types": ["Box"], "max": 1 }, "target": "Part" }
          }
        }
        """;

    private const string Refusals = """
        ,
        "constraints": {
          "builtIn": {
            "std.endpoints": {
              "message": "Not this one.",
              "refusal": { "cel": "source == target ? 'type: ' + source.name + ' to itself' : 'type: ' + relationType + ' ' + violation.end + ' ' + violation.rule + ' from a ' + source.sort + ' to a ' + target.sort" }
            },
            "std.multiplicity": {
              "refusal": { "cel": "'cardinality: ' + violation.relationType + ' ' + violation.end + ' ' + string(violation.count) + '/' + string(violation.max) + ' at ' + (violation.end == 'source' ? source.name : target.name)" }
            },
            "std.acyclic": {
              "refusal": { "cel": "'cycle: ' + violation.relationType + ' ' + violation.cycle.map(e, e.name).join(' -> ')" }
            }
          }
        }
        """;

    private static readonly DislSpecification Worded = Specifications.Loaded(Specifications.With(Metamodel + Refusals));

    private static DislElement Node(DislDiagram diagram, string type, string name) =>
        diagram.AddNode(type, name, new Dictionary<string, object?> { [type == "Note" ? "text" : "name"] = name });

    private static string Refusal(DislSpecification specification, DislDiagram diagram, string relationType, DislElement source, DislElement target, DislElement? self = null) =>
        GestureConstraintEvaluator.Connect(specification, diagram, relationType, source, target, self) is { } refusal
            ? $"{refusal.ConstraintId} | {refusal.Severity} | {refusal.Message} | {string.Join(",", refusal.ElementIds)}"
            : "";

    [Fact]
    public void AnEndOfATypeTheRelationDoesNotAllow_IsRefusedByTheTypeCheck_InItsOwnWords()
    {
        var diagram = new DislDiagram(Worded);
        var box = Node(diagram, "Box", "a");
        var bag = Node(diagram, "Bag", "b");

        Assert.Equal("std.endpoints | error | type: HoldsBox target std.endpoints from a box to a bag | a,b", Refusal(Worded, diagram, "HoldsBox", box, bag));
    }

    [Fact]
    public void AnEndOfATypeTheRelationDoesNotAllow_AtTheSource_IsCheckedFirst_AndARefusalThatFails_IsTheRuntimes()
    {
        var diagram = new DislDiagram(Worded);
        var note = Node(diagram, "Note", "n");
        var box = Node(diagram, "Box", "a");

        // A Note has no sort, so the refusal fails to evaluate and the runtime's own sentence stands in for it.
        Assert.Equal("std.endpoints | error | A HoldsBox cannot have this source. | n,a", Refusal(Worded, diagram, "HoldsBox", note, box));
    }

    [Fact]
    public void ALinkToItself_IsRefusedByTheTypeCheck_BeforeTheCycleCheck()
    {
        var diagram = new DislDiagram(Worded);
        var box = Node(diagram, "Box", "a");

        Assert.Equal("std.endpoints | error | type: a to itself | a,a", Refusal(Worded, diagram, "HoldsBox", box, box));
    }

    [Fact]
    public void ASecondParent_IsRefusedByTheCardinalityCheck_CountedOverTheEndsType()
    {
        var diagram = new DislDiagram(Worded);
        var a = Node(diagram, "Box", "a");
        var b = Node(diagram, "Bag", "b");
        var c = Node(diagram, "Box", "c");
        diagram.AddRelation("HoldsBox", "ac", a, c);

        Assert.Equal("std.multiplicity | error | cardinality: HoldsBox target 2/1 at c | b,c", Refusal(Worded, diagram, "HoldsBox", b, c));
    }

    [Fact]
    public void ASecondLinkFromASourceWithAMax_IsRefusedAtTheSourceEnd()
    {
        var diagram = new DislDiagram(Worded);
        var a = Node(diagram, "Box", "a");
        var b = Node(diagram, "Bag", "b");
        var c = Node(diagram, "Box", "c");
        diagram.AddRelation("Points", "ab", a, b);

        Assert.Equal("std.multiplicity | error | cardinality: Points source 2/1 at a | a,c", Refusal(Worded, diagram, "Points", a, c));
    }

    [Fact]
    public void ALinkThatClosesALoopThroughSubtypes_IsRefusedByTheCycleCheck_WithTheLoopInOrder()
    {
        var diagram = new DislDiagram(Worded);
        var a = Node(diagram, "Box", "a");
        var b = Node(diagram, "Bag", "b");
        var c = Node(diagram, "Bag", "c");
        diagram.AddRelation("HoldsBag", "ab", a, b);
        diagram.AddRelation("HoldsBag", "bc", b, c);

        Assert.Equal("std.acyclic | error | cycle: Holds c -> a -> b | c,a", Refusal(Worded, diagram, "HoldsBox", c, a));
    }

    [Fact]
    public void ALoopThroughARelationTheCycleRuleDoesNotCover_IsAllowed()
    {
        var diagram = new DislDiagram(Worded);
        var a = Node(diagram, "Box", "a");
        var b = Node(diagram, "Box", "b");
        diagram.AddRelation("Points", "ab", a, b);

        Assert.Equal("", Refusal(Worded, diagram, "HoldsBox", b, a));
    }

    [Fact]
    public void ALinkThatBreaksTheCardinalityAndTheCycleRule_IsRefusedByTheCardinalityCheck()
    {
        var diagram = new DislDiagram(Worded);
        var a = Node(diagram, "Box", "a");
        var b = Node(diagram, "Box", "b");
        var c = Node(diagram, "Box", "c");
        diagram.AddRelation("HoldsBox", "ab", a, b);
        diagram.AddRelation("HoldsBox", "ca", c, a);

        Assert.Equal("std.multiplicity | error | cardinality: HoldsBox target 2/1 at a | b,a", Refusal(Worded, diagram, "HoldsBox", b, a));
    }

    [Fact]
    public void ALinkThatBreaksNothing_IsNotRefused()
    {
        var diagram = new DislDiagram(Worded);
        var a = Node(diagram, "Box", "a");
        var b = Node(diagram, "Bag", "b");

        Assert.Equal("", Refusal(Worded, diagram, "HoldsBag", a, b));
    }

    [Fact]
    public void AReconnectedRelation_IsNotCountedAgainstItself()
    {
        var diagram = new DislDiagram(Worded);
        var a = Node(diagram, "Box", "a");
        var b = Node(diagram, "Bag", "b");
        var c = Node(diagram, "Box", "c");
        var held = diagram.AddRelation("HoldsBox", "ac", a, c);
        diagram.AddRelation("HoldsBox", "ca", c, a);

        Assert.Equal("", Refusal(Worded, diagram, "HoldsBox", b, c, held));
    }

    [Fact]
    public void WithoutARefusal_TheRuntimeWordsIt_NeverTakingTheMessage()
    {
        var specification = Specifications.Loaded(Specifications.With(Metamodel + """
            ,
            "constraints": { "builtIn": { "std.endpoints": { "message": "Not this one." } } }
            """));
        var diagram = new DislDiagram(specification);
        var box = Node(diagram, "Box", "a");
        var bag = Node(diagram, "Bag", "b");

        Assert.Equal("std.endpoints | error | A HoldsBox cannot have this target. | a,b", Refusal(specification, diagram, "HoldsBox", box, bag));
    }

    [Fact]
    public void ABuiltInThatIsSwitchedOff_RefusesNothing_AndTheNextCheckSpeaks()
    {
        var specification = Specifications.Loaded(Specifications.With(Metamodel + """
            ,
            "constraints": { "builtIn": { "std.endpoints": { "enabled": false }, "std.acyclic": { "refusal": "A loop." } } }
            """));
        var diagram = new DislDiagram(specification);
        var box = Node(diagram, "Box", "a");

        Assert.Equal("std.acyclic | error | A loop. | a,a", Refusal(specification, diagram, "HoldsBox", box, box));
    }

    [Fact]
    public void ADeclaredConnectConstraint_RefusesAfterTheBuiltIns()
    {
        var specification = Specifications.Loaded(Specifications.With(Metamodel + """
            ,
            "constraints": {
              "builtIn": { "std.multiplicity": { "refusal": "Full." } },
              "rules": [
                { "id": "noBags", "kind": "connect", "when": "relationType == 'HoldsBag'", "rule": "target.name != 'b'", "message": { "cel": "'Not into ' + target.name + '.'" } }
              ]
            }
            """));
        var diagram = new DislDiagram(specification);
        var a = Node(diagram, "Box", "a");
        var b = Node(diagram, "Bag", "b");
        var c = Node(diagram, "Box", "c");

        Assert.Equal("noBags | error | Not into b. | a,b", Refusal(specification, diagram, "HoldsBag", a, b));
        diagram.AddRelation("HoldsBag", "cb", c, b);
        Assert.Equal("std.multiplicity | error | Full. | a,b", Refusal(specification, diagram, "HoldsBag", a, b));
    }
}
