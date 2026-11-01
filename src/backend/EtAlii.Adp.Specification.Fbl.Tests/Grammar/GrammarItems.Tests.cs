using System.Text;
using System.Text.Json;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Grammar;

/// <summary>
/// FBL 0.12's <c>items</c> (etalii.adp spec 071, F2): a <c>{node}</c> slot of a <c>many</c> attribute whose items are
/// structured values, one per node its selector reaches, each a map of the item attributes its own slots read (section
/// 5.2); a set writes the difference by splices of one value, one item and one separator (section 6.3).
/// </summary>
public class GrammarItemsTests
{
    /// <summary>Named marks, each pinned at a bracketed list of points or at one bare point, as a Wardley map's annotation is.</summary>
    private const string Marks = "start: unit*\n?unit: mark\n     | _NL\nmark: NAME points\npoints: \"[\" point (\",\" point)* \"]\" | bare_point\npoint: \"[\" x \",\" y \"]\"\nbare_point: \"[\" x \",\" y \"]\" -> point\nx: NUMBER\ny: NUMBER\nNAME: /[a-z]+/\nNUMBER: /-?[0-9]+(\\.[0-9]+)?/\n_NL: /(\\r?\\n[ \\t]*)+/\nWHITESPACE: /[ \\t]+/\n%ignore WHITESPACE\n";

    private const string Body = "a [[1,2],[3,4]]\nb [5, 6]\n";

    [Fact]
    public void EachNodeTheSelectorReaches_IsOneItem_AMapOfWhatItsItemSlotsRead()
    {
        // Act.
        var body = Open(Body);

        // Assert: two items of the bracketed list, one of the bare point, each value as its text.
        Assert.False(body.Model.Unreadable);
        Assert.Equal(["1 2", "3 4"], Points(body, "a"));
        Assert.Equal(["5 6"], Points(body, "b"));
    }

    [Theory]
    [InlineData(new long[] { 1, 2, 3, 5 }, "replace-value 12 13 '5'", "a [[1,2],[3,5]]\nb [5, 6]\n")]
    [InlineData(new long[] { 1, 2, 3, 4, 7, 8 }, "insert-key 14 14 ',[7,8]'", "a [[1,2],[3,4],[7,8]]\nb [5, 6]\n")]
    [InlineData(new long[] { 3, 4 }, "remove-key 3 9 ''", "a [[3,4]]\nb [5, 6]\n")]
    [InlineData(new long[] { 1, 2 }, "remove-key 8 14 ''", "a [[1,2]]\nb [5, 6]\n")]
    public void ASetOfTheItems_WritesTheirDifference_AndIsUndoneByItsBytes(long[] values, string splice, string expected)
    {
        // Arrange.
        var body = Open(Body);

        // Act.
        var result = body.Change(Set("a", values));

        // Assert: one splice of that shape; the bytes it leaves read back as the set; undo puts back the bytes it replaced.
        Assert.Equal(splice, Describe(Assert.Single(Assert.IsType<PlanResult.Planned>(result).Edit.Splices)));
        Assert.Equal(expected, Encoding.UTF8.GetString(body.Bytes));
        Assert.Equal(values.Chunk(2).Select(pair => $"{pair[0]} {pair[1]}"), Points(body, "a"));
        Assert.IsType<UndoResult.Done>(body.Undo());
        Assert.Equal(Body, Encoding.UTF8.GetString(body.Bytes));
    }

    [Fact]
    public void AValueThatIsTheSameNumber_KeepsItsBytes()
    {
        // Arrange.
        var body = Open("a [[1.50,2]]\n");

        // Act: 1.5 is the number 1.50 is written as; only y changes.
        var result = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["points"] = new List<object?> { Point(1.5, 3L) } }));

        // Assert.
        Assert.Equal("replace-value 9 10 '3'", Describe(Assert.Single(Assert.IsType<PlanResult.Planned>(result).Edit.Splices)));
        Assert.Equal("a [[1.50,3]]\n", Encoding.UTF8.GetString(body.Bytes));
    }

    [Fact]
    public void ANewEntry_WritesItsItemsByTheSlotsCreate_JoinedByTheSeparator()
    {
        // Arrange.
        var body = Open(Body);

        // Act.
        var result = body.Change(new ModelChange.Add("Mark", "c", new Dictionary<string, object?> { ["name"] = "c", ["points"] = new List<object?> { Point(7L, 8L), Point(9L, 0L) } }));

        // Assert.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal(Body + "c [[7,8],[9,0]]\n", Encoding.UTF8.GetString(body.Bytes));
        Assert.Equal(["7 8", "9 0"], Points(body, "c"));
    }

    [Theory]
    [InlineData("\"node\": \"points/point\", \"items\": { \"x\": { \"key\": \"x\" } }", "/items")]
    [InlineData("\"node\": \"points/point\", \"items\": [ \"x\" ]", "/items")]
    public void ItemsThatAreNotNodeSlots_AreRefusedWhereTheyStand(string members, string place)
    {
        // Arrange.
        var json = BindingJson($$"""
            [ { "name": "mark", "type": "Mark", "at": "/mark", "id": { "from": { "node": "NAME" } },
                "attributes": { "name": { "node": "NAME", "readOnly": true }, "points": { {{members}} } } } ]
            """);

        // Act.
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), null, out var document);

        // Assert.
        Assert.Contains("/bindings/b/elements/0/attributes/points" + place, problems.Where(problem => problem.Severity == LoadSeverity.Error).Select(problem => problem.Pointer));
        Assert.Null(document);
    }

    [Theory]
    [InlineData("\"items\": { \"x\": { \"key\": \"x\" } }", "/items")]
    [InlineData("\"separator\": \",\"", "/separator")]
    public void ItemsOrASeparator_InAnotherFamily_AreRefusedWhereTheyStand(string members, string place)
    {
        // Arrange: a yaml binding whose slot is given one of the grammar family's constructs.
        var json = $$"""{ "fbl": "0.13", "bindings": { "b": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "yaml" }, "reader": "declared", "elements": [ { "name": "item", "type": "Item", "at": "/items/*", "attributes": { "n": { "key": "n", {{members}} } } } ] } } }""";

        // Act.
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), null, out var document);

        // Assert.
        Assert.Contains("/bindings/b/elements/0/attributes/n" + place, problems.Where(problem => problem.Severity == LoadSeverity.Error).Select(problem => problem.Pointer));
        Assert.Null(document);
    }

    [Fact]
    public void ItemsOnASingleValuedAttribute_AreRefusedByTheToolTypesAttributeTypes()
    {
        // Arrange.
        var binding = Binding(Elements);

        // Act.
        var single = binding.CheckManyAttributes((type, name) => !(type == "Mark" && name == "points"));
        var many = binding.CheckManyAttributes((_, _) => true);

        // Assert.
        Assert.Equal(["/bindings/b/elements/0/attributes/points/items"], single.Where(problem => problem.Severity == LoadSeverity.Error).Select(problem => problem.Pointer));
        Assert.Empty(many);
    }

    private const string Elements = """
        [ { "name": "mark", "type": "Mark", "at": "/mark", "id": { "from": { "node": "NAME" } },
            "attributes": { "name": { "node": "NAME", "readOnly": true },
                            "points": { "node": "points/point", "items": { "x": { "node": "x" }, "y": { "node": "y" } }, "separator": ",", "create": { "emit": "[{x},{y}]" } } },
            "insert": { "place": "after-last", "emit": "{name} \\[{points}\\]" }, "remove": {} } ]
        """;

    private static Dictionary<string, object?> Point(object x, object y) => new(StringComparer.Ordinal) { ["x"] = x, ["y"] = y };

    private static ModelChange.Set Set(string id, long[] values) =>
        new(id, new Dictionary<string, object?> { ["points"] = values.Chunk(2).Select(object? (pair) => Point(pair[0], pair[1])).ToList() });

    private static OpenBody Open(string text) =>
        OpenBody.Open(Encoding.UTF8.GetBytes(text), Binding(Elements), new FblOptions { FileName = "body.t", Many = (type, name) => type == "Mark" && name == "points" });

    private static string[] Points(OpenBody body, string id) =>
        [.. Assert.IsAssignableFrom<IEnumerable<object?>>(Assert.IsType<FblElement>(body.Model.Find(id)).Attributes["points"])
            .Select(Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>)
            .Select(item => $"{item["x"]} {item["y"]}")];

    private static string Describe(Splice splice) => $"{WireName.Of(splice.Operation)} {splice.Start} {splice.End} '{splice.Text}'";

    private static FblBinding Binding(string elements)
    {
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(BindingJson(elements)), null, out var document);
        Assert.True(document is not null, string.Join("\n", problems));
        return document.Bindings["b"];
    }

    private static string BindingJson(string elements) =>
        $$"""{ "fbl": "0.13", "bindings": { "b": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "grammar", "grammar": {{JsonSerializer.Serialize(Marks)}} }, "reader": "declared", "elements": {{elements}} } } }""";
}
