using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Planning;

/// <summary>
/// <c>insert.place: {before: key}</c> (FBL §5) is not implemented by any family yet, so every family
/// refuses it, as the lines family always did, rather than putting the new entry somewhere else.
/// </summary>
public class InsertPlacementTests
{
    public static TheoryData<string, string, string> Families => new()
    {
        { "lines", """{ "name": "item", "type": "Item", "line": "^item\\s+(?<id>\\S+)$", "id": { "from": { "group": "id" } }, "insert": { "place": { "before": "item" }, "emit": "item {id}" } }""", "item a\n" },
        { "yaml", """{ "name": "item", "type": "Item", "at": "/items/*", "id": { "from": { "key": "id" } }, "insert": { "place": { "before": "id" }, "container": "/items", "keys": ["id"] } }""", "items:\n  - id: a\n" },
        { "json", """{ "name": "item", "type": "Item", "at": "/items/*", "id": { "from": { "key": "id" } }, "insert": { "place": { "before": "id" }, "container": "/items", "keys": ["id"] } }""", "{ \"items\": [ { \"id\": \"a\" } ] }\n" },
        { "xml", """{ "name": "item", "type": "Item", "at": "/items/item", "id": { "from": { "attribute": "id" } }, "insert": { "place": { "before": "item" }, "emit": "<item id=\"{id}\"/>" } }""", "<items>\n  <item id=\"a\"/>\n</items>\n" },
    };

    [Theory]
    [MemberData(nameof(Families))]
    public void PlacingANewEntryBeforeAKeyIsRefused(string family, string element, string text)
    {
        // Arrange.
        var json = $$"""{ "fbl": "0.1", "bindings": { "t": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "{{family}}" }, "reader": "declared", "elements": [{{element}}] } } }""";
        Assert.Empty(FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), out var document));
        var body = OpenBody.Open(Encoding.UTF8.GetBytes(text), document!.Bindings["t"]);
        Assert.Single(body.Model.Elements);

        // Act.
        var result = body.Change(new ModelChange.Add("Item", "b", new Dictionary<string, object?>()));

        // Assert: refused with the lines family's sentence, and the body is untouched.
        var refused = Assert.IsType<PlanResult.Refused>(result);
        Assert.Equal($"A {family} body cannot place a new entry 'before'.", refused.Reason);
        Assert.Equal(text, Encoding.UTF8.GetString(body.Bytes));
    }
}
