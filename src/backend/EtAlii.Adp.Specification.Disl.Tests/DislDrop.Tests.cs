using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>A toolbox tool dropped on a drop target, resolved by its own <c>drop.targets</c> (DISL §7.2).</summary>
public class DislDropTests
{
    private const string Parts = """
        "metamodel": {
          "types": {
            "Thing": { "abstract": true, "attributes": { "name": { "type": "string" } } },
            "Box": { "extends": ["Thing"] },
            "Crate": { "extends": ["Thing"] }
          }
        },
        "toolbox": { "groups": [ { "id": "all", "tools": [
          { "id": "anywhere", "kind": "operation", "operation": "addTop", "mode": "drop",
            "drop": { "targets": [ { "on": ["canvas", "Thing"], "kind": "operation", "operation": "addTop" } ] } },
          { "id": "inBox", "kind": "operation", "operation": "addInside", "mode": "drop",
            "drop": { "targets": [ { "on": ["Box"], "kind": "operation", "operation": "addInside" } ], "elsewhere": "refuse",
              "refusal": { "cel": "dropTarget == null ? 'Drop it on a box.' : 'Not on a ' + dropTarget.type + '.'" } } },
          { "id": "never", "kind": "operation", "operation": "addNever", "mode": "drop",
            "drop": { "targets": [ { "on": ["canvas", "Thing"], "kind": "operation", "operation": "addNever" } ] } },
          { "id": "quiet", "kind": "operation", "operation": "addInside", "mode": "drop",
            "drop": { "targets": [ { "on": ["Box"], "kind": "operation", "operation": "addInside" } ], "elsewhere": "ignore" } },
          { "id": "worded", "kind": "operation", "operation": "addInside", "mode": "drop",
            "drop": { "targets": [ { "on": ["Box"], "kind": "operation", "operation": "addInside" } ], "elsewhere": "refuse" } },
          { "id": "plain", "kind": "operation", "operation": "addInside" },
          { "id": "named", "kind": "operation", "operation": "addNamed", "mode": "drop",
            "drop": { "targets": [
              { "on": ["Box"], "kind": "operation", "operation": "addNamed", "args": { "name": "self.name + '/' + string(position.x)" } },
              { "on": ["canvas"], "kind": "operation", "operation": "addNamed", "args": { "name": "'top ' + string(diagram.nodesOfType('Box').size())" } }
            ] } }
        ] } ] },
        "behavior": {
          "operations": {
            "addTop": { "label": "Add", "actions": [ { "create": { "type": "'Box'" } } ] },
            "addInside": { "label": "Add inside", "for": ["Box"], "actions": [ { "create": { "type": "'Crate'", "parent": "self" } } ] },
            "addNever": { "label": "Add never", "unavailable": [ "Not yet." ], "actions": [] },
            "addNamed": { "label": "Add named", "params": { "name": { "type": "string" } }, "actions": [ { "create": { "type": "'Box'", "attributes": { "name": "p.name" } } } ] }
          }
        }
        """;

    private static DislSpecification Specification { get; } = Specifications.Loaded(Specifications.With(Parts));

    private static (DislDiagram Diagram, DislElement Box, DislElement Crate) Diagram()
    {
        var diagram = new DislDiagram(Specification);
        var box = diagram.AddNode("Box", "b", new Dictionary<string, object?> { ["name"] = "B" });
        var crate = diagram.AddNode("Crate", "c", new Dictionary<string, object?> { ["name"] = "C" });
        return (diagram, box, crate);
    }

    [Fact]
    public void ATargetNamingTheDropTargetsSupertype_RunsOnIt_AndCanvasRunsOnTheDiagram()
    {
        // Arrange.
        (DislDiagram diagram, DislElement box, DislElement crate) = Diagram();

        // Act.
        var onBox = DislDrop.Resolve(Specification, "anywhere", diagram, box);
        var onCrate = DislDrop.Resolve(Specification, "anywhere", diagram, crate);
        var onCanvas = DislDrop.Resolve(Specification, "anywhere", diagram, null);

        // Assert.
        Assert.Equal(("operation", "addTop", box), Landing(onBox));
        Assert.Equal(("operation", "addTop", crate), Landing(onCrate));
        Assert.Equal(("operation", "addTop", (DislElement?)null), Landing(onCanvas));
    }

    [Fact]
    public void ADropNoTargetTakes_IsRefusedWithTheToolsSentence_EvaluatedWithTheDropTarget()
    {
        // Arrange.
        (DislDiagram diagram, DislElement box, DislElement crate) = Diagram();

        // Act.
        var onBox = DislDrop.Resolve(Specification, "inBox", diagram, box);
        var onCrate = DislDrop.Resolve(Specification, "inBox", diagram, crate);
        var onCanvas = DislDrop.Resolve(Specification, "inBox", diagram, null);

        // Assert.
        Assert.Equal(("operation", "addInside", box), Landing(onBox));
        Assert.Equal(new DislDropOutcome.Refused("Not on a Crate."), onCrate);
        Assert.Equal(new DislDropOutcome.Refused("Drop it on a box."), onCanvas);
    }

    [Fact]
    public void AMatchingTargetWhoseOperationIsUnavailable_IsRefusedWithItsReason()
    {
        // Act.
        var outcome = DislDrop.Resolve(Specification, "never", Diagram().Diagram, null);

        // Assert.
        Assert.Equal(new DislDropOutcome.Refused("Not yet."), outcome);
    }

    [Fact]
    public void Elsewhere_IsHandedBack_AndARefusalNobodyWordedIsTheRuntimes()
    {
        // Arrange.
        (DislDiagram diagram, _, DislElement crate) = Diagram();

        // Act.
        var ignored = DislDrop.Resolve(Specification, "quiet", diagram, crate);
        var worded = DislDrop.Resolve(Specification, "worded", diagram, crate);

        // Assert.
        Assert.Equal("ignore", Assert.IsType<DislDropOutcome.Elsewhere>(ignored).What);
        Assert.Equal(new DislDropOutcome.Refused(DislDrop.DefaultRefusal), worded);
    }

    [Fact]
    public void AnOperationToolWithoutDrop_RunsOnTheDropTargetItsOperationIsFor_ElseOnTheDiagram()
    {
        // Arrange.
        (DislDiagram diagram, DislElement box, DislElement crate) = Diagram();

        // Act.
        var onBox = DislDrop.Resolve(Specification, "plain", diagram, box);
        var onCrate = DislDrop.Resolve(Specification, "plain", diagram, crate);

        // Assert.
        Assert.Equal(("operation", "addInside", box), Landing(onBox));
        Assert.Equal(("operation", "addInside", (DislElement?)null), Landing(onCrate));
    }

    [Fact]
    public void AMatchingTargetsArgs_AreEvaluatedWhereTheDropLands()
    {
        // Arrange.
        (DislDiagram diagram, DislElement box, _) = Diagram();
        var position = new Dictionary<string, object?>(StringComparer.Ordinal) { ["x"] = 7L, ["y"] = 9L };

        // Act.
        var onBox = Assert.IsType<DislDropOutcome.Target>(DislDrop.Resolve(Specification, "named", diagram, box, position: position));
        var onCanvas = Assert.IsType<DislDropOutcome.Target>(DislDrop.Resolve(Specification, "named", diagram, null));
        var without = Assert.IsType<DislDropOutcome.Target>(DislDrop.Resolve(Specification, "anywhere", diagram, null));

        // Assert.
        Assert.Equal("B/7", onBox.Arguments["name"]);
        Assert.Equal("top 1", onCanvas.Arguments["name"]);
        Assert.Empty(without.Arguments);
    }

    /// <summary>What a drop that matched a target runs, and on what.</summary>
    private static (string Kind, string? Operation, DislElement? On) Landing(DislDropOutcome? outcome)
    {
        var target = Assert.IsType<DislDropOutcome.Target>(outcome);
        return (target.Kind, target.Operation, target.On);
    }

    [Fact]
    public void AToolTheSpecificationDoesNotHave_ResolvesToNothing()
    {
        // Act.
        var outcome = DislDrop.Resolve(Specification, "missing", Diagram().Diagram, null);

        // Assert.
        Assert.Null(outcome);
    }
}
