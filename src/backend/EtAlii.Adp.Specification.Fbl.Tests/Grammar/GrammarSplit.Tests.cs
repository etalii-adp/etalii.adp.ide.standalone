using System.Text;
using System.Text.Json;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Grammar;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Text;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Grammar;

/// <summary>
/// FBL 0.13's <c>split</c>, with the constructs of FBL 0.11 its fixture uses (etalii.adp spec 072, Requirement 11): a
/// <c>{node}</c> slot of a <c>many</c> attribute cuts its value into items (section 4.9), and a set writes the difference
/// into the node by splices, every byte of an item it keeps and between two it keeps staying (section 6.3).
/// </summary>
public class GrammarSplitTests
{
    /// <summary>Named labels, one per line, each with one quoted argument or none, as the fixture grammar-split-slot reads them.</summary>
    private const string Labels = "start: unit*\n?unit: label\n     | _NL\nlabel: NAME [tags]\ntags: STRING\nNAME: /[a-z]+/\nSTRING: /\"([^\"\\\\\\r\\n]|\\\\[^\\r\\n])*\"/\n_NL: /(\\r?\\n[ \\t]*)+/\nWHITESPACE: /[ \\t]+/\nCOMMENT: /\\/\\/[^\\r\\n]*/\n%ignore WHITESPACE\n%ignore COMMENT\n";

    /// <summary>Statements of words, strings and a few signs, each perhaps opening a block, as a Structurizr DSL file is written.</summary>
    private const string Statements = "start: unit*\n?unit: statement\n     | \"}\" _NL\n     | _NL\nstatement: WORD part* [\"{\"] _NL\n?part: WORD\n     | STRING\n     | \"=\"\n     | \"->\"\n     | \"*\"\nWORD: /[A-Za-z_][A-Za-z0-9_]*/\nSTRING: /\"([^\"\\\\\\r\\n]|\\\\[^\\r\\n])*\"/\n_NL: /(\\r?\\n[ \\t]*)+/\nWHITESPACE: /[ \\t]+/\n%ignore WHITESPACE\n";

    [Theory]
    [InlineData(" Extra ,More,, ", new[] { "Extra", "More" }, new[] { 11, 16, 18, 22 })]
    [InlineData("", new string[0], new int[0])]
    [InlineData("Existing System", new[] { "Existing System" }, new[] { 10, 25 })]
    [InlineData(" é ,b", new[] { "é", "b" }, new[] { 12, 14, 18, 19 })]
    public void AValue_IsCutAtItsSplit_EachPieceWithoutItsWhiteSpace_AnEmptyOneNoItem(string value, string[] items, int[] spans)
    {
        // Act: the value standing in the body from byte 10.
        var cut = SplitItems.Cut(value, 10, ",");

        // Assert: the texts, and the spans in bytes, Unicode's white space trimmed.
        Assert.Equal(items, cut.Select(item => item.Text));
        Assert.Equal(spans, cut.SelectMany(item => new[] { item.Span.Start, item.Span.End }));
    }

    [Fact]
    public void TheTagsOfTheC4ToolsFile_AreReadOneByOne_ExtraAndMoreFromOneArgument()
    {
        // Arrange: the C4 tool's own file, and a binding written for this test that reads its tags statements.
        var bytes = File.ReadAllBytes(Path.Combine(Repository.Root, "src", "diagrams", "c4", "backend", "EtAlii.Adp.Diagram.C4.Tests", "Fixtures", "findings", "tags-in-block.dsl"));
        var binding = Binding(Statements, """
            [ { "name": "tags", "type": "Tags", "at": "/statement[WORD='tags']",
                "attributes": { "tags": { "node": "STRING", "match": "\"(?<value>(?:[^\"\\\\]|\\\\.)*)\"", "escape": { "with": "\\", "quote": "\"" }, "split": ",", "readOnly": true } } } ]
            """);

        // Act.
        var model = OpenBody.Open(bytes, binding, new FblOptions { FileName = "tags-in-block.dsl" }).Model;

        // Assert: each argument's items in order, the two of one argument apart.
        Assert.False(model.Unreadable);
        Assert.Equal(
            [["External"], ["Database", "Existing System"], ["Extra", "More"]],
            model.Elements.Select(element => Assert.IsAssignableFrom<IEnumerable<object?>>(element.Attributes["tags"]).Cast<string>().ToArray()).ToArray());
    }

    [Theory]
    [InlineData("a \"x, y\"\n", new[] { "x", "y", "z" }, "insert-key 7 7 ', z'", "a \"x, y, z\"\n")]
    [InlineData("a \"x, y\"\n", new[] { "y" }, "remove-key 3 6 ''", "a \"y\"\n")]
    [InlineData("a \"x ,y,  z\"\n", new[] { "x", "z" }, "remove-key 4 7 ''", "a \"x,  z\"\n")]
    [InlineData("a \"x ,y,  z\"\n", new[] { "x", "w", "z" }, "replace-value 6 7 'w'", "a \"x ,w,  z\"\n")]
    [InlineData("a \"\"\n", new[] { "x", "y" }, "replace-value 3 3 'x, y'", "a \"x, y\"\n")]
    [InlineData("a \" , \"\n", new[] { "x" }, "replace-value 3 6 'x'", "a \"x\"\n")]
    [InlineData("a \"x, y\" // kept\n", new string[0], "remove-key 1 8 ''", "a // kept\n")]
    [InlineData("a\n", new[] { "x", "y" }, "insert-key 1 1 ' \"x, y\"'", "a \"x, y\"\n")]
    [InlineData("a\n", new[] { "x\"y" }, "insert-key 1 1 ' \"x\\\"y\"'", "a \"x\\\"y\"\n")]
    [InlineData("a \"q\\\"r\"\n", new[] { "q\"r", "s\"t" }, "insert-key 7 7 ', s\\\"t'", "a \"q\\\"r, s\\\"t\"\n")]
    [InlineData("a \"v, w, x, y, z\"\n", new[] { "v", "z" }, "remove-key 4 13 ''", "a \"v, z\"\n")]
    [InlineData("a \"q\\\"r\"\n", new[] { "s\"t" }, "replace-value 3 7 's\\\"t'", "a \"s\\\"t\"\n")]
    public void ASetOfTheItems_WritesTheirDifferenceIntoTheNode_AndIsUndoneByItsBytes(string text, string[] tags, string splice, string expected)
    {
        // Arrange.
        var body = Open(text, LabelsBinding());

        // Act.
        var result = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["tags"] = tags.Cast<object?>().ToList() }));

        // Assert: one splice of that shape; the bytes it leaves; undo puts back the bytes it replaced.
        Assert.Equal(splice, Describe(Assert.Single(Assert.IsType<PlanResult.Planned>(result).Edit.Splices)));
        Assert.Equal(expected, Encoding.UTF8.GetString(body.Bytes));
        Assert.Equal(tags, Tags(body, "a"));
        Assert.IsType<UndoResult.Done>(body.Undo());
        Assert.Equal(text, Encoding.UTF8.GetString(body.Bytes));
    }

    [Fact]
    public void ANewItemInTheMiddle_IsWrittenAfterTheLastItem()
    {
        // Arrange.
        var body = Open("a \"x, z\"\n", LabelsBinding());

        // Act.
        var result = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["tags"] = new List<object?> { "x", "y", "z" } }));

        // Assert: FBL 0.13 section 6.3 adds right after the last item, so the items read back in another order.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("a \"x, z, y\"\n", Encoding.UTF8.GetString(body.Bytes));
    }

    [Fact]
    public void AnItemHoldingTheSplit_WouldNotReadBack_AndIsRefused()
    {
        // Arrange.
        var body = Open("a \"x\"\n", LabelsBinding());

        // Act: 'y,z' would read back as two items.
        var result = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["tags"] = new List<object?> { "x", "y,z" } }));

        // Assert: refused, and nothing written.
        Assert.Equal("The new tags would not read back from the text.", Assert.IsType<PlanResult.Refused>(result).Reason);
        Assert.Equal("a \"x\"\n", Encoding.UTF8.GetString(body.Bytes));
    }

    [Fact]
    public void NoItemsForAnAbsentNode_WriteNothing()
    {
        // Arrange.
        var body = Open("a\n", LabelsBinding());

        // Act.
        var result = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["tags"] = new List<object?>() }));

        // Assert: no node is created to hold no item.
        Assert.Empty(Assert.IsType<PlanResult.Planned>(result).Edit.Splices);
        Assert.Equal("a\n", Encoding.UTF8.GetString(body.Bytes));
    }

    [Fact]
    public void ASingleValuedSlotWithAnEscape_ReadsItsValueUnescaped_AndWritesItEscaped()
    {
        // Arrange: the tags slot without split, one value.
        var body = Open("a \"q\\\"r\"\n", LabelsBinding(split: null));

        // Act.
        var read = Assert.IsType<FblElement>(body.Model.Find("a")).Attributes["tags"];
        var result = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["tags"] = "s\"t" }));

        // Assert.
        Assert.Equal("q\"r", read);
        Assert.Equal("replace-value 3 7 's\\\"t'", Describe(Assert.Single(Assert.IsType<PlanResult.Planned>(result).Edit.Splices)));
        Assert.Equal("a \"s\\\"t\"\n", Encoding.UTF8.GetString(body.Bytes));
    }

    [Fact]
    public void ASeparatorOtherThanTheDefault_IsWrittenBetweenTheItems()
    {
        // Arrange: items cut at '|' and written with ' | '.
        var body = Open("a \"x|y\"\nb \"\"\n", LabelsBinding(split: "|", separator: " | "));

        // Act.
        var added = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["tags"] = new List<object?> { "x", "y", "z" } }));
        var filled = body.Change(new ModelChange.Set("b", new Dictionary<string, object?> { ["tags"] = new List<object?> { "p", "q" } }));

        // Assert.
        Assert.IsType<PlanResult.Planned>(added);
        Assert.IsType<PlanResult.Planned>(filled);
        Assert.Equal("a \"x|y | z\"\nb \"p | q\"\n", Encoding.UTF8.GetString(body.Bytes));
    }

    [Fact]
    public void AnAttributeWithoutCreate_WhoseNodeIsAbsent_IsRefused()
    {
        // Arrange.
        var body = Open("a\n", LabelsBinding(create: false));

        // Act.
        var result = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["tags"] = new List<object?> { "x" } }));

        // Assert: refused, and nothing written.
        Assert.Contains("has no tags in this file to set", Assert.IsType<PlanResult.Refused>(result).Reason, StringComparison.Ordinal);
        Assert.Equal("a\n", Encoding.UTF8.GetString(body.Bytes));
    }

    [Fact]
    public void AManySlotWithoutSplit_ReadsEachNodeAsAnItem_AndWritesNodes()
    {
        // Arrange: hashtags after a name, one node each, the attribute many by the tool type.
        const string grammar = "start: unit*\n?unit: label\n     | _NL\nlabel: NAME TAG*\nNAME: /[a-z]+/\nTAG: /#[a-z]+/\n_NL: /(\\r?\\n[ \\t]*)+/\nWHITESPACE: /[ \\t]+/\n%ignore WHITESPACE\n";
        var binding = Binding(grammar, """
            [ { "name": "label", "type": "Label", "at": "/label", "id": { "from": { "node": "NAME" } },
                "attributes": { "name": { "node": "NAME", "readOnly": true },
                                "tags": { "node": "TAG", "match": "#(?<value>[a-z]+)", "create": { "emit": " #{value}", "after": ["name"] } } } } ]
            """);
        var options = new FblOptions { FileName = "body.t", Many = (type, name) => type == "Label" && name == "tags" };
        var body = OpenBody.Open(Encoding.UTF8.GetBytes("a #x #y\nb\n"), binding, options);

        // Act.
        var read = Tags(body, "a");
        var removed = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["tags"] = new List<object?> { "y", "z" } }));
        var created = body.Change(new ModelChange.Set("b", new Dictionary<string, object?> { ["tags"] = new List<object?> { "p", "q" } }));

        // Assert: x goes with the space before it, z is written after the last node, p and q after the name.
        Assert.Equal(["x", "y"], read);
        Assert.IsType<PlanResult.Planned>(removed);
        Assert.IsType<PlanResult.Planned>(created);
        Assert.Equal("a #y #z\nb #p #q\n", Encoding.UTF8.GetString(body.Bytes));
    }

    [Fact]
    public void TheVendoredSplitHoldingAnEscapeCharacter_IsRefusedAtItsSplit_AndLoadsAsAComma()
    {
        // Arrange.
        var path = Path.Combine(Repository.Conformance, "refused", "split-escape-collision.fbl");
        var comma = File.ReadAllText(path).Replace("\"split\": \"\\\\,\"", "\"split\": \",\"", StringComparison.Ordinal);

        // Act.
        var refused = FblDocumentLoader.Load(path, out var none);
        var loaded = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(comma), null, out var document);

        // Assert.
        Assert.Equal(["/bindings/loops/elements/0/attributes/members/split"], refused.Where(problem => problem.Severity == LoadSeverity.Error).Select(problem => problem.Pointer));
        Assert.Null(none);
        Assert.DoesNotContain(loaded, problem => problem.Severity == LoadSeverity.Error);
        Assert.NotNull(document);
    }

    [Fact]
    public void TheVendoredSplitOnASingleValuedAttribute_Loads_AndIsRefusedByTheToolTypesAttributeTypes()
    {
        // Arrange: the causal loop diagram's Loop.title is single-valued.
        var problems = FblDocumentLoader.Load(Path.Combine(Repository.Conformance, "refused", "split-not-many.fbl"), out var document);
        Assert.DoesNotContain(problems, problem => problem.Severity == LoadSeverity.Error);
        var binding = Assert.IsType<FblDocument>(document).Bindings["loops"];

        // Act.
        var single = binding.CheckManyAttributes((type, name) => !(type == "Loop" && name == "title"));
        var many = binding.CheckManyAttributes((_, _) => true);

        // Assert.
        Assert.Equal(["/bindings/loops/elements/0/attributes/title/split"], single.Where(problem => problem.Severity == LoadSeverity.Error).Select(problem => problem.Pointer));
        Assert.Empty(many);
    }

    [Theory]
    [InlineData("\"split\": \",\", \"items\": { \"a\": { \"node\": \"NAME\" } }", "/split")]
    [InlineData("\"split\": \"\\\"\"", "/split")]
    [InlineData("\"split\": \",\", \"create\": { \"emit\": \" {value}\", \"after\": [\"nothing\"] }", "/create/after")]
    [InlineData("\"split\": \"\"", "/split")]
    public void ASplitTheFamilyCannotRead_IsRefusedWhereItStands(string members, string place)
    {
        // Arrange: the labels binding, the slot of its tags given these members beside its node.
        var json = BindingJson(Labels, $$"""
            [ { "name": "label", "type": "Label", "at": "/label", "id": { "from": { "node": "NAME" } },
                "attributes": { "name": { "node": "NAME", "readOnly": true },
                                "tags": { "node": "tags", "escape": { "with": "\\", "quote": "\"" }, {{members}} } } } ]
            """);

        // Act.
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), null, out var document);

        // Assert.
        Assert.Contains("/bindings/b/elements/0/attributes/tags" + place, problems.Where(problem => problem.Severity == LoadSeverity.Error).Select(problem => problem.Pointer));
        Assert.Null(document);
    }

    [Theory]
    [InlineData("\"split\": \",\"", "/split")]
    [InlineData("\"escape\": { \"with\": \"\\\\\", \"quote\": \"\\\"\" }", "/escape")]
    [InlineData("\"create\": { \"emit\": \"n: 1\", \"place\": \"last\", \"after\": [\"m\"] }", "/create/after")]
    public void ASplitAnEscapeOrACreatesAfter_InAnotherFamily_IsRefusedWhereItStands(string members, string place)
    {
        // Arrange: a yaml binding whose slot is given one of the grammar family's constructs.
        var json = $$"""{ "fbl": "0.13", "bindings": { "b": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "yaml" }, "reader": "declared", "elements": [ { "name": "item", "type": "Item", "at": "/items/*", "attributes": { "m": { "key": "m" }, "n": { "key": "n", {{members}} } } } ] } } }""";

        // Act.
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), null, out var document);

        // Assert.
        Assert.Contains("/bindings/b/elements/0/attributes/n" + place, problems.Where(problem => problem.Severity == LoadSeverity.Error).Select(problem => problem.Pointer));
        Assert.Null(document);
    }

    private static FblBinding LabelsBinding(string? split = ",", string? separator = null, bool create = true)
    {
        var extra = (split is null ? "" : $", \"split\": {JsonSerializer.Serialize(split)}") + (separator is null ? "" : $", \"separator\": {JsonSerializer.Serialize(separator)}") + (create ? ", \"create\": { \"emit\": \" \\\"{value}\\\"\", \"after\": [\"name\"] }" : "");
        return Binding(Labels, $$"""
            [ { "name": "label", "type": "Label", "at": "/label", "id": { "from": { "node": "NAME" } },
                "attributes": { "name": { "node": "NAME", "readOnly": true },
                                "tags": { "node": "tags", "match": "\"?(?<value>(?:[^\"\\\\]|\\\\.)*)\"?", "escape": { "with": "\\", "quote": "\"" }{{extra}} } } } ]
            """);
    }

    private static OpenBody Open(string text, FblBinding binding) => OpenBody.Open(Encoding.UTF8.GetBytes(text), binding, new FblOptions { FileName = "body.t" });

    private static string[] Tags(OpenBody body, string id) =>
        Assert.IsAssignableFrom<IEnumerable<object?>>(Assert.IsType<FblElement>(body.Model.Find(id)).Attributes["tags"]).Cast<string>().ToArray();

    private static string Describe(Splice splice) => $"{WireName.Of(splice.Operation)} {splice.Start} {splice.End} '{splice.Text}'";

    private static FblBinding Binding(string grammar, string elements)
    {
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(BindingJson(grammar, elements)), null, out var document);
        Assert.True(document is not null, string.Join("\n", problems));
        return document.Bindings["b"];
    }

    private static string BindingJson(string grammar, string elements) =>
        $$"""{ "fbl": "0.13", "bindings": { "b": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "grammar", "grammar": {{JsonSerializer.Serialize(grammar)}} }, "reader": "declared", "elements": {{elements}} } } }""";
}
