using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// The toolbox, the context menus and the forms are typed at load (FR-079), and typing them moves no outcome: what
/// the derivations, a drop and a commit gave, refused or threw on when they read the JSON as they ran, they still do,
/// and loading reports none of it.
/// </summary>
public class TypedToolboxMenusFormsTests
{
    private const string Metamodel = """
        "metamodel": {
          "types": { "Thing": { "attributes": { "name": { "type": "string" } } } },
          "relations": { "Link": { "source": "Thing", "target": "Thing" } }
        }
        """;

    private static readonly IReadOnlyDictionary<string, object?> Origin = new Dictionary<string, object?> { ["x"] = 0.0, ["y"] = 0.0 };

    private static DislSpecification Load(string parts) => Specifications.Loaded(Specifications.With(Metamodel + "," + parts));

    private static (DislDiagram Diagram, DislElement Thing) Diagram(DislSpecification specification)
    {
        var diagram = new DislDiagram(specification);
        return (diagram, diagram.AddNode("Thing", "t", new Dictionary<string, object?> { ["name"] = "first" }));
    }

    [Fact]
    public void AMalformedToolbox_MenuOrForm_LoadsWithoutAnError()
    {
        string[] parts =
        [
            """ "toolbox": 5 """,
            """ "toolbox": { "tools": 3, "groups": [ { "id": "g", "tools": [ "shared" ] }, 7 ], "contextMenus": [ { "for": "diagram", "tools": [ 7 ] }, 6 ] } """,
            """ "forms": 3 """,
            """ "forms": { "bad": 4, "f": { "for": "Thing", "items": [ 8, { "kind": "tabs", "tabs": [ 3 ] }, { "attribute": "name", "parse": 5, "validate": [ 4 ], "widgetOptions": 3 } ] } } """,
        ];

        foreach (var part in parts) Assert.Empty(DislLoader.Load(Specifications.With(Metamodel + "," + part)).Errors);
    }

    [Fact]
    public void AToolboxThatIsNotAnObject_HasNoPalette_AndThrowsWhereItsMembersAreRead()
    {
        var specification = Load(""" "toolbox": 5 """);
        (DislDiagram diagram, DislElement thing) = Diagram(specification);

        Assert.Empty(ToolboxDerivation.Derive(specification, WireIdMap.None));
        Assert.Throws<InvalidOperationException>(() => ContextMenuDerivation.Derive(specification, DislMenuTarget.Canvas(diagram), new DislEnv(), WireIdMap.None));
        Assert.Throws<InvalidOperationException>(() => OperationInterpreter.Drop(specification, "any", diagram, Origin, DislIds.Fixed()));
        Assert.Throws<InvalidOperationException>(() => DislDrop.Resolve(specification, "any", diagram, null));
        Assert.Throws<InvalidOperationException>(() => OperationInterpreter.ConnectToNew(specification, "Link", diagram, thing, "target", Origin, DislIds.Fixed("n", "r")));
    }

    [Fact]
    public void AGroupThatIsNotAnObject_ThrowsOnlyOnceALookUpReachesIt()
    {
        var specification = Load(""" "toolbox": { "groups": [ { "id": "g", "tools": [ { "id": "first", "creates": "Thing" } ] }, 7 ] } """);
        (DislDiagram diagram, _) = Diagram(specification);

        Assert.Throws<InvalidOperationException>(() => ToolboxDerivation.Derive(specification, WireIdMap.None));
        Assert.True(OperationInterpreter.Drop(specification, "first", diagram, Origin, DislIds.Fixed("a")).WasApplied);
        Assert.Throws<InvalidOperationException>(() => OperationInterpreter.Drop(specification, "later", diagram, Origin, DislIds.Fixed("b")));
    }

    [Fact]
    public void ALibraryThatIsNotAnObject_ThrowsOnlyWhereAReferenceToItIsRead()
    {
        var specification = Load(""" "toolbox": { "tools": 3, "groups": [ { "id": "g", "tools": [ { "id": "first", "creates": "Thing" }, "shared" ] } ] } """);
        (DislDiagram diagram, DislElement thing) = Diagram(specification);

        Assert.Throws<InvalidOperationException>(() => ToolboxDerivation.Derive(specification, WireIdMap.None));
        Assert.True(OperationInterpreter.Drop(specification, "first", diagram, Origin, DislIds.Fixed("a")).WasApplied);
        Assert.Throws<InvalidOperationException>(() => OperationInterpreter.Drop(specification, "shared", diagram, Origin, DislIds.Fixed("b")));
        Assert.Equal("There is no tool 'none'.", OperationInterpreter.Drop(specification, "none", diagram, Origin, DislIds.Fixed("c")).Refusal);
        Assert.Equal("No tool creates a Link on empty canvas.", OperationInterpreter.ConnectToNew(specification, "Link", diagram, thing, "target", Origin, DislIds.Fixed("n", "r")).Refusal);
    }

    [Fact]
    public void ALibraryToolThatIsNotAnObject_CreatesNothing_AndThrowsWhereItsMembersAreRead()
    {
        var specification = Load("""
            "toolbox": { "tools": { "odd": 4 }, "groups": [ { "id": "g", "tools": [ "odd", "missing" ] } ] },
            "notation": { "edges": { "Link": { "connect": { "tool": "odd" } } } }
            """);
        (DislDiagram diagram, DislElement thing) = Diagram(specification);

        Assert.Throws<InvalidOperationException>(() => ToolboxDerivation.Derive(specification, WireIdMap.None));
        Assert.Equal("Tool 'odd' creates no node.", OperationInterpreter.Drop(specification, "odd", diagram, Origin, DislIds.Fixed("a")).Refusal);
        Assert.Equal("There is no tool 'missing'.", OperationInterpreter.Drop(specification, "missing", diagram, Origin, DislIds.Fixed("a")).Refusal);
        Assert.Throws<InvalidOperationException>(() => DislDrop.Resolve(specification, "odd", diagram, null));
        Assert.Throws<InvalidOperationException>(() => OperationInterpreter.ConnectToNew(specification, "Link", diagram, thing, "target", Origin, DislIds.Fixed("n", "r")));
    }

    [Fact]
    public void ADropTargetThatIsNotAnObject_NeverMatches_AndAConnectToNewEndIsReadAsWritten()
    {
        var specification = Load("""
            "toolbox": {
              "tools": { "edge": { "id": "own", "creates": "Link", "createTarget": { "type": "Thing", "initial": { "name": { "cel": "tool + '!'" } } }, "createSource": "ask" } },
              "groups": [ { "id": "g", "tools": [ { "id": "drop", "creates": "Thing", "drop": { "targets": [ 5, { "on": "canvas", "kind": "select" } ] } } ] } ]
            }
            """);
        (DislDiagram diagram, DislElement thing) = Diagram(specification);

        Assert.Equal("select", Assert.IsType<DislDropOutcome.Target>(DislDrop.Resolve(specification, "drop", diagram, null)).Kind);
        var created = Assert.IsType<DislChange.Create>(OperationInterpreter.ConnectToNew(specification, "Link", diagram, thing, "target", Origin, DislIds.Fixed("n", "r")).Changes[0]);
        Assert.Equal("own!", created.Attributes["name"]);
        Assert.Equal("A Link cannot end on empty canvas.", OperationInterpreter.ConnectToNew(specification, "Link", diagram, thing, "source", Origin, DislIds.Fixed("n", "r")).Refusal);
    }

    [Fact]
    public void AMenuSetOrEntryThatIsNotAnObject_ThrowsOnlyOnceTheDerivationReachesIt()
    {
        var reached = Load(""" "toolbox": { "contextMenus": [ { "for": "Thing", "when": "false", "tools": [ 7 ] }, 6 ] } """);
        var unreached = Load(""" "toolbox": { "contextMenus": [ { "for": "Thing", "when": "false", "tools": [ 7 ] }, { "for": "diagram", "tools": [ 7 ] } ] } """);
        (DislDiagram diagram, DislElement thing) = Diagram(unreached);

        Assert.Throws<InvalidOperationException>(() => ContextMenuDerivation.Derive(reached, DislMenuTarget.Element(Diagram(reached).Thing), new DislEnv(), WireIdMap.None));
        Assert.Empty(ContextMenuDerivation.Derive(unreached, DislMenuTarget.Element(thing), new DislEnv(), WireIdMap.None));
        Assert.Throws<InvalidOperationException>(() => ContextMenuDerivation.Derive(unreached, DislMenuTarget.Canvas(diagram), new DislEnv(), WireIdMap.None));
    }

    [Fact]
    public void AMenuEntryOnAnOperationThatIsNotAnObject_IsAnEntryWithoutAnOperation()
    {
        var specification = Load("""
            "toolbox": { "contextMenus": [ { "for": "diagram", "tools": [ { "operation": "odd" }, { "operation": "named", "icon": "own" } ] } ] },
            "behavior": { "operations": { "odd": 5, "named": { "label": "Named", "icon": "theirs", "shortcut": "Alt+N", "unavailable": [ "Not now." ] } } }
            """);
        (DislDiagram diagram, _) = Diagram(specification);

        var entries = Assert.Single(ContextMenuDerivation.Derive(specification, DislMenuTarget.Canvas(diagram), new DislEnv(), WireIdMap.None)).Entries;
        Assert.Equal(("operation", "", null, true), (entries[0].Label, entries[0].Icon, entries[0].Shortcut, entries[0].Available));
        Assert.Equal(("Named", "own", "Alt+N", false, "Not now."), (entries[1].Label, entries[1].Icon, entries[1].Shortcut?.Text, entries[1].Available, entries[1].UnavailableReason));
    }

    [Fact]
    public void FormsThatAreNotAnObject_HaveNoForm_AndThrowWhereAFormIsLookedUpByName()
    {
        var specification = Load(""" "forms": 3 """);
        (_, DislElement thing) = Diagram(specification);

        Assert.Empty(FormDerivation.Derive(specification, thing, new DislEnv(), WireIdMap.None));
        Assert.Equal("'name' is not a row of this element's form.", FormCommit.Row(specification, thing, "name", "x", new DislEnv()).Refusal);
        Assert.Throws<InvalidOperationException>(() => FormCommit.Request(specification, "f", thing, new DislEnv()));
        Assert.Throws<InvalidOperationException>(() => FormCommit.Refusal(specification, "f", "name", "x", thing, new DislEnv()));
    }

    [Fact]
    public void AFormOrItemThatIsNotAnObject_ThrowsOnlyOnceItIsReached()
    {
        var specification = Load(""" "forms": { "bad": 4, "f": { "for": "Thing", "items": [ { "attribute": "name", "label": "Name" }, 8 ] } } """);
        (_, DislElement thing) = Diagram(specification);

        Assert.Throws<InvalidOperationException>(() => FormDerivation.Derive(specification, thing, new DislEnv(), WireIdMap.None));
        Assert.Throws<InvalidOperationException>(() => FormCommit.Request(specification, "bad", thing, new DislEnv()));
        Assert.Throws<InvalidOperationException>(() => FormCommit.Refusal(specification, "bad", "name", "x", thing, new DislEnv()));
        Assert.Throws<InvalidOperationException>(() => FormCommit.Request(specification, "f", thing, new DislEnv()));
        Assert.Null(FormCommit.Refusal(specification, "f", "name", "x", thing, new DislEnv()));
        Assert.True(FormCommit.Row(specification, thing, "name", "x", new DislEnv()).WasApplied);
        Assert.Throws<InvalidOperationException>(() => FormCommit.Row(specification, thing, "other", "x", new DislEnv()));
    }

    [Fact]
    public void TabsWithoutAList_AreARowFoundByItsId_AndATabThatIsNotAnObjectThrowsOnceReached()
    {
        var specification = Load("""
            "forms": { "f": { "for": "Thing", "items": [
              { "kind": "tabs", "id": "t", "tabs": 5 },
              { "kind": "tabs", "tabs": [ { "title": "A", "items": [ { "attribute": "name" } ] }, 3 ] } ] } }
            """);
        (_, DislElement thing) = Diagram(specification);

        Assert.Equal("'t' cannot be edited here.", FormCommit.Row(specification, thing, "t", "x", new DislEnv()).Refusal);
        Assert.True(FormCommit.Row(specification, thing, "name", "x", new DislEnv()).WasApplied);
        Assert.Throws<InvalidOperationException>(() => FormCommit.Row(specification, thing, "other", "x", new DislEnv()));
        Assert.Throws<InvalidOperationException>(() => FormDerivation.Derive(specification, thing, new DislEnv(), WireIdMap.None));
    }

    [Fact]
    public void AParseOrValidationThatIsNotAnObject_ThrowsAtCommit()
    {
        var specification = Load("""
            "forms": { "f": { "for": "Thing", "items": [
              { "attribute": "name", "parse": 5 },
              { "id": "checked", "attribute": "name", "validate": [ { "severity": "warning", "rule": "false" }, 4 ] } ] } }
            """);
        (_, DislElement thing) = Diagram(specification);

        Assert.Throws<InvalidOperationException>(() => FormCommit.Row(specification, thing, "name", "x", new DislEnv()));
        Assert.Throws<InvalidOperationException>(() => FormCommit.Row(specification, thing, "checked", "x", new DislEnv()));
    }

    [Fact]
    public void ARowsValueAndCandidates_AreReadAsTheyWereWritten()
    {
        var specification = Load("""
            "forms": { "f": { "for": "Thing", "items": [
              { "kind": "computed", "id": "literal", "value": "self.name" },
              { "kind": "computed", "id": "number", "value": 3 },
              { "id": "enumNotAName", "attribute": "name", "options": { "enum": 1, "cel": "[{'value': 'v', 'label': 'L'}]" } },
              { "id": "computedOptions", "attribute": "name", "options": "[{'value': 'v', 'label': 'L'}]" },
              { "id": "marks", "attribute": "name", "options": 5, "widgetOptions": { "marks": [ { "value": 2.5 }, { "label": "Top" }, { } ] } },
              { "id": "unread", "attribute": "name", "options": [ "a" ], "widgetOptions": 3 } ] } }
            """);
        (_, DislElement thing) = Diagram(specification);

        var rows = FormDerivation.Derive(specification, thing, new DislEnv(), WireIdMap.None).ToDictionary(row => row.Id);
        Assert.Equal("self.name", rows["literal"].Value);
        Assert.Equal("", rows["number"].Value);
        Assert.Equal(["L"], rows["enumNotAName"].Candidates);
        Assert.Equal(["L"], rows["computedOptions"].Candidates);
        Assert.Equal(["2.5", "Top", ""], rows["marks"].Candidates);
        Assert.Equal(["a"], rows["unread"].Candidates);

        Assert.Equal("L", Written(FormCommit.Row(specification, thing, "enumNotAName", "L", new DislEnv())));
        Assert.Equal("v", Written(FormCommit.Row(specification, thing, "computedOptions", "L", new DislEnv())));
    }

    [Fact]
    public void MarksThatAreNotAnObject_ThrowOnlyWhereTheyAreRead()
    {
        var specification = Load(""" "forms": { "f": { "for": "Thing", "items": [ { "attribute": "name", "widgetOptions": { "marks": [ 1 ] } } ] } } """);
        (_, DislElement thing) = Diagram(specification);

        Assert.Throws<InvalidOperationException>(() => FormDerivation.Derive(specification, thing, new DislEnv(), WireIdMap.None));
        Assert.True(FormCommit.Row(specification, thing, "name", "x", new DislEnv()).WasApplied);
    }

    private static object? Written(DislTransaction transaction) => Assert.IsType<DislChange.Set>(Assert.Single(transaction.Changes)).Attributes["name"];
}
