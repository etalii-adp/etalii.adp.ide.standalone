using EtAlii.Adp.Specification.Fbl;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// The model a reading builds (DISL §4.11, §11.5, §14.2): binding types mapped by the type map, values
/// typed by their attributes, the derived ids and the derived relations computed.
/// </summary>
public class DislModelBuilderTests
{
    private static readonly DislSpecification Mapped = Specifications.Loaded(Specifications.With("""
        "metamodel": {
          "diagram": { "attributes": { "scale": { "type": "Scale", "default": "small" } } },
          "enums": { "Scale": { "values": { "small": { "value": "S" }, "large": { "value": "L" } } } },
          "types": {
            "Box": { "attributes": {
              "name": { "type": "string" }, "count": { "type": "int", "default": 5 }, "weight": { "type": "number" },
              "open": { "type": "bool" }, "when": { "type": "yearMonth" }, "size": { "type": "Scale" },
              "tags": { "type": "int", "many": true } },
              "children": { "allowed": ["Box"] } } },
          "relations": { "Arrow": { "source": "Box", "target": "Box", "attributes": { "label": { "type": "string" } } } } },
        "persistence": { "x-persistence.typeMap": {
          "Head": { "as": "header" },
          "Setting": { "as": "diagram", "attributes": { "value": "scale" } },
          "Item": { "as": "Box", "attributes": { "title": "name" }, "hostAttributes": ["raw"] },
          "Link": { "as": "Arrow", "attributes": { "from": "source", "to": "target" } },
          "Junk": { "as": "unreadable" } } }
        """));

    private static FblElement Element(string id, string type, IReadOnlyDictionary<string, object?> attributes, string? parent = null, bool relation = false, string? source = null, string? target = null, bool stored = true) =>
        new(id, stored, type, type.ToLowerInvariant(), relation, attributes, parent, parent is null ? null : "children", source, target, new Span(0, 0), 1);

    [Fact]
    public void TheTypeMap_PlacesEveryBindingType()
    {
        // Arrange.
        var reading = new FblModel(
        [
            Element("head@1", "Head", new Dictionary<string, object?> { ["version"] = 1L }, stored: false),
            Element("setting@2", "Setting", new Dictionary<string, object?> { ["value"] = "L" }, stored: false),
            Element("a", "Item", new Dictionary<string, object?> { ["title"] = "A", ["raw"] = "kept", ["colour"] = "red" }),
            Element("b", "Item", new Dictionary<string, object?> { ["title"] = "B" }, parent: "a"),
            Element("l1", "Link", new Dictionary<string, object?> { ["from"] = "a", ["to"] = "b", ["label"] = "go" }),
            Element("l2", "Link", new Dictionary<string, object?> { ["from"] = "a", ["to"] = "nowhere" }),
            Element("l3", "Arrow", new Dictionary<string, object?>(), relation: true, source: "b", target: "a"),
            Element("junk@9", "Junk", new Dictionary<string, object?>(), stored: false),
            Element("x", "Unknown", new Dictionary<string, object?>()),
        ], [new Finding(FindingCodes.DuplicateId, FindingSeverity.Warning, "From the reader.", null)], false);

        // Act.
        var model = DislModelBuilder.From(reading, Mapped);

        // Assert.
        var diagram = model.Diagram;
        Assert.Equal(["head@1"], model.Header.Select(element => element.Id));
        Assert.Equal(["junk@9", "x"], model.Unreadable.Select(element => element.Id));
        Assert.Equal("large", diagram.ValueOf("scale"));
        Assert.Equal(["a", "b"], diagram.Nodes.Select(node => node.Id));
        Assert.Equal("A", diagram.Nodes[0].ValueOf("name"));
        Assert.Equal(new Dictionary<string, object?> { ["raw"] = "kept", ["colour"] = "red" }, diagram.Nodes[0].HostAttributes);
        Assert.Same(diagram.Nodes[0], diagram.Nodes[1].Parent);
        Assert.Equal("children", diagram.Nodes[1].Slot);
        Assert.Equal(["l1", "l2", "l3"], diagram.Relations.Select(relation => relation.Id));
        Assert.Equal(("a", "b", "go"), (diagram.Relations[0].Source!.Id, diagram.Relations[0].Target!.Id, diagram.Relations[0].ValueOf("label")));
        Assert.Equal(("a", "nowhere"), (diagram.Relations[1].SourceId, diagram.Relations[1].TargetId));
        Assert.Null(diagram.Relations[1].Target);
        Assert.Equal(("b", "a"), (diagram.Relations[2].Source!.Id, diagram.Relations[2].Target!.Id));
        Assert.Equal(["std.duplicateId"], model.Findings.Select(finding => finding.Code));
        Assert.False(model.IsUnreadable);
    }

    [Fact]
    public void AChildReadBeforeItsParent_IsAddedAfterIt()
    {
        var reading = new FblModel(
        [
            Element("b", "Item", new Dictionary<string, object?>(), parent: "a"),
            Element("a", "Item", new Dictionary<string, object?>()),
        ], [], false);

        var diagram = DislModelBuilder.From(reading, Mapped).Diagram;

        Assert.Equal(["a", "b"], diagram.Nodes.Select(node => node.Id));
        Assert.Same(diagram.Nodes[0], diagram.Nodes[1].Parent);
    }

    [Fact]
    public void AnUnreadableBody_GivesAnEmptyModel()
    {
        var model = DislModelBuilder.From(new FblModel([], [new Finding(FindingCodes.Unparseable, FindingSeverity.Error, "Not YAML.", null)], true), Mapped);

        Assert.True(model.IsUnreadable);
        Assert.Empty(model.Diagram.Elements);
        Assert.Equal(["std.unparseable"], model.Findings.Select(finding => finding.Code));
    }

    /// <summary>Each read value against what the attribute stores: the value in its CEL type, or nothing when it does not fit.</summary>
    [Theory]
    [InlineData("count", "7", 7L)]
    [InlineData("count", 7L, 7L)]
    [InlineData("count", "seven", null)]
    [InlineData("count", "7.5", null)]
    [InlineData("weight", "2.5", 2.5)]
    [InlineData("weight", 3L, 3.0)]
    [InlineData("weight", "heavy", null)]
    [InlineData("open", true, true)]
    [InlineData("open", "true", true)]
    [InlineData("open", "yes", null)]
    [InlineData("when", "2026-09", 2026L * 12 + 8)]
    [InlineData("when", "-3200-01", -3200L * 12)]
    [InlineData("when", "2026-00", null)]
    [InlineData("when", "2026-13", null)]
    [InlineData("when", "September", null)]
    [InlineData("size", "S", "small")]
    [InlineData("size", "huge", "huge")]
    [InlineData("name", 42L, "42")]
    [InlineData("name", 2.5, "2.5")]
    [InlineData("name", false, "false")]
    [InlineData("name", null, "")]
    public void AReadValue_IsTypedByItsAttribute(string attribute, object? read, object? expected)
    {
        var reading = new FblModel([Element("a", "Item", new Dictionary<string, object?> { [attribute] = read })], [], false);

        var box = Assert.Single(DislModelBuilder.From(reading, Mapped).Diagram.Nodes);

        Assert.Equal(expected, box.Attributes.GetValueOrDefault(attribute));
        Assert.Equal(expected is not null, box.Attributes.ContainsKey(attribute));
    }

    [Fact]
    public void AManyValue_KeepsTheItemsThatFit()
    {
        var reading = new FblModel(
        [
            Element("a", "Item", new Dictionary<string, object?> { ["tags"] = new List<object?> { 1L, "2", "three", 4L } }),
            Element("b", "Item", new Dictionary<string, object?> { ["tags"] = "1" }),
        ], [], false);

        var nodes = DislModelBuilder.From(reading, Mapped).Diagram.Nodes;

        Assert.Equal(new List<object?> { 1L, 2L, 4L }, nodes[0].Attributes["tags"]);
        Assert.False(nodes[1].Attributes.ContainsKey("tags"));
    }

    // ---- derived ids and derived relations -----------------------------------------------------

    private static readonly DislSpecification Derived = Specifications.Loaded(Specifications.With("""
        "metamodel": {
          "types": {
            "Thing": { "attributes": { "name": { "type": "string" } }, "children": { "allowed": ["Thing"] } },
            "Other": { "attributes": { "name": { "type": "string" } } } },
          "relations": {
            "Next": { "source": "Thing", "target": "Thing",
              "derived": { "from": "diagram.nodesOfType('Thing').filter(n, n.name != 'last') + [diagram.nodes[0]]", "source": "item",
                           "target": "diagram.nodes.filter(n, n.name == 'last')[0]" } },
            "Up": { "source": "Thing", "target": "Thing",
              "derived": { "from": "diagram.nodes", "id": "'up:' + item.id", "source": "item", "target": "item.parent" } } } },
        "persistence": { "ids": { "strategy": "derived", "expression": "self.name == 'fail' ? self.missing : self.name" } }
        """));

    [Fact]
    public void TheDerivedIds_AreComputedAndChecked()
    {
        var diagram = new DislDiagram(Derived);
        diagram.AddNode("Thing", "", new Dictionary<string, object?> { ["name"] = "a" });
        diagram.AddNode("Thing", "", new Dictionary<string, object?> { ["name"] = "a" });
        diagram.AddNode("Thing", "stored", new Dictionary<string, object?> { ["name"] = "fail" });
        diagram.AddNode("Thing", "", new Dictionary<string, object?> { ["name"] = "last" });

        var findings = DislModelBuilder.Complete(diagram);

        Assert.Equal(["a", "a", "stored", "last"], diagram.Nodes.Select(node => node.Id));
        Assert.Equal(
            ["std.duplicateId: The id 'a' is computed for two elements; the first keeps it.",
             "std.missingId: The id of this Thing could not be computed: No such field: 'missing'."],
            findings.Take(2).Select(finding => finding.ToString()));
    }

    [Fact]
    public void ADerivedRelation_HasItsDefaultIdWithRepeatsNumbered_AndDropsWhatItsEndsDoNotAllow()
    {
        var diagram = new DislDiagram(Derived);
        var a = diagram.AddNode("Thing", "", new Dictionary<string, object?> { ["name"] = "a" });
        diagram.AddNode("Thing", "", new Dictionary<string, object?> { ["name"] = "b" }, a);
        diagram.AddNode("Thing", "", new Dictionary<string, object?> { ["name"] = "last" });
        diagram.AddNode("Other", "", new Dictionary<string, object?> { ["name"] = "o" });

        var findings = DislModelBuilder.Complete(diagram);

        Assert.Equal(["Next:a->last", "Next:b->last", "Next:a->last#2", "up:b"], diagram.Relations.Select(relation => relation.Id));
        Assert.Same(a, diagram.Relations[3].Target);
        Assert.Equal(
            ["std.derivedEnds: 1 item(s) of Up were dropped: their source is not one its declaration allows.",
             "std.derivedEnds: 2 item(s) of Up were dropped: their target is not one its declaration allows."],
            findings.Select(finding => finding.ToString()).Order(StringComparer.Ordinal));
        Assert.All(diagram.Relations, relation => Assert.True(relation.IsDerived && relation.Sources.Count == 1 && !relation.IdIsStored));
    }

    [Fact]
    public void ADerivedIdTakenAlready_IsNotDrawn()
    {
        var specification = Specifications.Loaded(Specifications.With("""
            "metamodel": {
              "types": { "Thing": { "attributes": { "name": { "type": "string" } } } },
              "relations": { "Self": { "source": "Thing", "target": "Thing",
                "derived": { "from": "diagram.nodes", "id": "'t'", "source": "item", "target": "item" } } } }
            """));
        var diagram = new DislDiagram(specification);
        diagram.AddNode("Thing", "t");
        diagram.AddNode("Thing", "u");

        var findings = DislModelBuilder.Complete(diagram);

        Assert.Empty(diagram.Relations);
        Assert.Equal(DislModelBuilder.DerivedId, Assert.Single(findings.Select(finding => finding.Code).Distinct()));
        Assert.Equal(2, findings.Count);
    }

    [Fact]
    public void AFailingFrom_DerivesNothing()
    {
        var specification = Specifications.Loaded(Specifications.With("""
            "metamodel": {
              "types": { "Thing": { "attributes": { "name": { "type": "string" } } } },
              "relations": { "Self": { "source": "Thing", "target": "Thing",
                "derived": { "from": "diagram.nodes.map(n, 1 / 0)", "source": "item", "target": "item" } } } }
            """));
        var diagram = new DislDiagram(specification);
        diagram.AddNode("Thing", "t");

        var finding = Assert.Single(DislModelBuilder.Complete(diagram));

        Assert.Equal(DislModelBuilder.DerivedFailed, finding.Code);
        Assert.Empty(diagram.Relations);
    }
}
