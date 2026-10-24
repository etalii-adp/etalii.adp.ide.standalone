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
/// before a key that is not there, and none created at the end of a document whose root is not a mapping.
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

    private static FblBinding Load(string elements, string version = "0.1")
    {
        var json = $$"""{ "fbl": "{{version}}", "bindings": { "t": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "yaml" }, "reader": "declared", "elements": {{elements}} } } }""";
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), out var document);
        Assert.Empty(problems);
        return document!.Bindings["t"];
    }

    private static OpenBody Open(string body) => OpenBody.Open(Encoding.UTF8.GetBytes(body), _binding);

    [Theory]
    [InlineData("- a\n- b\n")]
    [InlineData("# a comment\n- a\n")]
    [InlineData("just text\n")]
    public void AContainerCreatedAtTheEndOfTheDocument_IsRefused_WhereTheRootIsNotAMapping(string text)
    {
        // Arrange: a root that is a list or a scalar has no key to create the container beside.
        var binding = Load("""
            [{
              "name": "item", "type": "Item", "at": "/items/*", "id": { "from": { "key": "id" } },
              "insert": { "place": "after-last", "container": "/items", "create": { "at": "end-of-document" }, "keys": ["id"] }
            }]
            """);
        var body = OpenBody.Open(Encoding.UTF8.GetBytes(text), binding);

        // Act.
        var result = body.Change(new ModelChange.Add("Item", "n", new Dictionary<string, object?>()));

        // Assert: writing items: after it would leave a file that is not YAML.
        Assert.IsType<PlanResult.Refused>(result);
        Assert.Equal(text, Text(body));
    }

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
    public void AnEmptyValueIsWrittenInANewEntryAsASetWritesIt()
    {
        // Arrange.
        var body = Open("items:\n  - id: a\n");

        // Act.
        var result = body.Change(new ModelChange.Add("Item", "b", new Dictionary<string, object?> { ["note"] = "", ["text"] = "" }));

        // Assert: "note" says keep and "text" says nothing, which for a string is keep too (FBL 0.6 §6.2), so both are written.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("items:\n  - id: a\n  - id: b\n    text: \"\"\n    note: \"\"\n", Text(body));
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

    [Theory]
    [InlineData("yes")]
    [InlineData("007")]
    [InlineData("~")]
    public void ATextThatYamlWouldTypeIsWrittenQuotedAndReadsBackAsThatText(string text)
    {
        // Arrange: an attribute read as text, with no style asked for.
        var body = OpenText("items:\n  - id: a\n");

        // Act.
        var result = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["label"] = text }));

        // Assert: the plain-safe test of FBL §6.3 still holds, so a reader that types the value reads the same text.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal($"items:\n  - id: a\n    label: \"{text}\"\n", Text(body));
        Assert.Equal(text, body.Model.Elements[0].Attributes["label"]);
    }

    [Fact]
    public void ATextIsWrittenPlainWhenTheAttributeAsksForPlainAndReadsBackAsThatText()
    {
        // Arrange.
        var body = OpenText("items:\n  - id: a\n");

        // Act.
        var result = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["word"] = "yes" }));

        // Assert: read as text, "yes" is what the author wrote, so plain is what YAML's syntax allows.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("items:\n  - id: a\n    word: yes\n", Text(body));
        Assert.Equal("yes", body.Model.Elements[0].Attributes["word"]);
    }

    private static readonly FblBinding _text = Load("""
        [{
          "name": "item", "type": "Item", "at": "/items/*", "id": { "from": { "key": "id" } },
          "attributes": {
            "label": { "key": "label", "read": "text" },
            "word": { "key": "word", "read": "text", "style": "plain" }
          }
        }]
        """, "0.5");

    private static OpenBody OpenText(string body) => OpenBody.Open(Encoding.UTF8.GetBytes(body), _text);

    private static readonly FblBinding _typed = Load("""
        [{
          "name": "item", "type": "Item", "at": "/items/*", "id": { "from": { "key": "id" } },
          "attributes": {
            "label": { "key": "label" },
            "row": { "key": "row", "fallback": { "value": 0 } },
            "count": { "key": "count", "quoted": "convert" }
          }
        }]
        """, "0.5");

    private static readonly FblOptions _types = new() { FileName = "plan.t", AttributeType = (_, attribute) => attribute is "row" or "count" ? "int" : "string" };

    private static OpenBody OpenTyped(string body) => OpenBody.Open(Encoding.UTF8.GetBytes(body), _typed, _types);

    [Fact]
    public void SettingAnUnconvertedValueReplacesExactlyItsBytes()
    {
        // Arrange: row reads as its fallback, 0.
        var body = OpenTyped("items:\n  - id: a\n    row: wide   # not a row\n");

        // Act.
        var result = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["row"] = 3L }));

        // Assert: the value is replaced, and the comment after it is kept.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("items:\n  - id: a\n    row: 3   # not a row\n", Text(body));
        Assert.Equal(3L, body.Model.Elements[0].Attributes["row"]);
    }

    [Fact]
    public void SettingAnotherAttributeLeavesAnUnconvertedValueUntouched()
    {
        // Arrange.
        var body = OpenTyped("items:\n  - id: a\n    label: one\n    row: wide\n");

        // Act.
        var result = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["label"] = "two" }));

        // Assert.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("items:\n  - id: a\n    label: two\n    row: wide\n", Text(body));
        Assert.Equal(0L, body.Model.Elements[0].Attributes["row"]);
    }

    [Fact]
    public void ANumberReplacingAQuotedScalarUnderQuotedConvertKeepsItsQuotes()
    {
        // Arrange: count, written "3", reads as 3.
        var body = OpenTyped("items:\n  - id: a\n    count: \"3\"\n");

        // Act.
        var result = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["count"] = 5L }));

        // Assert.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("items:\n  - id: a\n    count: \"5\"\n", Text(body));
        Assert.Equal(5L, body.Model.Elements[0].Attributes["count"]);
    }
}
