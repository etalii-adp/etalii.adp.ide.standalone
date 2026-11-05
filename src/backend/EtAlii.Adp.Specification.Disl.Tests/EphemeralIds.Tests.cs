using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// Ephemeral ids (DISL §11.5.3): an id rule's <c>ephemeral</c>, a bool or an Expression in the identity context, and its
/// <c>reason</c>, which a host reads to refuse a gesture that would store the id.
/// </summary>
public class EphemeralIdsTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "metamodel": { "types": {
          "Node": { "attributes": { "name": { "type": "string" }, "blank": { "type": "bool" } } },
          "Anonymous": { "attributes": { "name": { "type": "string" } } },
          "Special": { "extends": "Anonymous" },
          "Named": { "attributes": { "name": { "type": "string" } } },
          "Plain": { "attributes": { "name": { "type": "string" } } },
          "Bare": {} } },
        "persistence": { "ids": {
          "strategy": "natural",
          "types": {
            "Node": { "ephemeral": { "cel": "self.blank" }, "reason": { "cel": "'Node ' + self.name + ' has no lasting identity.'" } },
            "Anonymous": { "ephemeral": true, "reason": "An anonymous element cannot be arranged." },
            "Named": { "ephemeral": "false" },
            "Bare": { "ephemeral": true } } } }
        """));

    private static DislElement Add(DislDiagram diagram, string type, string id, Dictionary<string, object?> attributes) =>
        diagram.Add(new DislElement(diagram, Specification.Metamodel.TypeOf(type)!, id, attributes));

    [Fact]
    public void AnEphemeralExpression_DecidesPerElement_AndItsReasonIsEvaluatedForIt()
    {
        // Arrange.
        var diagram = new DislDiagram(Specification);
        var blank = Add(diagram, "Node", "n1", new Dictionary<string, object?> { ["name"] = "first", ["blank"] = true });
        var named = Add(diagram, "Node", "n2", new Dictionary<string, object?> { ["name"] = "second", ["blank"] = false });

        // Act and assert.
        Assert.True(Specification.IsEphemeral(blank));
        Assert.Equal("Node first has no lasting identity.", Specification.EphemeralReason(blank));
        Assert.False(Specification.IsEphemeral(named));
        Assert.Null(Specification.EphemeralReason(named));
    }

    [Fact]
    public void EphemeralTrue_HoldsForEveryElement_OfTheTypeAndOfTypesThatExtendIt()
    {
        // Arrange.
        var diagram = new DislDiagram(Specification);
        var anonymous = Add(diagram, "Anonymous", "a1", new Dictionary<string, object?>());
        var special = Add(diagram, "Special", "s1", new Dictionary<string, object?>());

        // Act and assert.
        Assert.True(Specification.IsEphemeral(anonymous));
        Assert.True(Specification.IsEphemeral(special));
        Assert.Equal("An anonymous element cannot be arranged.", Specification.EphemeralReason(special));
    }

    [Fact]
    public void ARuleThatIsNotEphemeral_AndATypeWithoutARule_AreNot()
    {
        // Arrange.
        var diagram = new DislDiagram(Specification);
        var named = Add(diagram, "Named", "x", new Dictionary<string, object?>());
        var plain = Add(diagram, "Plain", "p", new Dictionary<string, object?>());

        // Act and assert.
        Assert.False(Specification.IsEphemeral(named));
        Assert.False(Specification.IsEphemeral(plain));
        Assert.Null(Specification.EphemeralReason(plain));
    }

    private static readonly Dictionary<string, object?> Bounds = new() { ["x"] = 0.0, ["y"] = 0.0, ["width"] = 1.0, ["height"] = 1.0 };

    private static string Refused(IReadOnlyList<DislFinding> refusals) => refusals.Count == 0 ? "" : $"{refusals[0].Code}: {refusals[0].Message}";

    [Fact]
    public void APlacementOfAnEphemeralElement_IsRefusedWithItsRulesReason()
    {
        // Arrange.
        var diagram = new DislDiagram(Specification);
        var blank = Add(diagram, "Node", "n1", new Dictionary<string, object?> { ["name"] = "first", ["blank"] = true });
        var anonymous = Add(diagram, "Anonymous", "a1", new Dictionary<string, object?>());

        // Act and assert.
        Assert.Equal("std.ephemeralId: Node first has no lasting identity.", Refused(GestureConstraintEvaluator.Placement(Specification, blank, "move", Bounds, Bounds)));
        Assert.Equal("std.ephemeralId: An anonymous element cannot be arranged.", Refused(GestureConstraintEvaluator.Placement(Specification, anonymous, "resize", Bounds, Bounds)));
        Assert.Equal("std.ephemeralId: An anonymous element cannot be arranged.", Refused(GestureConstraintEvaluator.Placement(Specification, anonymous, "reparent", Bounds, Bounds)));
    }

    [Fact]
    public void AnEphemeralElementWithoutAReason_IsRefusedWithTheRuntimesSentence_AndAStableOneIsNot()
    {
        // Arrange.
        var diagram = new DislDiagram(Specification);
        var bare = Add(diagram, "Bare", "b", new Dictionary<string, object?>());
        var named = Add(diagram, "Node", "n2", new Dictionary<string, object?> { ["name"] = "second", ["blank"] = false });

        // Act and assert.
        Assert.Equal(
            "std.ephemeralId: This element's identity does not survive reading the file again, so nothing can be stored for it.",
            Refused(GestureConstraintEvaluator.Placement(Specification, bare, "move", Bounds, Bounds)));
        Assert.Equal("", Refused(GestureConstraintEvaluator.Placement(Specification, named, "move", Bounds, Bounds)));
    }

    [Fact]
    public void ADeletionOfAnEphemeralElement_StoresNothing_AndIsNotRefused()
    {
        // Arrange.
        var diagram = new DislDiagram(Specification);
        var anonymous = Add(diagram, "Anonymous", "a1", new Dictionary<string, object?>());

        // Act and assert.
        Assert.Empty(GestureConstraintEvaluator.Delete(Specification, anonymous));
    }
}
