using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Planning;

/// <summary>
/// An insert's <c>constant</c> (FBL 0.8 §6.2, etalii.adp spec 033): the keys and values an insert writes
/// into every new entry as they are, whatever the add gives. A constant that <c>insert.keys</c> names is
/// written at its place there; every other one after the entry's other keys, in the constant's member order.
/// Each value is written as its JSON type, as §6.3 writes a new value; no other edit writes it.
/// </summary>
public class InsertConstantTests
{
    private const string Constant = """{ "kind": "task", "size": 3, "done": false, "format": "1.0", "none": null }""";

    private static FblBinding Load(string family, string keys)
    {
        var json = $$"""
            { "fbl": "0.8", "bindings": { "t": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "{{family}}" }, "reader": "declared", "text": { "quote": "double" }, "elements": [{
              "name": "item", "type": "Item", "at": "/items/*", "id": { "from": { "key": "id" } },
              "attributes": { "name": { "key": "name" } },
              "insert": { "place": "end", "container": "/items", "keys": {{keys}}, "constant": {{Constant}} },
              "remove": {}
            }] } } }
            """;
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), out var document);
        Assert.DoesNotContain(problems, p => p.Severity == LoadSeverity.Error);
        return document!.Bindings["t"];
    }

    private static string Text(OpenBody body) => Encoding.UTF8.GetString(body.Bytes);

    private static ModelChange.Add AddB() => new("Item", "b", new Dictionary<string, object?> { ["name"] = "B" });

    [Fact]
    public void AYamlConstantNamedByKeysIsWrittenAtItsPlaceAndTheOthersAfterInMemberOrder()
    {
        // Arrange: keys names kind second, and none of the other four constants.
        var body = OpenBody.Open("items:\n  - id: a\n    kind: task\n    name: A\n"u8.ToArray(), Load("yaml", """["id", "kind", "name"]"""));

        // Act.
        var result = body.Change(AddB());

        // Assert: kind second; size, done, format and none after the name, as their JSON types, the string 1.0 quoted
        // because it is not plain-safe (FBL 6.3).
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("items:\n  - id: a\n    kind: task\n    name: A\n  - id: b\n    kind: task\n    name: B\n    size: 3\n    done: false\n    format: \"1.0\"\n    none: null\n", Text(body));
    }

    [Fact]
    public void AYamlConstantNotNamedByKeysFollowsTheKeysTheRuleWrites()
    {
        // Arrange: no keys, so the entry's keys are the id's and the attribute's, and every constant follows them.
        var body = OpenBody.Open("items:\n  - id: a\n    name: A\n"u8.ToArray(), Load("yaml", "[]"));

        // Act.
        var result = body.Change(AddB());

        // Assert.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("items:\n  - id: a\n    name: A\n  - id: b\n    name: B\n    kind: task\n    size: 3\n    done: false\n    format: \"1.0\"\n    none: null\n", Text(body));
    }

    [Fact]
    public void AJsonConstantIsWrittenAsItsJsonType()
    {
        // Arrange.
        var body = OpenBody.Open("{\n  \"items\": [\n    {\n      \"id\": \"a\",\n      \"name\": \"A\"\n    }\n  ]\n}\n"u8.ToArray(), Load("json", """["id", "kind", "name"]"""));

        // Act.
        var result = body.Change(AddB());

        // Assert: a number, a bool and null as such, never as strings; the string 1.0 stays a string; kind second.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("{\n  \"items\": [\n    {\n      \"id\": \"a\",\n      \"name\": \"A\"\n    },\n    {\n      \"id\": \"b\",\n      \"kind\": \"task\",\n      \"name\": \"B\",\n      \"size\": 3,\n      \"done\": false,\n      \"format\": \"1.0\",\n      \"none\": null\n    }\n  ]\n}\n", Text(body));
    }

    [Theory]
    [InlineData("yaml", "items:\n  - id: a\n    kind: task\n    name: A\n", "items:\n  - id: a\n    kind: task\n    name: Renamed\n")]
    [InlineData("json", "{ \"items\": [ { \"id\": \"a\", \"kind\": \"task\", \"name\": \"A\" } ] }\n", "{ \"items\": [ { \"id\": \"a\", \"kind\": \"task\", \"name\": \"Renamed\" } ] }\n")]
    public void ASetLeavesAConstantAsItIs(string family, string text, string expected)
    {
        // Arrange: an entry that holds one constant and lacks the others.
        var body = OpenBody.Open(Encoding.UTF8.GetBytes(text), Load(family, """["id", "kind", "name"]"""));

        // Act.
        var result = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["name"] = "Renamed" }));

        // Assert: only the name changes; no constant is written, added or corrected by a set (FBL 6.4).
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal(expected, Text(body));
    }
}
