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
          "Plain": { "attributes": { "name": { "type": "string" } } } } },
        "persistence": { "ids": {
          "strategy": "natural",
          "types": {
            "Node": { "ephemeral": { "cel": "self.blank" }, "reason": { "cel": "'Node ' + self.name + ' has no lasting identity.'" } },
            "Anonymous": { "ephemeral": true, "reason": "An anonymous element cannot be arranged." },
            "Named": { "ephemeral": "false" } } } }
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
}
