using EtAlii.Adp.Specification.Fbl;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// A derived node type (DISL §4.11.2): one node per item of its <c>from</c>, or per distinct <c>key</c> with
/// <c>group</c> bound to the items that share it, computed in <c>metamodel.types</c> order before the derived
/// relations (§4.11.5), with its <c>id</c>, <c>attributes</c>, <c>parent</c> and <c>sources</c>; its <c>reason</c>
/// and <c>edits</c> readable by a host.
/// </summary>
public class DerivedNodeTypesTests
{
    /// <summary>The example of §4.11.2, with <c>RDF_TYPE</c> written out as the IRI it stands for.</summary>
    private static readonly DislSpecification Example = Specifications.Loaded(Specifications.With("""
        "disl": "0.3",
        "metamodel": {
          "enums": { "TermKind": { "values": { "iri": {}, "literal": {} } } },
          "types": {
            "Triple": {
              "doc": "One triple of the file, as the format binding reads it. Never drawn.",
              "attributes": { "s": { "type": "string" }, "p": { "type": "uri" }, "o": { "type": "string" }, "oKind": { "type": "TermKind" } }
            },
            "Term": { "abstract": true },
            "Resource": {
              "extends": "Term",
              "attributes": { "iri": { "type": "uri" }, "types": { "type": "uri", "many": true } },
              "derived": {
                "from": "diagram.nodesOfType('Triple').map(t, t.oKind == 'iri' && t.p != 'http://www.w3.org/1999/02/22-rdf-syntax-ns#type' ? [[t.s, t], [t.o, t]] : [[t.s, t]]).flatten()",
                "key": "item[0]",
                "id": "'res:' + item[0]",
                "attributes": {
                  "iri":   "item[0]",
                  "types": "group.map(g, g[1]).filter(t, t.s == item[0] && t.p == 'http://www.w3.org/1999/02/22-rdf-syntax-ns#type' && t.oKind == 'iri').map(t, t.o)"
                },
                "sources": "group.map(g, g[1]).distinct()",
                "reason": "Resources are read from the triples; add, rename or remove them through the context menu.",
                "edits": { "delete": "removeResource", "rename": "renameResource" }
              }
            }
          }
        },
        "behavior": { "operations": {
          "removeResource": { "for": ["Resource"], "actions": [ { "delete": "self.sources" } ] },
          "renameResource": { "for": ["Resource"], "actions": [] } } }
        """));

    private const string Type = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";

    private static FblElement Triple(string id, string s, string p, string o, string oKind, int line) =>
        new(id, true, "Triple", "triple", false, new Dictionary<string, object?> { ["s"] = s, ["p"] = p, ["o"] = o, ["oKind"] = oKind }, null, null, null, null, new Span(0, 0), line);

    private static DislModel ExampleModel() => DislModelBuilder.From(new FblModel(
    [
        Triple("t1", "a", Type, "Person", "iri", 1),
        Triple("t2", "a", "knows", "b", "iri", 2),
        Triple("t3", "b", "name", "Bob", "literal", 3),
        Triple("t4", "a", Type, "Agent", "iri", 4),
    ], [], false), Example);

    [Fact]
    public void TheExampleOf4112_DrawsOneCardPerIri_InTheOrderTheTriplesFirstNameIt()
    {
        // Act.
        var model = ExampleModel();

        // Assert.
        var resources = model.Diagram.NodesOfType("Resource");
        Assert.Equal(["res:a", "res:b"], resources.Select(resource => resource.Id));
        Assert.Equal(["Person", "Agent"], (IEnumerable<object?>)resources[0].ValueOf("types")!);
        Assert.Empty((IEnumerable<object?>)resources[1].ValueOf("types")!);
        Assert.Equal(["t1", "t2", "t4"], resources[0].Sources.Select(source => source.Id));
        Assert.Equal(["t2", "t3"], resources[1].Sources.Select(source => source.Id));
        Assert.All(resources, resource => Assert.True(resource.IsDerived));
        Assert.Empty(model.Findings);
    }

    [Fact]
    public void ATypeUsedOnlyAsAType_IsNoCard()
    {
        // Act.
        var model = ExampleModel();

        // Assert.
        Assert.NotNull(model.Diagram.ElementById("res:a"));
        Assert.Null(model.Diagram.ElementById("res:Person"));
    }

    [Fact]
    public void TheReasonAndTheEditsOfADerivedType_AreReadable()
    {
        // Act.
        var resource = Example.Metamodel.TypeOf("Resource")!;

        // Assert.
        Assert.Equal("Resources are read from the triples; add, rename or remove them through the context menu.", resource.Reason);
        Assert.Equal(new Dictionary<string, string> { ["delete"] = "removeResource", ["rename"] = "renameResource" }, resource.DerivedEdits);
    }

    private static readonly DislSpecification Chained = Specifications.Loaded(Specifications.With("""
        "metamodel": {
          "types": {
            "Thing": { "attributes": { "name": { "type": "string" }, "group": { "type": "string" }, "weight": { "type": "number" } } },
            "Group": {
              "attributes": { "label": { "type": "string" }, "size": { "type": "int" }, "position": { "type": "int" } },
              "children": { "allowed": ["Member"] },
              "derived": { "from": "diagram.nodesOfType('Thing')", "key": "item.group", "id": "'group:' + item.group",
                "attributes": { "label": "item.group", "size": "group.size()", "position": "index" } } },
            "Member": {
              "attributes": { "name": { "type": "string" } },
              "derived": { "from": "diagram.nodesOfType('Thing')", "id": "'member:' + item.name",
                "parent": "diagram.elementById('group:' + item.group)", "slot": "'members'",
                "attributes": { "name": "item.name" } } },
            "Loose": {
              "derived": { "from": "diagram.nodesOfType('Thing')", "id": "'loose:' + item.name", "parent": "diagram.elementById('group:' + item.group)" } },
            "Heavy": {
              "attributes": { "half": { "type": "number" } },
              "derived": { "from": "diagram.nodesOfType('Thing')", "id": "'heavy:' + item.name", "attributes": { "half": "{'a': 2.0, 'b': 1.0}[item.name]" } } },
            "Clash": {
              "derived": { "from": "diagram.nodesOfType('Thing')", "id": "item.name" } },
            "Counted": {
              "attributes": { "count": { "type": "int" } },
              "derived": { "from": "[1, 1.0, 'one', [1, 'x'], [1.0, 'x']]", "key": "item", "id": "'counted:' + string(index)",
                "attributes": { "count": "group.size()" } } }
          }
        }
        """));

    private static FblElement Thing(string id, string group, double? weight, int line)
    {
        var attributes = new Dictionary<string, object?> { ["name"] = id, ["group"] = group };
        if (weight is { } value) attributes["weight"] = value;
        return new FblElement(id, true, "Thing", "thing", false, attributes, null, null, null, null, new Span(0, 0), line);
    }

    private static DislModel ChainedModel() => DislModelBuilder.From(new FblModel(
    [
        Thing("a", "red", 4, 1),
        Thing("b", "blue", 2, 2),
        Thing("c", "red", null, 3),
    ], [], false), Chained);

    [Fact]
    public void KeyedItems_GiveOneNodePerKey_InTheOrderOfTheirFirstItem()
    {
        // Act.
        var model = ChainedModel();

        // Assert.
        var groups = model.Diagram.NodesOfType("Group");
        Assert.Equal(["group:red", "group:blue"], groups.Select(group => group.Id));
        Assert.Equal([2L, 1L], groups.Select(group => group.ValueOf("size")));
        Assert.Equal([0L, 1L], groups.Select(group => group.ValueOf("position")));
        Assert.Equal(["a", "c"], groups[0].Sources.Select(source => source.Id));
    }

    [Fact]
    public void ADerivedType_SeesTheDerivedNodesOfTheTypesBeforeIt_AndIsContainedWhereItsParentAllows()
    {
        // Act.
        var model = ChainedModel();

        // Assert.
        var members = model.Diagram.NodesOfType("Member");
        Assert.Equal(["member:a", "member:b", "member:c"], members.Select(member => member.Id));
        Assert.Equal(["group:red", "group:blue", "group:red"], members.Select(member => member.Parent?.Id));
        Assert.All(members, member => Assert.Equal("members", member.Slot));
    }

    [Fact]
    public void AParentThatDoesNotAllowTheType_LeavesTheNodeAtTheTopLevel_WithOneFinding()
    {
        // Act.
        var model = ChainedModel();

        // Assert.
        Assert.All(model.Diagram.NodesOfType("Loose"), loose => Assert.Null(loose.Parent));
        var finding = Assert.Single(model.Findings, finding => finding.Code == DislModelBuilder.Containment);
        Assert.Equal("3 Loose node(s) are drawn at the top level: their computed parent does not allow them.", finding.Message);
    }

    [Fact]
    public void AnItemWhoseAttributeFails_IsDropped_WithOneFindingForItsType()
    {
        // Act.
        var model = ChainedModel();

        // Assert.
        Assert.Equal(["heavy:a", "heavy:b"], model.Diagram.NodesOfType("Heavy").Select(heavy => heavy.Id));
        Assert.Single(model.Findings, finding => finding.Code == DislModelBuilder.DerivedFailed && finding.Message.StartsWith("Items of Heavy were dropped", StringComparison.Ordinal));
    }

    [Fact]
    public void AnIdAnElementAlreadyHas_IsNotDrawnAgain()
    {
        // Act.
        var model = ChainedModel();

        // Assert.
        Assert.Empty(model.Diagram.NodesOfType("Clash"));
        Assert.Equal(3, model.Findings.Count(finding => finding.Code == DislModelBuilder.DerivedId));
    }

    [Fact]
    public void KeysAreComparedAsCelComparesThem_AnIntAndADoubleOfOneValueMeeting()
    {
        // Act.
        var model = ChainedModel();

        // Assert.
        var counted = model.Diagram.NodesOfType("Counted");
        Assert.Equal([2L, 1L, 2L], counted.Select(node => node.ValueOf("count")));
    }

    /// <summary>
    /// A derived attribute may be called <c>display</c>, a name the language gives a data type's summary (§4.4) too:
    /// under a derived type's <c>attributes</c> it is an Expression of the deriveItem context like any other.
    /// </summary>
    private static readonly DislSpecification Displayed = Specifications.Loaded(Specifications.With("""
        "disl": "0.3",
        "metamodel": {
          "types": {
            "Word": { "attributes": { "text": { "type": "string" } } },
            "Shout": {
              "attributes": { "display": { "type": "string" } },
              "derived": { "from": "diagram.nodesOfType('Word')", "id": "'shout:' + item.id", "attributes": { "display": "item.text + '!'" } }
            }
          }
        }
        """));

    [Fact]
    public void ADerivedAttributeCalledDisplay_IsComputedLikeAnyOther()
    {
        // Act.
        var model = DislModelBuilder.From(new FblModel(
        [
            new FblElement("w", true, "Word", "word", false, new Dictionary<string, object?> { ["text"] = "hey" }, null, null, null, null, new Span(0, 0), 1),
        ], [], false), Displayed);

        // Assert.
        Assert.Empty(model.Findings);
        Assert.Equal("hey!", Assert.Single(model.Diagram.NodesOfType("Shout")).ValueOf("display"));
    }
}
