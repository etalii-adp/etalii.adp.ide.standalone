using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// The <c>after</c> hooks of a relation drawn (DISL §9.2, <c>on: connect</c>): every hook whose <c>for</c>
/// takes the new relation runs, with <c>self</c> the relation and <c>old</c> null, its actions seeing what
/// earlier iterations made (§9.4), and a hook for another event or another type does not.
/// </summary>
public class ConnectHooksTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "disl": "0.3",
        "metamodel": {
          "types": { "Box": { "attributes": { "name": { "type": "string" } } },
                     "Tag": { "attributes": { "text": { "type": "string" }, "about": { "type": "Box", "many": true } } } },
          "relations": { "Wire": { "source": ["Box"], "target": ["Box"] }, "Cord": { "source": ["Box"], "target": ["Box"] } } },
        "behavior": {
          "hooks": [
            { "id": "tagEnds", "on": "connect", "for": "Wire",
              "actions": [ { "forEach": "[self.source, self.target]", "as": "end",
                             "do": [ { "create": { "type": "'Tag'",
                                                   "attributes": { "text": "end.name + ' ' + string(diagram.nodesOfType('Tag').size() + 1) + (old == null ? '' : '!')",
                                                                   "about": "[end]" } } } ] } ] },
            { "id": "notOnCreate", "on": "create", "for": "Wire",
              "actions": [ { "create": { "type": "'Tag'", "attributes": { "text": "'create'" } } } ] },
            { "id": "notWhenFalse", "on": "connect", "for": "Wire", "when": "self.source.name == 'nobody'",
              "actions": [ { "create": { "type": "'Tag'", "attributes": { "text": "'when'" } } } ] },
            { "id": "notForCord", "on": ["connect"], "for": "Cord",
              "actions": [ { "create": { "type": "'Tag'", "attributes": { "text": "'cord'" } } } ] }
          ]
        }
        """));

    [Fact]
    public void ARelationDrawn_RunsTheConnectHooksForItsType_WithSelfTheRelation_EachIterationSeeingTheOnesBefore()
    {
        // Arrange: the host draws the relation on the working diagram first.
        var diagram = new DislDiagram(Specification);
        var a = diagram.AddNode("Box", "a", new Dictionary<string, object?> { ["name"] = "A" });
        var b = diagram.AddNode("Box", "b", new Dictionary<string, object?> { ["name"] = "B" });
        var wire = diagram.AddRelation("Wire", "w", a, b);

        // Act.
        var transaction = HookRunner.AfterConnect(Specification, wire, DislIds.Fixed("t1", "t2"));

        // Assert.
        Assert.True(transaction.WasApplied, transaction.Refusal);
        var created = transaction.Changes.Cast<DislChange.Create>().ToList();
        Assert.Equal(["A 1", "B 2"], created.Select(change => change.Attributes["text"]));
        Assert.Equal([["a"], ["b"]], created.Select(change => (IEnumerable<object?>)change.Attributes["about"]!));
    }

    [Fact]
    public void ARelationOfAnotherType_RunsNoneOfThem()
    {
        // Arrange.
        var diagram = new DislDiagram(Specification);
        var a = diagram.AddNode("Box", "a", new Dictionary<string, object?> { ["name"] = "A" });
        var cord = diagram.AddRelation("Cord", "c", a, a);
        var hose = diagram.AddRelation("Wire", "w", a, a);

        // Act.
        var corded = HookRunner.AfterConnect(Specification, cord, DislIds.Fixed("t1"));
        var changed = HookRunner.AfterChange(Specification, hose, HookRunner.Snapshot(hose), [], DislIds.Fixed());

        // Assert: the cord's own hook runs and nothing of the wire's; a change of the wire runs no connect hook.
        Assert.Equal(["cord"], corded.Changes.Cast<DislChange.Create>().Select(change => change.Attributes["text"]));
        Assert.Empty(changed.Changes);
    }
}
