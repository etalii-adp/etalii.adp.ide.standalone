using System.Globalization;
using System.Text.Json;
using EtAlii.Adp.Designer.TableModel;
using EtAlii.Adp.Specification.Fbl.Planning;
using Xunit;

namespace EtAlii.Adp.Designer.Knowledge.Tests;

/// <summary>
/// The table of gestures, in which order does not matter (code-structure T234): a gesture is found
/// by its whole kind, so <c>setCells</c> is never planned as <c>setCell</c>, and whether it is refused
/// for want of a view is said by its own entry - not by where it stands, as it was when a guard arm
/// halfway down one switch refused every case below it.
/// </summary>
public sealed class KnowledgeEditsGesturesTests
{
    /// <summary>
    /// The kinds the client's table library raises (<c>src/client/src/table/library/</c>). A search
    /// is the session's own and changes no file, so it is answered before a gesture is planned.
    /// </summary>
    private static readonly string[] _raised =
    [
        "setCell", "setCells", "addRow", "deleteRows", "moveRows",
        "addColumn", "renameColumn", "duplicateColumn", "deleteColumn", "setColumnType",
        "addRelation", "setColumnParent", "setRelationLimit", "setRelationOtherSide",
        "addOption", "renameOption", "recolourOption", "moveOption", "deleteOption",
        "addView", "renameView", "duplicateView", "deleteView", "moveView",
        "moveColumn", "resizeColumn", "hideColumn", "showColumn", "setColumnWrap", "setColumnCalculation", "freezeColumns",
        "addSort", "setSort", "removeSort", "moveSort",
        "groupBy", "setHideEmptyGroups", "toggleGroup", "toggleRow", "hideGroup", "showGroups", "showGroup", "moveGroup", "sortGroups",
        "addFilter", "addFilterGroup", "setFilter", "setFilterMatch", "removeFilter",
        "search",
    ];

    /// <summary>
    /// The gestures that were refused in a table without views before the table of gestures: the
    /// cases that stood below the switch's <c>_ when view is null</c> arm.
    /// </summary>
    private static readonly string[] _aboutTheViewShown =
    [
        "moveRows",
        "moveColumn", "resizeColumn", "hideColumn", "showColumn", "setColumnWrap", "setColumnCalculation", "freezeColumns",
        "addSort", "setSort", "removeSort", "moveSort",
        "groupBy", "setHideEmptyGroups", "toggleGroup", "toggleRow", "hideGroup", "showGroups", "showGroup", "moveGroup", "sortGroups",
        "addFilter", "addFilterGroup", "setFilter", "setFilterMatch", "removeFilter",
    ];

    /// <summary>Kinds the table does not have: a kind one of its kinds begins with, one that begins with one of them, and ones that are no kind at all.</summary>
    private static readonly string[] _unknown = ["search", "set", "setCellsAll", "setcell", "addFilter ", "bogus", ""];

    /// <summary>What a gesture may name, several ways: the shapes are planned for every kind, so each kind meets what it reads and what it refuses.</summary>
    private static readonly TableGesture[] _shapes =
    [
        new("", RowId: "r1", ColumnId: "p1", Values: ["Fresh"], TargetId: "r2", Index: 1, Settings: Settings(("rows", "r1,r2"), ("type", "number"), ("direction", "descending"), ("collapsed", "true"), ("hide", "true"), ("width", "100"), ("calculate", "countAll"), ("match", "any"), ("parent", "true"), ("colour", "red"), ("order", "descending"), ("wrap", "true"))),
        new("", RowId: "r2", ColumnId: "p2", ViewId: "v1", Values: ["o2"], TargetId: "o1", Index: 0, Settings: Settings(("type", "multipleSelection"), ("comparison", "isEmpty"), ("place", "after"), ("direction", "down"), ("colour", "purple"))),
        new("", RowId: "r1", ColumnId: "p4", ViewId: "v2", Values: ["o3", "o4"], TargetId: "o3", Index: 2, Settings: Settings(("group", "o3"), ("newOption", "Harbour"), ("colour", "default"))),
        new("", RowId: "r1", ColumnId: "p5", ViewId: "v1", Values: ["nh"], TargetId: "0", Index: 1, Settings: Settings(("comparison", "contains"), ("limit", "one"), ("twoWay", "true"), ("counterpart", "Cities"), ("target", "provinces.yaml"), ("otherSide", "delete"))),
        new("", RowId: "r2", ColumnId: "p3", ViewId: "v1", Values: ["42"], TargetId: "", Index: 1, Settings: Settings(("side", "left"), ("type", "selection"), ("calculate", "sum"), ("width", "x"))),
        new("", ColumnId: "p5", ViewId: "w1", Values: ["true"], TargetId: "1", Index: 5, Settings: Settings(("match", "all"), ("limit", "none"), ("otherSide", "keep"))),
        new("", RowId: "r1", ColumnId: "s1", Values: [], TargetId: "r2", Settings: Settings(("parent", "true"), ("place", "after"))),
    ];

    public static TheoryData<string> Orders => new("reversed", "seed 1", "seed 2", "seed 3", "seed 4", "seed 5", "seed 6", "seed 7", "seed 8");

    private static Dictionary<string, string> Settings(params (string Name, string Value)[] settings) => settings.ToDictionary(setting => setting.Name, setting => setting.Value);

    private static KnowledgeGesture[] Ordered(string order)
    {
        if (order == "reversed")
        {
            return [.. KnowledgeEdits.Gestures.Reverse()];
        }

        var random = new Random(int.Parse(order["seed ".Length..], CultureInfo.InvariantCulture));
        var shuffled = KnowledgeEdits.Gestures.ToArray();
        random.Shuffle(shuffled);
        return shuffled;
    }

    /// <summary>
    /// The shipped examples, each as it is and without its views, and one with what none of them has:
    /// a relation that can be the parent relation, a view with two sorts and a hidden group.
    /// </summary>
    private static IEnumerable<(string Name, KnowledgeTable Table)> Tables()
    {
        foreach (var name in new[] { "cities.yaml", "cities.json", "cities.xml", KnowledgeFiles.Related })
        {
            var table = KnowledgeBody.Read(KnowledgeFiles.ExampleBytes(name), name).Table;
            yield return (name, table);
            yield return ($"{name} without views", table with { Views = [] });
        }

        var cities = KnowledgeBody.Read(KnowledgeFiles.ExampleBytes("cities.yaml"), "cities.yaml").Table;
        var parent = new KnowledgeProperty("s1", "Part of", KnowledgeValueTypes.Relation.Name, false, [], KnowledgeRelations.Self, "one", "", false, false);
        var busy = cities.Views[0] with { Sorts = [.. cities.Views[0].Sorts, new KnowledgeSort("p1", false)], HiddenGroups = ["o1"] };
        yield return ("cities.yaml with more", cities with { Properties = [.. cities.Properties, parent], Views = [busy, .. cities.Views.Skip(1)] });
    }

    /// <summary>Every kind in every shape, and the shapes again with nothing about a relation known: the gestures planned in each table.</summary>
    private static IEnumerable<(TableGesture Gesture, bool BesideItsRule, bool WithFiles)> Gestures() =>
        from kind in KnowledgeEdits.Gestures.Select(gesture => gesture.Kind).Concat(_unknown)
        from shape in _shapes
        from variant in new[] { (BesideItsRule: false, WithFiles: true), (BesideItsRule: true, WithFiles: false) }
        select (shape with { Kind = kind }, variant.BesideItsRule, variant.WithFiles);

    private static KnowledgeSurroundings Files(KnowledgeTable table)
    {
        var related = KnowledgeBody.Read(KnowledgeFiles.ExampleBytes(KnowledgeFiles.Related), KnowledgeFiles.Related).Table;
        var self = new KnowledgeTarget(Path.GetFullPath("cities.yaml"), table);
        var target = new KnowledgeTarget(Path.GetFullPath(KnowledgeFiles.Related), related);
        return new KnowledgeSurroundings("cities.yaml", property => property.TargetFile == KnowledgeFiles.Related ? target : self, name => name == KnowledgeFiles.Related ? target : self);
    }

    /// <summary>A plan, every field of it in order, or what it threw.</summary>
    private static string Plan(KnowledgeGestureTable gestures, KnowledgeTable table, TableGesture gesture, bool besideItsRule, KnowledgeSurroundings? files)
    {
        var next = 0;
        try
        {
            var edit = KnowledgeEdits.Plan(gestures, table, "v2", gesture, () => FormattableString.Invariant($"id{++next}"), besideItsRule, files);
            return JsonSerializer.Serialize(new
            {
                edit.Refusal,
                edit.NewRowId,
                edit.NewViewId,
                Changes = edit.Changes.Select(Change),
                Others = edit.Others.Select(other => new { other.BodyPath, Changes = other.Changes.Select(Change) }),
            });
        }
        catch (Exception exception)
        {
            return $"{exception.GetType().Name}: {exception.Message}";
        }
    }

    /// <summary>A change with what kind of change it is, since two kinds can have the same fields.</summary>
    private static object Change(ModelChange change) => new { Kind = change.GetType().Name, Change = (object)change };

    [Theory]
    [MemberData(nameof(Orders))]
    public void Plan_IsTheSame_WhateverTheOrderOfTheGestureTable(string order)
    {
        // Arrange.
        var canonical = new KnowledgeGestureTable(KnowledgeEdits.Gestures);
        var reordered = new KnowledgeGestureTable(Ordered(order));

        foreach ((string name, KnowledgeTable table) in Tables())
        {
            var files = Files(table);
            foreach ((TableGesture gesture, bool besideItsRule, bool withFiles) in Gestures())
            {
                // Act.
                var expected = Plan(canonical, table, gesture, besideItsRule, withFiles ? files : null);
                var actual = Plan(reordered, table, gesture, besideItsRule, withFiles ? files : null);

                // Assert. The changes, the refusal and the ids, every field, in the same order.
                Assert.True(expected == actual, $"'{gesture.Kind}' in {name} is planned differently with the gestures in order {order}:\n{expected}\n{actual}");
            }
        }
    }

    [Fact]
    public void Plan_ChangesSomething_ForEveryGesture_InTheShapesTheOrderTestPlans()
    {
        // Arrange. A comparison of refusals alone would pass whichever entry planned a gesture.
        var tables = Tables().Select(table => (table.Table, Files: Files(table.Table))).ToList();

        // Act.
        var unplanned = KnowledgeEdits.Gestures
            .Select(entry => entry.Kind)
            .Where(kind => !tables.Any(table => Gestures().Any(planned => planned.Gesture.Kind == kind && Changes(planned, table.Table, table.Files))))
            .ToList();

        // Assert.
        Assert.True(unplanned.Count == 0, $"No shape changes anything with: {string.Join(", ", unplanned)}.");
    }

    private static bool Changes((TableGesture Gesture, bool BesideItsRule, bool WithFiles) planned, KnowledgeTable table, KnowledgeSurroundings files) =>
        KnowledgeEdits.Plan(table, "v2", planned.Gesture, () => "id", planned.BesideItsRule, planned.WithFiles ? files : null).Changes.Count > 0;

    [Fact]
    public void Gestures_AreTheKindsTheClientRaises_EachOnce()
    {
        // Act.
        var kinds = KnowledgeEdits.Gestures.Select(gesture => gesture.Kind).ToList();

        // Assert. Two entries of one kind would make one of them win, which is order.
        Assert.Equal(kinds.Count, kinds.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(_raised.Where(kind => kind != "search").Order(StringComparer.Ordinal), kinds.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void RequiresView_IsSaidOfTheGesturesAboutTheViewShown_AndOfNoOther()
    {
        // Act.
        var requiring = KnowledgeEdits.Gestures.Where(gesture => gesture.RequiresView).Select(gesture => gesture.Kind);

        // Assert.
        Assert.Equal(_aboutTheViewShown.Order(StringComparer.Ordinal), requiring.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AGestureAboutTheViewShown_AndAKindTheTableDoesNotKnow_AreRefusedForWantOfAView_InATableWithoutViews()
    {
        // Arrange.
        var table = KnowledgeBody.Read(KnowledgeFiles.ExampleBytes("cities.yaml"), "cities.yaml").Table with { Views = [] };

        // Act.
        var answers = _aboutTheViewShown.Concat(_unknown).ToDictionary(kind => kind, kind => KnowledgeEdits.Plan(table, "", _shapes[0] with { Kind = kind }, () => "id").Refusal);

        // Assert.
        Assert.All(answers, answer => Assert.True(answer.Value == KnowledgeEdits.ViewGone, $"'{answer.Key}' is answered '{answer.Value}'."));
    }
}
