using EtAlii.Adp.Specification.Fbl.Links;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// What DISL 0.12 adds for links between documents (§4.12, §9.4; etalii.adp specs 089 and 092): the attribute type <c>link</c>, a
/// <c>linked</c> type and the <c>link</c> attribute it holds, a <c>linkable</c> type and the attribute that names it, and the
/// <c>lookup</c> action, which creates a linked element for what the person picked and is offered only where the host has a lookup.
/// </summary>
public class LinksBetweenDocumentsTests
{
    private const string Parts = """
        "disl": "0.12",
        "metamodel": { "types": {
          "Node": { "linkable": { "nameProperty": "title" }, "attributes": { "title": { "type": "string" }, "note": { "type": "string" } } },
          "LinkedNode": { "extends": ["Node"], "linked": { "attribute": "link" }, "attributes": { "link": { "type": "link" } } },
          "Plain": { "linkable": {}, "attributes": { "name": { "type": "string" } } } } },
        "toolbox": { "contextMenus": [
          { "for": [ "Node" ], "tools": [ { "kind": "operation", "operation": "addLinked", "label": "Add from another document…" }, { "kind": "editLabel", "label": "Rename" } ] },
          { "for": [ "diagram" ], "tools": [ { "kind": "operation", "operation": "addLinkedHere", "label": "Add here…" } ] } ] },
        "behavior": { "operations": {
          "addLinked": { "for": ["Node"], "actions": [ { "lookup": { "create": "LinkedNode", "as": "child" }, "as": "added" }, { "select": "added" } ] },
          "addLinkedHere": { "for": "diagram", "actions": [ { "lookup": { "create": "LinkedNode", "as": "at" }, "as": "added" }, { "set": { "note": "'placed'" }, "target": "added" } ] },
          "addPlain": { "for": ["Node"], "actions": [ { "lookup": { "create": "Plain", "as": "child" } } ] },
          "misplaced": { "for": ["Node"], "actions": [ { "lookup": { "create": "LinkedNode", "as": "beside" } } ] } } },
        "forms": { "node": { "for": "Node", "items": [
          { "attribute": "title", "readOnly": { "cel": "has(self.link)" }, "readOnlyReasons": [ "A linked node shows its target's name." ] } ] } }
        """;

    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With(Parts));

    private static readonly DislLookupPick Pick = new("$a1b2c3d4@roadmap.tml#e1.label=Launch", "Launch");

    [Fact]
    public void ALinkAttribute_LoadsAsAString_ThatReadsEmptyWhenUnset()
    {
        var node = new DislDiagram(Specification).AddNode("LinkedNode", "n");

        Assert.Equal("link", Specification.Metamodel.TypeOf("LinkedNode")!.Attributes["link"].Type);
        Assert.Equal("", node.ValueOf("link"));
    }

    [Fact]
    public void ALinkedType_NamesItsLinkAttribute_AndALinkableTypeTheAttributeThatNamesIt()
    {
        var linked = Specification.Metamodel.TypeOf("LinkedNode")!;

        Assert.Equal("link", linked.LinkedAttribute);
        Assert.True(linked.Linkable);
        Assert.Equal("title", linked.NameProperty);
        Assert.Equal("name", Specification.Metamodel.TypeOf("Plain")!.NameProperty);
        Assert.Null(Specification.Metamodel.TypeOf("Node")!.LinkedAttribute);
    }

    [Fact]
    public void ALinkedTypeWhoseAttributeIsNoLink_AndANamePropertyItLacks_AreErrors()
    {
        var diagnostics = Specifications.Diagnostics(Specifications.With("""
            "disl": "0.12",
            "metamodel": { "types": {
              "Wrong": { "linked": { "attribute": "name" }, "linkable": { "nameProperty": "missing" }, "attributes": { "name": { "type": "string" } } } } }
            """));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Contains("/metamodel/types/Wrong/linked", StringComparison.Ordinal) && diagnostic.Contains("not an attribute of type link", StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Contains("/metamodel/types/Wrong/linkable/nameProperty", StringComparison.Ordinal));
    }

    [Fact]
    public void ALookup_WithNothingPicked_IsRefusedWithTheSentenceThatTheWindowOffersNone()
    {
        var node = new DislDiagram(Specification).AddNode("Node", "n");

        var transaction = OperationInterpreter.Run(Specification, "addLinked", node.Diagram, node, DislIds.Fixed());

        Assert.Equal(DislLookupPick.NoneOffered, transaction.Refusal);
    }

    [Fact]
    public void ALookupAsAChild_CreatesTheLinkedTypeUnderSelf_WithThePicksLinkAndName()
    {
        var node = new DislDiagram(Specification).AddNode("Node", "n");

        var transaction = OperationInterpreter.Run(Specification, "addLinked", node.Diagram, node, DislIds.Fixed("x1"), new DislInvocation(Lookup: Pick));

        Assert.Null(transaction.Refusal);
        var created = Assert.IsType<DislChange.Create>(Assert.Single(transaction.Changes));
        Assert.Equal("LinkedNode", created.Type);
        Assert.Equal("n", created.ParentId);
        Assert.Equal(Pick.Link, created.Attributes["link"]);
        Assert.Equal("Launch", created.Attributes["title"]);
        Assert.Equal([created.Id], Assert.IsType<HostAction.Select>(Assert.Single(transaction.HostActions)).ElementIds);
    }

    [Fact]
    public void ALookupAt_CreatesTheLinkedTypeAtThePointer_ForTheActionsAfterIt()
    {
        var diagram = new DislDiagram(Specification);
        var position = new Dictionary<string, object?>(StringComparer.Ordinal) { ["x"] = 3.0, ["y"] = 4.0 };

        var transaction = OperationInterpreter.Run(Specification, "addLinkedHere", diagram, null, DislIds.Fixed("x1"), new DislInvocation(Position: position, Lookup: Pick));

        Assert.Null(transaction.Refusal);
        var created = Assert.IsType<DislChange.Create>(transaction.Changes[0]);
        Assert.Null(created.ParentId);
        Assert.NotNull(created.At);
        Assert.Equal("placed", Assert.IsType<DislChange.Set>(transaction.Changes[1]).Attributes["note"]);
    }

    [Theory]
    [InlineData("addPlain", "which is no linked node type")]
    [InlineData("misplaced", "neither 'child' nor 'at'")]
    public void ALookupOfATypeThatIsNotLinked_OrPlacedNeitherWay_IsRefused(string operation, string expected)
    {
        var node = new DislDiagram(Specification).AddNode("Node", "n");

        var transaction = OperationInterpreter.Run(Specification, operation, node.Diagram, node, DislIds.Fixed("x1"), new DislInvocation(Lookup: Pick));

        Assert.Contains(expected, transaction.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOperationThatOpensTheLookup_IsNoMenuEntry_WhereTheHostOffersNone_AndOneWhereItDoes()
    {
        var node = new DislDiagram(Specification).AddNode("Node", "n");

        Assert.Equal("Rename", Labels(new DislEnv(), DislMenuTarget.Element(node)));
        Assert.Equal("Add from another document…,Rename", Labels(new DislEnv(Lookup: true), DislMenuTarget.Element(node)));
        Assert.Equal("", Labels(new DislEnv(), DislMenuTarget.Canvas(node.Diagram)));
        Assert.Equal("Add here…", Labels(new DislEnv(Lookup: true), DislMenuTarget.Canvas(node.Diagram)));
    }

    [Fact]
    public void AFieldsReadOnlyReasons_ApplyOnlyWhileItsOwnReadOnlyHolds()
    {
        var diagram = new DislDiagram(Specification);
        var plain = diagram.AddNode("Node", "n", new Dictionary<string, object?>(StringComparer.Ordinal) { ["title"] = "Mine" });
        var linked = diagram.AddNode("LinkedNode", "l", new Dictionary<string, object?>(StringComparer.Ordinal) { ["title"] = "Launch", ["link"] = Pick.Link });

        Assert.Equal("", Reason(plain));
        Assert.Equal("A linked node shows its target's name.", Reason(linked));
    }

    /// <summary>The labels of the menu entries offered at <paramref name="target"/>, in order.</summary>
    private static string Labels(DislEnv env, DislMenuTarget target) =>
        string.Join(",", ContextMenuDerivation.Derive(Specification, target, env, WireIdMap.None).SelectMany(group => group.Entries).Select(entry => entry.Label));

    /// <summary>The reason the title row of <paramref name="element"/>'s form gives for being read-only, empty when it is not.</summary>
    private static string Reason(DislElement element) =>
        FormDerivation.Derive(Specification, element, new DislEnv(), WireIdMap.None).Single(row => row.Label is "title" or "Title").ReadOnlyReason;

    [Fact]
    public void ALoadingDiagnostic_IsNotRaisedForTheLookupsNames()
    {
        Assert.DoesNotContain(DislLoader.Load(Specifications.With(Parts)).Diagnostics, diagnostic => diagnostic.Severity == LoadSeverity.Error);
    }

    [Fact]
    public void ADocumentIdRule_MintsEightCharactersOfBase36_AndNoneIsMintedWithoutOne()
    {
        var specification = Specifications.Loaded(Specifications.With("""
            "disl": "0.12",
            "persistence": { "format": "fbl", "binding": "x.fbl", "documentId": { "strategy": "nanoid", "alphabet": "base36", "length": 8 } }
            """));

        var id = DislIds.NewDocumentId(specification);

        Assert.True(specification.HasDocumentId);
        Assert.Matches("^[0-9a-z]{8}$", id);
        Assert.False(Specification.HasDocumentId);
        Assert.Null(DislIds.NewDocumentId(Specification));
    }

    [Fact]
    public void ANanoidStrategy_MintsItsAlphabetAndLength_AndTwentyOneOfNanoidsCharactersByDefault()
    {
        var eight = Specifications.Loaded(Specifications.With("""
            "disl": "0.12",
            "persistence": { "format": "fbl", "binding": "x.fbl", "ids": { "strategy": "nanoid", "alphabet": "base36", "length": 8 } }
            """));
        var standard = Specifications.Loaded(Specifications.With("""
            "disl": "0.12",
            "persistence": { "format": "fbl", "binding": "x.fbl", "ids": { "strategy": "nanoid" } }
            """));

        Assert.Matches("^[0-9a-z]{8}$", DislIds.Of(eight).Next("Thing"));
        Assert.Matches("^[A-Za-z0-9_-]{21}$", DislIds.Of(standard).Next("Thing"));
    }

    [Fact]
    public void ADiagramInTheIndex_HoldsItsLinkableElementsByTheirNameProperty_AndTheLinksItsElementsHold()
    {
        var diagram = new DislDiagram(Specification);
        diagram.AddNode("Node", "n", new Dictionary<string, object?>(StringComparer.Ordinal) { ["title"] = "Mine", ["note"] = "A note" });
        diagram.AddNode("LinkedNode", "l", new Dictionary<string, object?>(StringComparer.Ordinal) { ["title"] = "Launch", ["link"] = Pick.Link });
        diagram.AddNode("Plain", "p", new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = "Plain one" });

        var indexed = DislLinks.Indexed(diagram, "/w/ideas.x", "d0c1d2e3");

        Assert.Equal("d0c1d2e3", indexed.DocumentId);
        Assert.Equal("title", indexed.NameProperty);
        Assert.Equal(["n", "l", "p"], indexed.Elements.Select(element => element.Id));
        Assert.Equal([new KeyValuePair<string, string>("title", "Mine"), new KeyValuePair<string, string>("note", "A note")], indexed.Elements[0].Attributes);
        Assert.Equal("Plain one", indexed.Elements[2].ValueOf("name"));
        Assert.Equal([new HeldLink("l", Pick.Link)], indexed.Links);
    }
}
