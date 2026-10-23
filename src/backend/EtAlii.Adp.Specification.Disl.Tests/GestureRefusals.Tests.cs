using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// A node or edge notation's <c>refusals</c> (DISL §6.9 <i>GestureRefusals</i>, §6.10): the first of a gesture's Reasons
/// that applies, evaluated in the element context, refuses that gesture on an element of the kind, before every other
/// check (§8.4, step 1). The notation is the nearest one in the type's linearisation, the most specific winning whole.
/// </summary>
public class GestureRefusalsTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "metamodel": {
          "types": {
            "Shape": { "abstract": true, "attributes": { "name": { "type": "string" }, "locked": { "type": "bool" } }, "children": { "allowed": ["Shape"] } },
            "Box": { "extends": "Shape" },
            "Circle": { "extends": "Shape" } },
          "relations": { "Link": { "source": "Shape", "target": "Shape" } } },
        "notation": {
          "nodes": {
            "Shape": { "refusals": { "move": ["A shape sits where the layout puts it."], "connect": [{ "cel": "'Nothing connects to ' + self.name + '.'" }] } },
            "Box": { "refusals": {
              "reparent": ["A box stays where it is."],
              "delete": [{ "when": "self.locked", "message": "A locked box cannot be deleted." }] } } },
          "edges": { "Link": { "refusals": { "move": ["An edge is drawn between its endpoints; move one of those instead."], "reconnect": ["A link keeps its ends."] } } } },
        "constraints": { "rules": [
          { "id": "never", "kind": "placement", "scope": "Shape", "rule": "false", "message": "A declared refusal." } ] }
        """));

    private static readonly Dictionary<string, object?> Bounds = new() { ["x"] = 0.0, ["y"] = 0.0, ["width"] = 1.0, ["height"] = 1.0 };

    private readonly DislDiagram _diagram = new(Specification);
    private readonly DislElement _box;
    private readonly DislElement _locked;
    private readonly DislElement _circle;
    private readonly DislElement _link;

    public GestureRefusalsTests()
    {
        _box = _diagram.AddNode("Box", "b", new Dictionary<string, object?> { ["name"] = "B" });
        _locked = _diagram.AddNode("Box", "l", new Dictionary<string, object?> { ["name"] = "L", ["locked"] = true });
        _circle = _diagram.AddNode("Circle", "c", new Dictionary<string, object?> { ["name"] = "C" });
        _link = _diagram.AddRelation("Link", "bc", _box, _circle);
    }

    private static string First(IReadOnlyList<DislFinding> refusals) => refusals.Count == 0 ? "" : $"{refusals[0].Code}: {refusals[0].Message}";

    [Fact]
    public void AnEdgesMove_IsRefusedWithItsNotationsReason_BeforeADeclaredConstraint() =>
        Assert.Equal("std.switchedOff: An edge is drawn between its endpoints; move one of those instead.", First(GestureConstraintEvaluator.Placement(Specification, _link, "move", Bounds, Bounds)));

    [Fact]
    public void ATypeWithoutItsOwnNotation_UsesItsNearestSupertypes() =>
        Assert.Equal("std.switchedOff: A shape sits where the layout puts it.", First(GestureConstraintEvaluator.Placement(Specification, _circle, "move", Bounds, Bounds)));

    [Fact]
    public void TheMostSpecificNotationWinsWhole_SoAGestureItDoesNotRefuseReachesTheConstraints()
    {
        // Act.
        var move = GestureConstraintEvaluator.Placement(Specification, _box, "move", Bounds, Bounds);
        var reparent = GestureConstraintEvaluator.Placement(Specification, _box, "reparent", Bounds, Bounds);

        // Assert.
        Assert.Equal("never: A declared refusal.", First(move));
        Assert.Equal("std.switchedOff: A box stays where it is.", First(reparent));
    }

    [Fact]
    public void AReasonWhoseWhenDoesNotHold_DoesNotRefuse()
    {
        // Act.
        var deletable = GestureConstraintEvaluator.Delete(Specification, _box);
        var locked = GestureConstraintEvaluator.Delete(Specification, _locked);

        // Assert.
        Assert.Equal("", First(deletable));
        Assert.Equal("std.switchedOff: A locked box cannot be deleted.", First(locked));
        Assert.Equal(["l"], Assert.Single(locked).ElementIds);
    }

    [Fact]
    public void AConnect_IsRefusedForAnEndWhoseNotationRefusesIt_InTheElementContext()
    {
        // Act.
        var refusal = GestureConstraintEvaluator.Connect(Specification, _diagram, "Link", _box, _circle);

        // Assert.
        Assert.Equal("std.switchedOff", refusal?.Code);
        Assert.Equal("Nothing connects to C.", refusal?.Message);
        Assert.Equal(["c"], refusal?.ElementIds);
    }

    [Fact]
    public void AReconnect_IsRefusedWithTheRelationsReconnectReason() =>
        Assert.Equal("A link keeps its ends.", GestureConstraintEvaluator.Connect(Specification, _diagram, "Link", _box, _circle, self: _link)?.Message);
}
