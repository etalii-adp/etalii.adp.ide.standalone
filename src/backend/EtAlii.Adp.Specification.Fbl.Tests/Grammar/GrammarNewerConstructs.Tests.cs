using System.Text;
using System.Text.Json;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Grammar;

/// <summary>
/// A binding of the grammar family that uses a construct this library does not read (FBL 0.17 §15.1; etalii.adp spec 072,
/// Requirement 8.1): each construct is warned of by name, no splice touches the text it governs, an edit that would is
/// refused with the construct as its reason, and a construct of the body, whose text cannot be told, makes the file read-only.
/// </summary>
public class GrammarNewerConstructsTests
{
    /// <summary>Items of a key and a value, each with an optional note after it on its line.</summary>
    private const string Items = "start: (item | _NL)*\nitem: KEY \"=\" VALUE [note]\nnote: \"!\" KEY\nKEY: /[a-z]+/\nVALUE: /[0-9]+/\n_NL: /\\r?\\n/\nWS: /[ \\t]+/\n%ignore WS\n";

    private const string ItemRule = """{ "name": "item", "type": "Item", "at": "/item", "id": { "from": { "node": "KEY" } }, "attributes": { "key": { "node": "KEY" }, "value": { "node": "VALUE" } }, "insert": { "place": "end", "emit": "{key} = {value}" }, "remove": {} }""";

    private const string NoteRule = """{ "name": "note", "type": "Note", "at": "/item/note", "id": { "from": { "node": "KEY" } }, "attributes": { "label": { "node": "KEY" } } }""";

    [Theory]
    [InlineData("\"insert\": { \"place\": \"end\",", "\"insert\": { \"continues\": { \"at\": \"/item\", \"same\": { \"value\": { \"node\": \"VALUE\" } }, \"separator\": \";\" }, \"place\": \"end\",", "/bindings/b/elements/0/insert/continues", "insert.continues")]
    [InlineData("\"remove\": {}", "\"remove\": { \"parts\": [ { \"item\": \".\", \"separator\": \",\" } ] }", "/bindings/b/elements/0/remove/parts", "remove.parts")]
    [InlineData("\"remove\": {}", "\"remove\": { \"parts\": [ { \"item\": \".\", \"separator\": \",\" } ], \"whole\": \".\" }", "/bindings/b/elements/0/remove/whole", "remove.whole")]
    [InlineData("\"value\": { \"node\": \"VALUE\" }", "\"value\": { \"node\": \"VALUE\", \"expand\": true }", "/bindings/b/elements/0/attributes/value/expand", "expand")]
    [InlineData("\"value\": { \"node\": \"VALUE\" }", "\"value\": { \"node\": \"VALUE\", \"ordinal\": \"items\" }", "/bindings/b/elements/0/attributes/value/ordinal", "ordinal")]
    [InlineData("\"value\": { \"node\": \"VALUE\" }", "\"value\": { \"node\": \"VALUE\", \"name\": true }", "/bindings/b/elements/0/attributes/value/name", "name")]
    [InlineData("\"value\": { \"node\": \"VALUE\" }", "\"value\": { \"node\": \"VALUE\", \"escape\": { \"with\": \"\\\\\", \"quote\": \"'\", \"sequences\": { \"n\": \"\\n\" } } }", "/bindings/b/elements/0/attributes/value/escape/sequences", "escape.sequences")]
    [InlineData("\"family\": \"grammar\",", "\"family\": \"grammar\", \"prefixes\": { \"declarations\": [] },", "/bindings/b/body/prefixes", "body.prefixes")]
    [InlineData("\"family\": \"grammar\",", "\"family\": \"grammar\", \"ordinals\": { \"items\": { \"at\": [ \"/item\" ] } },", "/bindings/b/body/ordinals", "body.ordinals")]
    [InlineData("\"family\": \"grammar\",", "\"family\": \"grammar\", \"order\": \"closed\",", "/bindings/b/body/order", "body.order")]
    public void EachConstructNotRead_IsWarnedOfByName(string kept, string planted, string pointer, string construct)
    {
        // Arrange: the binding loads with no problem before the construct is planted.
        Assert.Empty(Load(Json()));
        var json = Json().Replace(kept, planted, StringComparison.Ordinal);
        Assert.NotEqual(Json(), json);

        // Act.
        var problems = Load(json);

        // Assert.
        Assert.All(problems, problem => Assert.Equal(LoadSeverity.Warning, problem.Severity));
        var warning = Assert.Single(problems, problem => problem.Pointer == pointer);
        Assert.Contains($"does not read '{construct}'", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDocumentOrder_IsNoConstructNotRead()
    {
        // Act: document is the order this library reads in.
        var problems = Load(Json().Replace("\"family\": \"grammar\",", "\"family\": \"grammar\", \"order\": \"document\",", StringComparison.Ordinal));

        // Assert.
        Assert.Empty(problems);
    }

    [Fact]
    public void ARuleWithAConstructNotRead_IsReadAndEditedNoWhereItsTextIs()
    {
        // Arrange: the note's label has an 'expand', which this library does not read.
        var body = Open("a = 1 !x\nb = 2\n", NoteRule.Replace("\"label\": { \"node\": \"KEY\" }", "\"label\": { \"node\": \"KEY\", \"expand\": true }", StringComparison.Ordinal));

        // Act.
        var setNote = body.Change(new ModelChange.Set("x", new Dictionary<string, object?> { ["label"] = "y" }));
        var removeHolder = body.Change(new ModelChange.Remove("a"));
        var setHolder = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["value"] = "7" }));
        var removeOther = body.Change(new ModelChange.Remove("b"));

        // Assert: the note is read, and neither it nor the item that holds it is spliced through its text.
        Assert.False(body.IsReadOnly);
        Assert.Contains("'expand'", Assert.IsType<PlanResult.Refused>(setNote).Reason, StringComparison.Ordinal);
        Assert.Contains("'expand'", Assert.IsType<PlanResult.Refused>(removeHolder).Reason, StringComparison.Ordinal);
        Assert.IsType<PlanResult.Planned>(setHolder);
        Assert.IsType<PlanResult.Planned>(removeOther);
        Assert.Equal("a = 7 !x\n", Text(body));
    }

    [Fact]
    public void ARuleWithAConstructNotRead_RefusesItsOwnAdd()
    {
        // Arrange: the item's insert continues a statement, which this library does not read.
        var body = Open("a = 1\n", NoteRule, ItemRule.Replace("\"insert\": { ", "\"insert\": { \"continues\": { \"at\": \"/item\", \"same\": { \"value\": { \"node\": \"VALUE\" } }, \"separator\": \";\" }, ", StringComparison.Ordinal));

        // Act.
        var result = body.Change(new ModelChange.Add("Item", "c", new Dictionary<string, object?> { ["key"] = "c", ["value"] = "9" }));

        // Assert.
        Assert.Contains("'insert.continues'", Assert.IsType<PlanResult.Refused>(result).Reason, StringComparison.Ordinal);
        Assert.Equal("a = 1\n", Text(body));
    }

    [Fact]
    public void ABodyWithAConstructNotRead_IsReadOnly()
    {
        // Arrange.
        var json = Json().Replace("\"family\": \"grammar\",", "\"family\": \"grammar\", \"prefixes\": { \"declarations\": [] },", StringComparison.Ordinal);
        var body = OpenBody.Open("a = 1\n"u8.ToArray(), Binding(json), new FblOptions { FileName = "body.t" });

        // Act.
        var result = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["value"] = "7" }));

        // Assert.
        Assert.True(body.IsReadOnly);
        Assert.Contains("'prefixes'", Assert.IsType<PlanResult.Refused>(result).Reason, StringComparison.Ordinal);
        Assert.Equal("a = 1\n", Text(body));
    }

    private static string Json(string note = NoteRule, string item = ItemRule) =>
        $$"""{ "fbl": "0.9", "bindings": { "b": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "grammar", "grammar": {{JsonSerializer.Serialize(Items)}} }, "reader": "declared", "elements": [ {{item}}, {{note}} ], "relations": [] } } }""";

    private static IReadOnlyList<LoadDiagnostic> Load(string json) => FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), null, out _);

    private static FblBinding Binding(string json)
    {
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), null, out var document);
        Assert.True(document is not null, string.Join("\n", problems));
        return document.Bindings["b"];
    }

    private static OpenBody Open(string text, string note, string item = ItemRule) =>
        OpenBody.Open(Encoding.UTF8.GetBytes(text), Binding(Json(note, item)), new FblOptions { FileName = "body.t" });

    private static string Text(OpenBody body) => Encoding.UTF8.GetString(body.Bytes);
}
