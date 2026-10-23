using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// Budgets (DISL §3.2.1): what a budget counts, which candidates a truncating one keeps - by <c>order</c>, ties in
/// persistence order, whole <c>unit</c>s while they fit - and what it withholds, read in CEL with <c>budget(id)</c>,
/// shown as a notice and reported as a finding.
/// </summary>
public class BudgetsTests
{
    private const string Metamodel = """
        "metamodel": {
          "types": { "Item": { "attributes": { "name": { "type": "string" }, "rank": { "type": "int" }, "group": { "type": "string" } } } },
          "relations": { "Link": { "source": "Item", "target": "Item" } } }
        """;

    private static DislSpecification Loaded(string limits, string extra = "") => Specifications.Loaded(Specifications.With(Metamodel + ", \"language\": { \"id\": \"org.example.test\", \"version\": \"1.0.0\", \"limits\": { \"budgets\": " + limits + " } }" + extra));

    private static DislDiagram Diagram(DislSpecification specification, params (string Id, long Rank, string Group)[] items)
    {
        var diagram = new DislDiagram(specification);
        foreach ((string id, long rank, string group) in items)
        {
            diagram.AddNode("Item", id, attributes: new Dictionary<string, object?> { ["name"] = id, ["rank"] = rank, ["group"] = group });
        }
        return diagram;
    }

    private static object? Cel(DislDiagram diagram, string expression) =>
        diagram.Specification.Environment(DislContext.Element).Compile(expression).Evaluate(new Dictionary<string, object?> { ["self"] = diagram, ["diagram"] = diagram, ["env"] = diagram.Env.ToCel() });

    [Fact]
    public void ACountBudget_KeepsTheFirstMaxInPersistenceOrder_AndCelReadsItsState()
    {
        // Arrange.
        var specification = Loaded("""{ "items": { "measure": { "count": ["Item"] }, "max": 2 } }""");
        var diagram = Diagram(specification, ("a", 3, "x"), ("b", 1, "x"), ("c", 2, "y"));

        // Act.
        var state = diagram.Budget("items")!;

        // Assert.
        Assert.Equal((2L, 3L, true, true, true), (state.Shown, state.Total, state.Exceeded, state.Truncated, state.Withheld));
        Assert.Equal(["a", "b"], diagram.Nodes.Where(diagram.IsDrawn).Select(node => node.Id));
        Assert.Equal(new Dictionary<string, object?> { ["shown"] = 2L, ["total"] = 3L, ["truncated"] = true, ["withheld"] = true }, (IDictionary<string, object?>)Cel(diagram, "budget('items')")!);
    }

    [Fact]
    public void ABudget_IsComputedAgain_AfterTheDiagramChanges()
    {
        // Arrange.
        var specification = Loaded("""{ "items": { "measure": { "count": ["Item"] }, "max": 2 } }""");
        var diagram = Diagram(specification, ("a", 0, "x"), ("b", 0, "x"));
        Assert.False(diagram.Budget("items")!.Exceeded);

        // Act.
        diagram.AddNode("Item", "c");

        // Assert.
        Assert.True(diagram.Budget("items")!.Exceeded);
    }

    [Fact]
    public void AnOrder_SortsTheCandidates_ListKeysMemberByMember_TiesInPersistenceOrder()
    {
        // Arrange.
        var specification = Loaded("""{ "items": { "measure": { "count": ["Item"] }, "max": 2, "order": "[self.group, self.rank]" } }""");
        var diagram = Diagram(specification, ("a", 3, "y"), ("b", 2, "x"), ("c", 2, "x"), ("d", 1, "y"));

        // Act.
        var drawn = diagram.Nodes.Where(diagram.IsDrawn).Select(node => node.Id);

        // Assert.
        Assert.Equal(["b", "c"], drawn);
    }

    [Fact]
    public void TheDrawnCandidates_AreGivenInTheBudgetsOrder_WhetherOrNotItCuts()
    {
        // Arrange: one budget that is not exceeded and one that cuts, both ordered by rank.
        var roomy = Diagram(Loaded("""{ "items": { "measure": { "count": ["Item"] }, "max": 10, "order": "self.rank" } }"""), ("a", 3, "x"), ("b", 1, "x"), ("c", 2, "x"));
        var tight = Diagram(Loaded("""{ "items": { "measure": { "count": ["Item", "Link"] }, "max": 3, "order": "self.isA('Link') ? 9 : self.rank" } }"""), ("a", 3, "x"), ("b", 1, "x"), ("c", 2, "x"));
        tight.AddRelation("Link", "ba", tight.Nodes[1], tight.Nodes[0]);
        tight.AddRelation("Link", "bc", tight.Nodes[1], tight.Nodes[2]);

        // Act.
        var notCut = roomy.InBudgetOrder("items").Select(element => element.Id);
        var cut = tight.InBudgetOrder("items").Select(element => element.Id);

        // Assert: the cut keeps b, c and a, which fill the budget, and drops both links.
        Assert.Equal(["b", "c", "a"], notCut);
        Assert.Equal(["b", "c", "a"], cut);
        Assert.Empty(roomy.InBudgetOrder("unknown"));
    }

    [Fact]
    public void AUnit_IsKeptWhole_AndTheFirstUnitThatDoesNotFitEndsTheCut()
    {
        // Arrange.
        var specification = Loaded("""{ "items": { "measure": { "count": ["Item"] }, "max": 3, "unit": "self.group" } }""");
        var diagram = Diagram(specification, ("a", 0, "x"), ("b", 0, "y"), ("c", 0, "x"), ("d", 0, "y"), ("e", 0, "z"));

        // Act.
        var drawn = diagram.Nodes.Where(diagram.IsDrawn).Select(node => node.Id);

        // Assert.
        Assert.Equal(["a", "c"], drawn);
        Assert.Equal(2L, diagram.Budget("items")!.Shown);
    }

    [Fact]
    public void ARelation_IsDrawnOnlyWhenBothItsEndsAre()
    {
        // Arrange.
        var specification = Loaded("""{ "items": { "measure": { "count": ["Item"] }, "max": 2 } }""");
        var diagram = Diagram(specification, ("a", 0, "x"), ("b", 0, "x"), ("c", 0, "x"));
        var kept = diagram.AddRelation("Link", "ab", diagram.Nodes[0], diagram.Nodes[1]);
        var cut = diagram.AddRelation("Link", "bc", diagram.Nodes[1], diagram.Nodes[2]);

        // Act and assert.
        Assert.True(diagram.IsDrawn(kept));
        Assert.False(diagram.IsDrawn(cut));
    }

    [Fact]
    public void ACelMeasure_IsExceededWithoutCuttingAnything()
    {
        // Arrange.
        var specification = Loaded("""{ "size": { "measure": { "cel": "diagram.nodesOfType('Item').size() * 10" }, "max": 15 } }""");
        var diagram = Diagram(specification, ("a", 0, "x"), ("b", 0, "x"));

        // Act.
        var state = diagram.Budget("size")!;

        // Assert.
        Assert.Equal((20L, 20L, true, false, true), (state.Shown, state.Total, state.Exceeded, state.Truncated, state.Withheld));
        Assert.All(diagram.Nodes, node => Assert.True(diagram.IsDrawn(node)));
    }

    [Fact]
    public void BudgetWithoutAnId_AggregatesEveryBudget()
    {
        // Arrange.
        var specification = Loaded("""{ "items": { "measure": { "count": ["Item"] }, "max": 5 }, "size": { "measure": { "cel": "7" }, "max": 3, "withhold": { "edits": "none" } } }""");
        var diagram = Diagram(specification, ("a", 0, "x"));

        // Act.
        var all = (IDictionary<string, object?>)Cel(diagram, "budget()")!;

        // Assert.
        Assert.Equal(new Dictionary<string, object?> { ["shown"] = 8L, ["total"] = 8L, ["truncated"] = false, ["withheld"] = false }, all);
    }

    private const string Editing = """
        , "forms": { "inspector": { "for": ["Item"], "usage": ["inspector"], "items": [ { "attribute": "name", "id": "name", "widget": "text" } ] } },
        "toolbox": { "contextMenus": [ { "for": ["Item"], "tools": [
          { "kind": "operation", "operation": "rename", "label": "Rename" },
          { "kind": "operation", "operation": "focus", "label": "Focus" },
          { "kind": "delete", "label": "Delete" } ] } ] },
        "behavior": { "operations": {
          "rename": { "for": ["Item"], "actions": [ { "set": { "name": "'renamed'" } } ] },
          "focus": { "for": ["Item"], "actions": [ { "select": "self" } ] } } }
        """;

    private static DislSpecification Withholding(string edits) => Loaded(
        "{ \"items\": { \"measure\": { \"count\": [\"Item\"] }, \"max\": 1, \"withhold\": { \"edits\": \"" + edits + "\", \"reason\": \"Too many items to edit them here.\" } } }",
        Editing);

    [Fact]
    public void AWithheldEdit_IsTheReasonOfEveryModelChangingEntry_AndOfEveryRow()
    {
        // Arrange.
        var specification = Withholding("model");
        var diagram = Diagram(specification, ("a", 0, "x"), ("b", 0, "x"));
        var item = diagram.Nodes[0];

        // Act.
        var entries = ContextMenuDerivation.Derive(specification, DislMenuTarget.Element(item), new DislEnv(), WireIdMap.None).SelectMany(group => group.Entries);
        var rows = FormDerivation.Derive(specification, item, new DislEnv(), WireIdMap.None);

        // Assert.
        Assert.Equal(["Rename: Too many items to edit them here.", "Focus: ", "Delete: Too many items to edit them here."], entries.Select(entry => $"{entry.Label}: {entry.UnavailableReason}"));
        Assert.Equal(["Too many items to edit them here."], rows.Select(row => row.ReadOnlyReason));
    }

    [Fact]
    public void AWithheldEdit_RefusesTheOperationAndTheGesture_ButAMoveOnlyUnderAll()
    {
        // Arrange.
        var model = Withholding("model");
        var all = Withholding("all");
        var modelDiagram = Diagram(model, ("a", 0, "x"), ("b", 0, "x"));
        var allDiagram = Diagram(all, ("a", 0, "x"), ("b", 0, "x"));
        var bounds = new Dictionary<string, object?> { ["x"] = 0.0, ["y"] = 0.0 };

        // Act.
        var renamed = OperationInterpreter.Run(model, "rename", modelDiagram, modelDiagram.Nodes[0], DislIds.Fixed());
        var deleted = GestureConstraintEvaluator.Delete(model, modelDiagram.Nodes[0]);
        var movedUnderModel = GestureConstraintEvaluator.Placement(model, modelDiagram.Nodes[0], "move", bounds, bounds);
        var movedUnderAll = GestureConstraintEvaluator.Placement(all, allDiagram.Nodes[0], "move", bounds, bounds);

        // Assert.
        Assert.Equal("Too many items to edit them here.", renamed.Refusal);
        Assert.Equal(["Too many items to edit them here."], deleted.Select(refusal => refusal.Message));
        Assert.Empty(movedUnderModel);
        Assert.Equal([DislBudgets.Withheld], movedUnderAll.Select(refusal => refusal.Code));
    }

    [Fact]
    public void ATruncatingBudget_ShowsItsNotice_WithItsTextOrTheRuntimesSentence()
    {
        // Arrange.
        var specification = Loaded("""
            { "items": { "measure": { "count": ["Item"] }, "max": 1, "withhold": { "edits": "none" },
                "notice": { "text": { "cel": "'Showing ' + string(budget('items').shown) + ' of ' + string(budget('items').total) + ' items'" }, "severity": "warning", "position": "top" } },
              "plain": { "measure": { "count": ["Item"] }, "max": 1 },
              "silent": { "measure": { "count": ["Item"] }, "max": 1, "notice": false } }
            """);
        var diagram = Diagram(specification, ("a", 0, "x"), ("b", 0, "x"));

        // Act.
        var notices = CanvasChromeDerivation.Derive(diagram, diagram.Nodes, diagram.Env).Notices!;

        // Assert.
        Assert.Equal(
            ["budget:items warning top Showing 1 of 2 items", "budget:plain info  Showing 1 of 2. Edits are withheld while the view is partial."],
            notices.Select(notice => $"{notice.Id} {notice.Severity} {notice.Position} {notice.Text}"));
    }

    [Fact]
    public void AnExceededBudget_ReportsItsFinding()
    {
        // Arrange.
        var specification = Loaded("""
            { "items": { "measure": { "count": ["Item"] }, "max": 1, "finding": { "severity": "warning", "message": { "cel": "'There are ' + string(budget('items').total) + ' items.'" } } } }
            """);
        var small = Diagram(specification, ("a", 0, "x"));
        var large = Diagram(specification, ("a", 0, "x"), ("b", 0, "x"));

        // Act and assert.
        Assert.Empty(ConstraintEvaluator.Evaluate(specification, small));
        var finding = Assert.Single(ConstraintEvaluator.Evaluate(specification, large));
        Assert.Equal(("budget:items", "warning", "There are 2 items."), (finding.Code, finding.Severity, finding.Message));
    }
}
