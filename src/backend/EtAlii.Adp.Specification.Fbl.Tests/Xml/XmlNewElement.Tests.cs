using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Xml;

/// <summary>
/// FBL 0.6 §6.2 for a new xml element written without an <c>emit</c>: an attribute the add gives its
/// empty value is written or left out by its binding's <c>empty</c>, with the defaults a set has, as the
/// json, lines and yaml families do (spec 025 B-09, T307).
/// </summary>
public class XmlNewElementTests
{
    private static readonly FblBinding _binding = Load("""
        [{
          "name": "item", "type": "Item", "at": "/items/item", "id": { "from": { "attribute": "id" } },
          "attributes": {
            "text": { "attribute": "text", "empty": "keep" },
            "note": { "attribute": "note", "empty": "remove" },
            "plain": { "attribute": "plain" },
            "title": { "attribute": "title", "empty": "refuse" }
          },
          "insert": { "place": "after-last", "container": "/items" },
          "remove": {}
        }]
        """);

    private static FblBinding Load(string elements)
    {
        var json = $$"""{ "fbl": "0.1", "bindings": { "t": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "xml" }, "reader": "declared", "elements": {{elements}} } } }""";
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), out var document);
        Assert.Empty(problems);
        return document!.Bindings["t"];
    }

    private static OpenBody Open(string text) => OpenBody.Open(Encoding.UTF8.GetBytes(text), _binding);

    private static string Text(OpenBody body) => Encoding.UTF8.GetString(body.Bytes);

    [Fact]
    public void AnEmptyAttributeUnderRemoveIsLeftOutOfANewElement()
    {
        // Arrange.
        var body = Open("<items>\n  <item id=\"a\"/>\n</items>\n");

        // Act.
        var result = body.Change(new ModelChange.Add("Item", "b", new Dictionary<string, object?> { ["note"] = "", ["title"] = "B" }));

        // Assert: "note" says remove, so its attribute is left out, as a set to the empty value removes it.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("<items>\n  <item id=\"a\"/>\n  <item id=\"b\" title=\"B\"/>\n</items>\n", Text(body));
    }

    [Fact]
    public void AnEmptyAttributeUnderKeepOrNoRuleIsWrittenInANewElement()
    {
        // Arrange.
        var body = Open("<items>\n  <item id=\"a\"/>\n</items>\n");

        // Act.
        var result = body.Change(new ModelChange.Add("Item", "b", new Dictionary<string, object?> { ["text"] = "", ["plain"] = "", ["title"] = "B" }));

        // Assert: "text" says keep and "plain" says nothing, which for a string is keep too, so both are written empty.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("<items>\n  <item id=\"a\"/>\n  <item id=\"b\" text=\"\" plain=\"\" title=\"B\"/>\n</items>\n", Text(body));
    }

    [Fact]
    public void AnEmptyAttributeUnderRefuseRefusesTheNewElement()
    {
        // Arrange.
        const string text = "<items>\n  <item id=\"a\"/>\n</items>\n";
        var body = Open(text);

        // Act.
        var result = body.Change(new ModelChange.Add("Item", "b", new Dictionary<string, object?> { ["title"] = "" }));

        // Assert: refused, and the body is untouched.
        Assert.IsType<PlanResult.Refused>(result);
        Assert.Equal(text, Text(body));
    }
}
