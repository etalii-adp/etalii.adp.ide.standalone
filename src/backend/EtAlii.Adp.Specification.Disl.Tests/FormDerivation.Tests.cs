using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>The property rows of each bundled definition's inspector forms (DISL §7.5).</summary>
public class FormDerivationTests
{
    private static readonly DislEnv Readable = new(ReadOnly: false, Viewpoint: "trueTime");

    /// <summary>One line per row, as the parity transcripts write rows, the candidates inline.</summary>
    private static List<string> Lines(IReadOnlyList<DerivedRow> rows) =>
    [
        .. rows.Select(row =>
            $"{row.Id} | {row.Label} | {row.Value.Replace("\n", "\\n", StringComparison.Ordinal)} | {row.Widget ?? "-"} | {(row.ReadOnlyReason.Length > 0 ? $"read-only ({row.ReadOnlyReason})" : "editable")} | group {row.Group}{(row.Candidates is { } candidates ? $" | [{string.Join(", ", candidates)}]" : "")}"),
    ];

    private static List<string> HypeCycle(string id, DislEnv? env = null)
    {
        var diagram = Derivations.HypeCycleDiagram();
        return Lines(FormDerivation.Derive(Derivations.HypeCycle, Derivations.Element(diagram, id), env ?? Readable, Derivations.HypeCycleIds));
    }

    [Fact]
    public void ATrend_ShowsItsFieldsAndEachDrawnPhasesInfluences()
    {
        Assert.Equal(
        [
            "ghg.name | Name | Steam engine | - | editable | group Identity",
            "ghg.description | Description |  | textarea | editable | group Identity",
            "ghg.tags | Tags | energy, industry | tags | editable | group Identity | [energy, industry, transport, law]",
            "ghg.start | Start | 1800-01 | text | editable | group Time",
            "ghg.stop | Stop | 1850-01 | text | editable | group Time",
            "ghg.phases | Phases | All four | slider | editable | group Phases | [Peak, Peak and Trough, Peak, Trough and Slope, All four]",
            "ghg.peak-end | Peak ends | 1812-07 | text | editable | group Phases",
            "ghg.trough-end | Trough ends | 1825-01 | text | editable | group Phases",
            "ghg.slope-end | Slope ends | 1837-07 | text | editable | group Phases",
            "ghg.peak-influences | Influence | None | textarea | read-only (Draw, reattach or delete an influence on the canvas.) | group Peak",
            "ghg.peak-influenced-by | Influenced by | Watt's patent\\nRailways · Slope | textarea | read-only (Draw, reattach or delete an influence on the canvas.) | group Peak",
            "ghg.trough-influences | Influence | None | textarea | read-only (Draw, reattach or delete an influence on the canvas.) | group Trough",
            "ghg.trough-influenced-by | Influenced by | None | textarea | read-only (Draw, reattach or delete an influence on the canvas.) | group Trough",
            "ghg.slope-influences | Influence | None | textarea | read-only (Draw, reattach or delete an influence on the canvas.) | group Slope",
            "ghg.slope-influenced-by | Influenced by | None | textarea | read-only (Draw, reattach or delete an influence on the canvas.) | group Slope",
            "ghg.plateau-influences | Influence | Railways · Trough | textarea | read-only (Draw, reattach or delete an influence on the canvas.) | group Plateau",
            "ghg.plateau-influenced-by | Influenced by | None | textarea | read-only (Draw, reattach or delete an influence on the canvas.) | group Plateau",
        ], HypeCycle("steam"));
    }

    [Fact]
    public void AHiddenPhase_IsListedOnlyWhileAnInfluenceAttachesToIt()
    {
        var rows = HypeCycle("rail");

        Assert.Contains("ghg.peak-end | Peak ends | 1830-01 | text | editable | group Phases", rows);
        Assert.DoesNotContain(rows, row => row.StartsWith("ghg.trough-end", StringComparison.Ordinal));
        Assert.Contains("ghg.trough-influenced-by | Influenced by | Steam engine · Plateau | textarea | read-only (Draw, reattach or delete an influence on the canvas.) | group Trough", rows);
        Assert.Contains("ghg.slope-influences | Influence | Steam engine · Peak | textarea | read-only (Draw, reattach or delete an influence on the canvas.) | group Slope (hidden)", rows);
        Assert.DoesNotContain(rows, row => row.Contains("group Plateau", StringComparison.Ordinal));
    }

    [Fact]
    public void ANote_ShowsItsTextAndItsSize()
    {
        Assert.Equal(
        [
            "ghg.text | Text | A remark | textarea | editable | group Identity",
            "ghg.size | Size | 160 x 64.5 | - | editable | group Identity",
        ], HypeCycle("remark"));
    }

    [Fact]
    public void AnInfluence_ShowsItsEnds()
    {
        Assert.Equal(
        [
            "ghg.description | Description |  | textarea | editable | group Identity",
            "ghg.from | From | Steam engine · Plateau | - | read-only (Where it is attached; drag the end on the canvas to move it.) | group Ends",
            "ghg.to | To | Railways · Trough | - | read-only (Where it is attached; drag the end on the canvas to move it.) | group Ends",
            "ghg.from-attachment | From attachment | plateau/top/0.5 | - | editable | group Ends",
            "ghg.to-attachment | To attachment | trough/bottom/0.25 | - | editable | group Ends",
        ], HypeCycle("i1"));
    }

    [Fact]
    public void ReadOnly_GivesEveryRowWithoutAReasonOfItsOwnTheStandardOne()
    {
        var rows = HypeCycle("i1", Readable with { ReadOnly = true });

        Assert.Equal("ghg.description | Description |  | textarea | read-only (The graph could not be read, so it cannot be edited.) | group Identity", rows[0]);
        Assert.Equal("ghg.from | From | Steam engine · Plateau | - | read-only (Where it is attached; drag the end on the canvas to move it.) | group Ends", rows[1]);
    }

    [Fact]
    public void ABehaviorNode_ShowsKindLabelNotesAndPlace_AndARetryItsAttempts()
    {
        var diagram = Derivations.BehaviorDiagram();

        var check = Lines(FormDerivation.Derive(Derivations.BehaviorModel, Derivations.Element(diagram, "1.1"), new DislEnv(), Derivations.BehaviorModelIds));
        var retry = Lines(FormDerivation.Derive(Derivations.BehaviorModel, Derivations.Element(diagram, "1.2"), new DislEnv(), Derivations.BehaviorModelIds));

        Assert.Equal(
        [
            "abm.kind | Kind | Check | - | editable | group Node | [Do in order, Try in order, Do together, Retry, Repeat until, Only while, Ask approval before, Check, Do, Ask the user, Delegate]",
            "abm.label | Label | Is it known? | - | editable | group Node",
            "abm.notes | Notes |  | textarea | editable | group Node",
            "abm.place | Place | 1.1 | - | read-only (A node's place follows from where it sits in the tree.) | group Node",
        ], check);
        Assert.Equal("abm.attempts | Attempts | 3 | - | editable | group Node", retry[2]);
    }

    [Fact]
    public void ARetypeItem_OffersTheTypesItsOptionsGive_NamedByItsOptionLabel()
    {
        // Arrange.
        var specification = Specifications.Loaded(Specifications.With("""
            "metamodel": { "types": { "Box": { "attributes": { "name": { "type": "string" } } }, "Crate": { "attributes": { "name": { "type": "string" } } } } },
            "forms": { "thing": { "for": ["Box", "Crate"], "items": [
              { "kind": "type", "label": "Type", "display": { "cel": "self.type" },
                "options": "['Box', 'Crate'].filter(t, t != self.type || self.name == 'keep')", "optionLabel": "item + ' (' + self.name + ')'" },
              { "kind": "computed", "label": "Plain", "value": { "cel": "self.type" } } ] } }
            """));
        var diagram = new DislDiagram(specification);
        var box = diagram.AddNode("Box", "a", new Dictionary<string, object?> { ["name"] = "A" });
        var kept = diagram.AddNode("Crate", "b", new Dictionary<string, object?> { ["name"] = "keep" });

        // Act.
        var rows = FormDerivation.Derive(specification, box, new DislEnv(), WireIdMap.None);
        var keptRows = FormDerivation.Derive(specification, kept, new DislEnv(), WireIdMap.None);

        // Assert.
        Assert.Equal(("Type", "Box", true, "Crate (A)"), (rows[0].Label, rows[0].Value, rows[0].Retypes, string.Join(", ", rows[0].Candidates!)));
        Assert.Equal("Box (keep), Crate (keep)", string.Join(", ", keptRows[0].Candidates!));
        Assert.Equal((false, null), (rows[1].Retypes, rows[1].Candidates));
    }

    [Fact]
    public void AnUnsetAttribute_ShowsItsDefault_OrNothing()
    {
        var specification = Specifications.Loaded(Specifications.With("""
            "metamodel": { "types": { "Thing": { "attributes": {
              "name": { "type": "string" }, "size": { "type": "int", "default": 3 }, "when": { "type": "yearMonth" }, "weight": { "type": "number" } } } } },
            "forms": { "thing": { "for": "Thing", "items": [
              { "attribute": "name" }, { "attribute": "size", "label": "Size" }, { "attribute": "when" }, { "attribute": "weight" } ] } }
            """));
        var diagram = new DislDiagram(specification);
        var empty = diagram.AddNode("Thing", "a");
        var full = diagram.AddNode("Thing", "b", new Dictionary<string, object?> { ["name"] = "B", ["size"] = 7L, ["when"] = -1L, ["weight"] = 2.5 });

        Assert.Equal(["name | name |  | - | editable | group ", "size | Size | 3 | - | editable | group ", "when | when |  | - | editable | group ", "weight | weight |  | - | editable | group "],
            Lines(FormDerivation.Derive(specification, empty, new DislEnv(), WireIdMap.None)));
        Assert.Equal(["name | name | B | - | editable | group ", "size | Size | 7 | - | editable | group ", "when | when | -0001-12 | - | editable | group ", "weight | weight | 2.5 | - | editable | group "],
            Lines(FormDerivation.Derive(specification, full, new DislEnv(), WireIdMap.None)));
    }

    [Fact]
    public void OptionsGivenAsValueAndLabelMaps_OfferTheirLabels_ElseTheirValues()
    {
        // Arrange: options are values or {value, label, icon, doc} maps (DISL 7.5), from CEL or written out.
        var specification = Specifications.Loaded(Specifications.With("""
            "metamodel": { "types": { "Thing": { "attributes": { "size": { "type": "int" }, "flavour": { "type": "string" } } } } },
            "forms": { "thing": { "for": "Thing", "items": [
              { "attribute": "size", "widget": "select", "options": { "cel": "[{'value': 1, 'label': 'One'}, {'value': 2}, 3]" } },
              { "attribute": "flavour", "widget": "select", "options": [ { "value": "a", "label": "Alpha" }, { "value": "b" }, "c" ] } ] } }
            """));
        var thing = new DislDiagram(specification).AddNode("Thing", "a");

        // Act.
        var rows = FormDerivation.Derive(specification, thing, new DislEnv(), WireIdMap.None);

        // Assert.
        Assert.Equal(["One", "2", "3"], rows[0].Candidates);
        Assert.Equal(["Alpha", "b", "c"], rows[1].Candidates);
    }

    /// <summary>The diagram's own form (§7.5, <c>for: "diagram"</c>): its rows read the attributes of <c>metamodel.diagram</c>, with <c>self</c> the diagram.</summary>
    private static readonly DislSpecification DiagramForm = Specifications.Loaded(Specifications.With("""
        "metamodel": {
          "diagram": { "attributes": {
            "title": { "type": "string" },
            "count": { "type": "int", "default": 3 },
            "heading": { "type": "string", "derived": "self.title + ' (' + string(self.nodes.size()) + ')'" } } },
          "types": { "Box": { "attributes": { "title": { "type": "string" } } } } },
        "forms": {
          "box": { "for": "Box", "items": [ { "attribute": "title", "label": "Box title" } ] },
          "header": { "for": "diagram", "items": [ { "kind": "section", "label": "Query", "items": [
            { "attribute": "title", "label": "Title" },
            { "attribute": "count", "label": "Count" },
            { "attribute": "heading", "id": "head", "label": "Heading", "widget": "readonly", "readOnlyReasons": ["The heading is read from the title."] },
            { "kind": "computed", "label": "Boxes", "value": { "cel": "string(diagram.nodes.size()) + ' of them'" } } ] } ] } }
        """));

    [Fact]
    public void TheDiagramsOwnForm_GivesItsRows_FromTheDiagramsAttributes()
    {
        // Arrange.
        var diagram = new DislDiagram(DiagramForm, new Dictionary<string, object?> { ["title"] = "Query" });
        diagram.AddNode("Box", "b", new Dictionary<string, object?> { ["title"] = "Inner" });

        // Act.
        var rows = FormDerivation.Derive(DiagramForm, diagram, new DislEnv(), WireIdMap.None);

        // Assert.
        Assert.Equal(
        [
            "title | Title | Query | - | editable | group Query",
            "count | Count | 3 | - | editable | group Query",
            "head | Heading | Query (1) | readonly | read-only (The heading is read from the title.) | group Query",
            "Boxes | Boxes | 1 of them | - | editable | group Query",
        ], Lines(rows));
    }

    [Fact]
    public void ADiagramWithoutAFormForIt_HasNoRows_AndAnElementsFormIsNotTheDiagrams()
    {
        // Arrange.
        var specification = Specifications.Loaded(Specifications.With("""
            "metamodel": { "diagram": { "attributes": { "title": { "type": "string" } } }, "types": { "Box": { "attributes": { "title": { "type": "string" } } } } },
            "forms": { "box": { "for": "Box", "items": [ { "attribute": "title" } ] } }
            """));
        var diagram = new DislDiagram(specification, new Dictionary<string, object?> { ["title"] = "Query" });
        var box = diagram.AddNode("Box", "b", new Dictionary<string, object?> { ["title"] = "Inner" });

        // Act and assert.
        Assert.Empty(FormDerivation.Derive(specification, diagram, new DislEnv(), WireIdMap.None));
        Assert.Equal("Inner", Assert.Single(FormDerivation.Derive(specification, box, new DislEnv(), WireIdMap.None)).Value);
    }

    /// <summary>
    /// A property row keyed by its form's type (<c>"&lt;Type&gt;/&lt;key&gt;"</c> in the <c>x-</c> block's <c>properties</c>):
    /// two forms whose items share a key map to two wire ids, a subtype takes its ancestor's, the diagram's form is
    /// keyed by <c>diagram</c>, and a key with no type-keyed entry keeps the plain one.
    /// </summary>
    private static readonly DislSpecification TypeKeyedRows = Specifications.Loaded(Specifications.With("""
        "metamodel": {
          "diagram": { "attributes": { "label": { "type": "string" } } },
          "types": {
            "Region": { "attributes": { "label": { "type": "string" } } },
            "Frame": { "extends": "Region" },
            "Note": { "attributes": { "label": { "type": "string" } } } } },
        "forms": {
          "region": { "for": ["Region"], "items": [ { "attribute": "label", "id": "label" } ] },
          "note": { "for": ["Note"], "items": [ { "attribute": "label", "id": "label" } ] },
          "diagram": { "for": "diagram", "items": [ { "attribute": "label", "id": "label" } ] } },
        "x-test": { "properties": { "Region/label": "test.region.label", "diagram/label": "test.diagram.label", "label": "test.label" } }
        """));

    [Fact]
    public void ARowsId_IsKeyedByItsFormsType_BeforeItsKeyAlone()
    {
        // Arrange.
        var ids = WireIdMap.Of(TypeKeyedRows, "x-test");
        var diagram = new DislDiagram(TypeKeyedRows, new Dictionary<string, object?> { ["label"] = "d" });
        var region = diagram.AddNode("Region", "r", new Dictionary<string, object?> { ["label"] = "r" });
        var frame = diagram.AddNode("Frame", "f", new Dictionary<string, object?> { ["label"] = "f" });
        var note = diagram.AddNode("Note", "n", new Dictionary<string, object?> { ["label"] = "n" });

        // Act and assert.
        Assert.Equal("test.region.label", Assert.Single(FormDerivation.Derive(TypeKeyedRows, region, new DislEnv(), ids)).Id);
        Assert.Equal("test.region.label", Assert.Single(FormDerivation.Derive(TypeKeyedRows, frame, new DislEnv(), ids)).Id);
        Assert.Equal("test.label", Assert.Single(FormDerivation.Derive(TypeKeyedRows, note, new DislEnv(), ids)).Id);
        Assert.Equal("test.diagram.label", Assert.Single(FormDerivation.Derive(TypeKeyedRows, diagram, new DislEnv(), ids)).Id);
    }

    private static readonly DislSpecification AbsentRows = Specifications.Loaded(Specifications.With("""
        "disl": "0.3",
        "metamodel": { "types": { "Mark": { "attributes": {
          "offset": { "type": "number" },
          "note": { "type": "string" } } } } },
        "forms": { "mark": { "for": ["Mark"], "items": [
          { "attribute": "offset", "label": "Offset", "showAbsent": false },
          { "attribute": "note", "label": "Note" } ] } }
        """));

    [Fact]
    public void AFieldThatShowsNoAbsentValue_HasNoRowWhileItsValueIsAbsent_AndOneOnceItIsStored()
    {
        // Arrange.
        var diagram = new DislDiagram(AbsentRows);
        var bare = diagram.AddNode("Mark", "bare", new Dictionary<string, object?>());
        var placed = diagram.AddNode("Mark", "placed", new Dictionary<string, object?> { ["offset"] = 2.0 });

        // Act.
        var bareRows = FormDerivation.Derive(AbsentRows, bare, new DislEnv(), WireIdMap.None);
        var placedRows = FormDerivation.Derive(AbsentRows, placed, new DislEnv(), WireIdMap.None);

        // Assert: showAbsent false hides the row of an absent value only (DISL 7.5); a field without it is shown empty.
        Assert.Equal(["note"], bareRows.Select(row => row.Id));
        Assert.Equal(["offset", "note"], placedRows.Select(row => row.Id));
    }
}
