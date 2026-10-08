using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Yaml;

/// <summary>
/// FBL §6.3 for yaml values the hype cycle graph writes: a flow list whose items need quoting in a
/// flow collection, a multi-line string in a new entry nested in a sequence, a string that starts with
/// <c>-</c> written plain where it can be, an empty value an attribute keeps, and a container created
/// before a key that is not there.
/// </summary>
public class YamlWritingTests
{
    private static readonly FblBinding _binding = Load("""
        [{
          "name": "item", "type": "Item", "at": "/items/*", "id": { "from": { "key": "id" } },
          "attributes": {
            "tags": { "key": "tags", "style": "flow" },
            "text": { "key": "text", "style": "literal" },
            "note": { "key": "note", "empty": "keep" },
            "month": { "key": "month", "style": "plain" }
          },
          "insert": { "place": "after-last", "container": "/items", "keys": ["id", "text", "note", "month", "tags"] },
          "remove": {}
        },
        {
          "name": "other", "type": "Other", "at": "/others/*", "id": { "from": { "key": "id" } },
          "insert": { "place": "after-last", "container": "/others", "create": { "at": { "before": "items" } }, "keys": ["id"] }
        }]
        """);

    private static FblBinding Load(string elements)
    {
        var json = $$"""{ "fbl": "0.1", "bindings": { "t": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "yaml" }, "reader": "declared", "elements": {{elements}} } } }""";
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), out var document);
        Assert.Empty(problems);
        return document!.Bindings["t"];
    }

    private static OpenBody Open(string body) => OpenBody.Open(Encoding.UTF8.GetBytes(body), _binding);

    private static string Text(OpenBody body) => Encoding.UTF8.GetString(body.Bytes);

    [Fact]
    public void AFlowListItemThatAFlowCollectionWouldSplitIsQuoted()
    {
        // Arrange.
        var body = Open("items:\n  - id: a\n    tags: [x]\n");

        // Act.
        var result = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["tags"] = new List<object?> { "energy", "a, b", "c:d", "[x]" } }));

        // Assert: written, and read back as the same four tags rather than as more, fewer or other ones.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("items:\n  - id: a\n    tags: [energy, \"a, b\", \"c:d\", \"[x]\"]\n", Text(body));
        Assert.Equal(["energy", "a, b", "c:d", "[x]"], (List<object?>)body.Model.Elements[0].Attributes["tags"]!);
    }

    [Fact]
    public void AMultiLineStringInANewNestedEntryIsIndentedUnderItsKey()
    {
        // Arrange.
        var body = Open("items:\n  - id: a\n    text: one\n");

        // Act.
        var result = body.Change(new ModelChange.Add("Item", "b", new Dictionary<string, object?> { ["text"] = "Two\nlines" }));

        // Assert: the block's lines sit one step deeper than "text", so the body still reads.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("items:\n  - id: a\n    text: one\n  - id: b\n    text: |-\n      Two\n      lines\n", Text(body));
        Assert.False(body.Model.Unreadable);
        Assert.Equal("Two\nlines", body.Model.Elements[1].Attributes["text"]);
    }

    [Fact]
    public void AValueStartingWithADashKeepsItsPlainStyle()
    {
        // Arrange.
        var body = Open("items:\n  - id: a\n    text: -100-01\n");

        // Act.
        var result = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["text"] = "-3200-01" }));

        // Assert: "-3200-01" reads back as that string in plain style, so the style it had is kept.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("items:\n  - id: a\n    text: -3200-01\n", Text(body));
        Assert.Equal("-3200-01", body.Model.Elements[0].Attributes["text"]);
    }

    [Fact]
    public void AValueStartingWithADashIsWrittenPlainWhenTheAttributeAsksForPlain()
    {
        // Arrange.
        var body = Open("items:\n  - id: a\n");

        // Act.
        var result = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["month"] = "-3200-01" }));

        // Assert.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("items:\n  - id: a\n    month: -3200-01\n", Text(body));
        Assert.Equal("-3200-01", body.Model.Elements[0].Attributes["month"]);
    }

    [Fact]
    public void AValueThatADashWouldMakeAListItemIsStillQuoted()
    {
        // Arrange.
        var body = Open("items:\n  - id: a\n");

        // Act.
        var result = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["month"] = "- 5" }));

        // Assert.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("items:\n  - id: a\n    month: \"- 5\"\n", Text(body));
    }

    [Fact]
    public void AnEmptyValueThatTheAttributeKeepsIsWrittenInANewEntry()
    {
        // Arrange.
        var body = Open("items:\n  - id: a\n");

        // Act.
        var result = body.Change(new ModelChange.Add("Item", "b", new Dictionary<string, object?> { ["note"] = "", ["text"] = "" }));

        // Assert: "note" says keep, "text" says nothing, so only "note" is written.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("items:\n  - id: a\n  - id: b\n    note: \"\"\n", Text(body));
    }

    [Fact]
    public void AContainerCreatedBeforeAKeyThatIsNotThereIsCreatedAtTheEnd()
    {
        // Arrange.
        var body = Open("top: 1\n");

        // Act.
        var result = body.Change(new ModelChange.Add("Other", "o", new Dictionary<string, object?>()));

        // Assert.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("top: 1\nothers:\n  - id: o\n", Text(body));
    }

    [Fact]
    public void RemovingTheLastEntryOfABodyWithoutAFinalNewlineKeepsItWithout()
    {
        // Arrange.
        var body = Open("items:\n  - id: a\n  - id: b");

        // Act.
        var result = body.Change(new ModelChange.Remove("b"));

        // Assert.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("items:\n  - id: a", Text(body));
    }
}
