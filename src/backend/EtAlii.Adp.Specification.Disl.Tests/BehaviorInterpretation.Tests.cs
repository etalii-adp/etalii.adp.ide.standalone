using EtAlii.Adp.Specification.Fbl.Planning;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>Operations, hooks, deletion and the writing of their changes (DISL §9.2 to §9.5).</summary>
public class BehaviorInterpretationTests
{
    private const string Parts = """
        "metamodel": {
          "enums": { "Size": { "values": { "small": { "value": "S" }, "large": { "value": "L" } } } },
          "types": {
            "Box": { "attributes": { "name": { "type": "string" }, "size": { "type": "Size" }, "made": { "type": "yearMonth" }, "count": { "type": "int" }, "copy": { "type": "int" } } }
          },
          "relations": { "Link": { "source": "Box", "target": "Box", "directed": true, "attributes": { "weight": { "type": "number" } } } }
        },
        "persistence": { "typeMap": { "Crate": { "as": "Box", "attributes": { "title": "name" } } } },
        "toolbox": { "groups": [ { "id": "all", "tools": [ { "id": "box", "creates": "Box", "initial": { "name": { "cel": "'Box ' + string(int(position.x))" }, "count": 3 }, "after": "editLabel" } ] } ] },
        "behavior": {
          "operations": {
            "addBox": {
              "label": "Add", "for": "diagram",
              "actions": [
                { "let": { "n": "size(diagram.nodes) + 1" } },
                { "create": { "type": "'Box'", "at": "position", "attributes": { "name": "'Box ' + string(n)", "made": "yearMonth(2001, 2)" } }, "as": "added" },
                { "set": { "count": "n * 10" }, "target": "added" },
                { "set": { "size": "'large'" }, "target": "added", "when": "n > 5" },
                { "select": "added" },
                { "editLabel": { "target": "added" } }
              ]
            },
            "clear": {
              "label": "Clear", "for": ["Box"],
              "unavailable": [ { "when": "!has(self.count)", "message": "Nothing to clear." } ],
              "actions": [ { "unset": ["count", "size"] }, { "if": "self.name == ''", "then": [ { "set": { "name": "'unnamed'" } } ], "else": [ { "layout": { "algorithm": "tidy" } } ] } ]
            },
            "refuse": { "label": "Refuse", "for": ["Box"], "actions": [ { "set": { "count": "1" } }, { "abort": { "message": "'No, ' + self.name + '.'" } } ] },
            "unknown": { "label": "Unknown", "for": ["Box"], "actions": [ { "resize": { "width": "10" } } ] }
          },
          "hooks": [
            { "id": "copyCount", "on": "change", "for": "Box", "attribute": "count", "actions": [ { "set": { "copy": "self.count * 100 + (old == null ? 0 : old.count)" } } ] },
            { "id": "rename", "on": "change", "for": "Box", "attribute": ["copy"], "when": "self.copy > 0", "actions": [ { "set": { "name": "self.name + '!'" } } ] },
            { "id": "never", "on": "change", "for": "Box", "attribute": "name", "phase": "before", "actions": [ { "set": { "count": "0" } } ] }
          ],
          "deletion": {
            "Box": { "relations": "delete", "confirm": { "count": "self.incoming.size() + self.outgoing.size()", "threshold": 2, "title": "Remove box",
              "message": { "cel": "'Remove ' + self.name + ' and ' + string(count) + ' links?'" }, "confirmLabel": "Remove", "danger": false } }
          }
        }
        """;

    private static DislSpecification Specification { get; } = Specifications.Loaded(Specifications.With(Parts));

    private static string Text(DislTransaction transaction) =>
        (transaction.Refusal is { } refusal ? $"refused: {refusal}\n" : "")
        + string.Join("\n", transaction.Changes.Select(change => change switch
        {
            DislChange.Create create => $"create {create.Type} {create.Id} {Values(create.Attributes)}",
            DislChange.Set set => $"set {set.ElementId} {Values(set.Attributes)}",
            DislChange.Remove remove => $"remove {remove.ElementId}",
            _ => change.ToString(),
        }).Concat(transaction.HostActions.Select(action => action switch
        {
            HostAction.Select select => $"select {string.Join(",", select.ElementIds)}",
            HostAction.EditLabel edit => $"editLabel {edit.ElementId}",
            HostAction.Layout layout => $"layout {layout.Algorithm}",
            _ => action.ToString(),
        })));

    private static string Values(IReadOnlyDictionary<string, object?> values) =>
        string.Join(" ", values.Select(pair => $"{pair.Key}={pair.Value ?? "null"}"));

    [Fact]
    public void AnOperation_RunsItsActionsInOrder_EachSeeingTheOnesBefore()
    {
        var diagram = new DislDiagram(Specification);
        diagram.AddNode("Box", "a", new Dictionary<string, object?> { ["name"] = "A" });

        var transaction = OperationInterpreter.Run(Specification, "addBox", diagram, null, DislIds.Fixed("b"), new DislInvocation(new Dictionary<string, object?> { ["x"] = 1.0, ["y"] = 2.0 }));

        Assert.Equal("create Box b name=Box 2 made=24013\nset b count=20\nselect b\neditLabel b", Text(transaction));
        Assert.Equal(20L, diagram.ElementById("b")!.ValueOf("count"));
    }

    [Fact]
    public void AnUnavailableOperation_IsRefusedWithItsReason_AndAnAbortOrAnUnknownActionRollsBack()
    {
        var diagram = new DislDiagram(Specification);
        var empty = diagram.AddNode("Box", "a", new Dictionary<string, object?> { ["name"] = "" });
        var full = diagram.AddNode("Box", "b", new Dictionary<string, object?> { ["name"] = "B", ["count"] = 4L, ["size"] = "small" });

        Assert.Equal("refused: Nothing to clear.\n", Text(OperationInterpreter.Run(Specification, "clear", diagram, empty, DislIds.Fixed())));
        Assert.Equal("set b count=null size=null\nlayout tidy", Text(OperationInterpreter.Run(Specification, "clear", diagram, full, DislIds.Fixed())));
        Assert.Equal("refused: No, B.\n", Text(OperationInterpreter.Run(Specification, "refuse", diagram, full, DislIds.Fixed())));
        Assert.StartsWith("refused: This runtime cannot run a `resize` action", Text(OperationInterpreter.Run(Specification, "unknown", diagram, full, DislIds.Fixed())), StringComparison.Ordinal);
    }

    [Fact]
    public void ADrop_CreatesItsTypeWithItsInitialValues_AndHandsBackItsAfter()
    {
        var diagram = new DislDiagram(Specification);

        var transaction = OperationInterpreter.Drop(Specification, "box", diagram, new Dictionary<string, object?> { ["x"] = 7.5, ["y"] = 0.0 }, DislIds.Fixed("n"));

        Assert.Equal("create Box n name=Box 7 count=3\neditLabel n", Text(transaction));
    }

    [Fact]
    public void AChangesHooks_RunAfterIt_WithOld_EachOncePerElement_AndTheirChangesRunFurtherHooks()
    {
        var diagram = new DislDiagram(Specification);
        var box = diagram.AddNode("Box", "a", new Dictionary<string, object?> { ["name"] = "A", ["count"] = 2L });
        var old = HookRunner.Snapshot(box);
        HookRunner.Store(box, "count", 5L);

        var transaction = HookRunner.AfterChange(Specification, box, old, ["count"], DislIds.Fixed());

        Assert.Equal("set a copy=502\nset a name=A!", Text(transaction));
        Assert.Equal("", Text(HookRunner.AfterChange(Specification, box, old, ["made"], DislIds.Fixed())));
    }

    [Fact]
    public void ADeletion_RemovesTheRelationsFirst_AndAsksOnlyFromItsThreshold()
    {
        var diagram = new DislDiagram(Specification);
        var a = diagram.AddNode("Box", "a", new Dictionary<string, object?> { ["name"] = "A" });
        var b = diagram.AddNode("Box", "b", new Dictionary<string, object?> { ["name"] = "B" });
        diagram.AddRelation("Link", "ab", a, b);

        Assert.Equal("remove ab\nremove a", Text(DeletionPolicy.Changes(Specification, a)));
        Assert.Null(DeletionPolicy.Confirmation(Specification, a));

        diagram.AddRelation("Link", "ba", b, a);
        Assert.Equal(new DislConfirmation("Remove box", "Remove A and 2 links?", "Remove", false, 2), DeletionPolicy.Confirmation(Specification, a));
    }

    [Fact]
    public void AChange_IsWrittenInTheBindingsTermsAndStoredForms()
    {
        var created = DislWrite.ToFbl(Specification, new DislChange.Create("Box", "n", new Dictionary<string, object?> { ["name"] = "N", ["made"] = (2001L * 12) + 1, ["size"] = "large" }, null, null));
        var add = Assert.IsType<ModelChange.Add>(created);

        Assert.Equal("Crate n title=N made=2001-02 size=L", $"{add.Type} {add.Id} {Values(add.Attributes)}");
        Assert.Equal("-0001-12", DislWrite.StoredForm(Specification, "Box", "made", -1L));
        Assert.Equal(new ModelChange.Move("n", null, -1), DislWrite.ToFbl(Specification, new DislChange.Reparent("n", null)));
    }

    [Fact]
    public void ABase36Id_Is25LowercaseCharacters()
    {
        var specification = Specifications.Loaded(Specifications.With(""" "persistence": { "ids": { "strategy": "uuid-v4", "encoding": "base36" } } """));

        Assert.Matches("^[0-9a-z]{25}$", DislIds.Of(specification).Next("Thing"));
    }

    /// <summary>A bundled definition's <c>create</c> with an <c>at</c> hands the host the point it was asked at, placement being the host's.</summary>
    [Fact]
    public void TheHypeCycleGraphsAddTrendHere_HandsBackTheClickedPoint()
    {
        // Arrange.
        var diagram = new DislDiagram(Derivations.HypeCycle);
        var position = new Dictionary<string, object?> { ["x"] = 1900.0 * 12, ["y"] = 3.0 };

        // Act.
        var transaction = OperationInterpreter.Run(Derivations.HypeCycle, "addTrendHere", diagram, null, DislIds.Fixed("t"), new DislInvocation(position));

        // Assert.
        var create = Assert.IsType<DislChange.Create>(Assert.Single(transaction.Changes));
        var at = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(create.At);
        Assert.Equal(1900.0 * 12, at["x"]);
        Assert.Equal(3.0, at["y"]);
    }

    /// <summary>A bundled definition's <c>editLabel</c> that names a label hands that name to the host.</summary>
    [Fact]
    public void TheBehaviorModelsAddChild_OpensTheEditorOnTheLabelItNames()
    {
        // Arrange.
        var diagram = Derivations.BehaviorDiagram();
        var invocation = new DislInvocation(Parameters: new Dictionary<string, object?> { ["kind"] = "Check" });

        // Act.
        var transaction = OperationInterpreter.Run(Derivations.BehaviorModel, "addChild", diagram, Derivations.Element(diagram, "1"), DislIds.Fixed("n"), invocation);

        // Assert.
        var edit = Assert.IsType<HostAction.EditLabel>(transaction.HostActions[^1]);
        Assert.Equal("n", edit.ElementId);
        Assert.Equal("label", edit.Label);
    }

    /// <summary>A bundled definition's operation that a plugin carries out is handed back whole, naming the plugin.</summary>
    [Fact]
    public void TheBehaviorModelsArrange_HandsBackThePluginItNames()
    {
        // Act.
        var transaction = OperationInterpreter.Run(Derivations.BehaviorModel, "arrange", Derivations.BehaviorDiagram(), null, DislIds.Fixed());

        // Assert.
        var plugin = Assert.IsType<HostAction.Plugin>(Assert.Single(transaction.HostActions));
        Assert.Equal("net.etalii.adp.etalii.abmArrange", plugin.Name);
    }

    /// <summary>A bundled definition's deletion confirmation carries the count it was asked from.</summary>
    [Fact]
    public void TheHypeCycleGraphsTrendRemoval_IsConfirmedWithItsInfluenceCount()
    {
        // Arrange: the steam engine has two influences in and one out.
        var diagram = Derivations.HypeCycleDiagram();

        // Act.
        var confirmation = DeletionPolicy.Confirmation(Derivations.HypeCycle, Derivations.Element(diagram, "steam"));

        // Assert.
        Assert.NotNull(confirmation);
        Assert.Equal(3L, confirmation.Count);
    }
}
