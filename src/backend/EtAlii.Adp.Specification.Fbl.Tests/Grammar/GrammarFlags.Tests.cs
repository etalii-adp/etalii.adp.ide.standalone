using System.Text;
using System.Text.Json;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Grammar;

/// <summary>
/// FBL 0.18's <c>holds</c> (etalii.adp spec 071, Peter's card ruling of 2026-11-04): a <c>{node}</c> slot reads one value
/// of a segment as a bool, true when a node it reaches holds that text in any case of its ASCII letters (section 4.9); a set
/// of true writes one node after the last the slot reaches, a set of false removes every node that holds it, each with the
/// whitespace before it, and a set of the value it reads writes nothing (section 6.3).
/// </summary>
public class GrammarFlagsTests
{
    /// <summary>Named items, one per line, each followed by marks in parentheses, as a Wardley map writes its decorators.</summary>
    private const string Items = "start: unit*\n?unit: item\n     | _NL\nitem: NAME mark*\nmark: \"(\" WORD \")\"\nNAME: /[A-Z][a-z]*/\nWORD: /[A-Za-z]+/\n_NL: /(\\r?\\n[ \\t]*)+/\nWHITESPACE: /[ \\t]+/\n%ignore WHITESPACE\n";

    [Fact]
    public void AFlag_IsTrue_WhenANodeHoldsItsText_InAnyCase_AndFalseOtherwise()
    {
        // Act.
        var body = Open("A (Market) (other)\nB\nC (build) (build)\n");

        // Assert: (Market) holds market, an unknown mark holds neither, and no mark is false, never absent.
        Assert.Equal((true, false), Flags(body, "A"));
        Assert.Equal((false, false), Flags(body, "B"));
        Assert.Equal((false, true), Flags(body, "C"));
        Assert.Equal(new Span(2, 10), Assert.IsType<FblElement>(body.Model.Find("A")).ValueSpans["market"]);
        Assert.Equal(new Span(23, 30), Assert.IsType<FblElement>(body.Model.Find("C")).ValueSpans["build"]);
    }

    [Theory]
    [InlineData("A (other)\n", "A", "build", true, "insert-key 9 9 ' (build)'", "A (other) (build)\n")]
    [InlineData("B\n", "B", "market", true, "insert-key 1 1 ' (market)'", "B (market)\n")]
    [InlineData("A (Market) (other)\n", "A", "market", false, "remove-key 1 10 ''", "A (other)\n")]
    public void ASetOfAFlag_WritesOrRemovesOneNode_AndIsUndoneByItsBytes(string text, string id, string flag, bool value, string splice, string expected)
    {
        // Arrange.
        var body = Open(text);

        // Act.
        var result = body.Change(new ModelChange.Set(id, new Dictionary<string, object?> { [flag] = value }));

        // Assert: one splice of that shape; the bytes it leaves read the flag as set; undo puts the bytes back.
        Assert.Equal(splice, Describe(Assert.Single(Assert.IsType<PlanResult.Planned>(result).Edit.Splices)));
        Assert.Equal(expected, Encoding.UTF8.GetString(body.Bytes));
        Assert.Equal(value, Assert.IsType<FblElement>(body.Model.Find(id)).Attributes[flag]);
        Assert.IsType<UndoResult.Done>(body.Undo());
        Assert.Equal(text, Encoding.UTF8.GetString(body.Bytes));
    }

    [Fact]
    public void AFlagClearedThatIsWrittenTwice_RemovesBoth()
    {
        // Arrange.
        var body = Open("C (build) (build)\n");

        // Act.
        var result = body.Change(new ModelChange.Set("C", new Dictionary<string, object?> { ["build"] = false }));

        // Assert: both go, each with the space before it, in one edit.
        Assert.Equal(["remove-key 1 9 ''", "remove-key 9 17 ''"], Assert.IsType<PlanResult.Planned>(result).Edit.Splices.Select(Describe));
        Assert.Equal("C\n", Encoding.UTF8.GetString(body.Bytes));
        Assert.IsType<UndoResult.Done>(body.Undo());
        Assert.Equal("C (build) (build)\n", Encoding.UTF8.GetString(body.Bytes));
    }

    [Fact]
    public void OneFlagClearedAndAnotherSet_InOneEdit_RemovesTheOneAndWritesTheOtherAfterTheLastNode()
    {
        // Arrange.
        var body = Open("A (Market) (other)\n");

        // Act.
        var result = body.Change(new ModelChange.Set("A", new Dictionary<string, object?> { ["market"] = false, ["build"] = true }));

        // Assert.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("A (other) (build)\n", Encoding.UTF8.GetString(body.Bytes));
        Assert.Equal((false, true), Flags(body, "A"));
    }

    [Fact]
    public void AFlagSetToTheValueItReads_WritesNothing()
    {
        // Arrange.
        var body = Open("A (Market)\n");

        // Act.
        body.Change(new ModelChange.Set("A", new Dictionary<string, object?> { ["market"] = true, ["build"] = false }));

        // Assert: (Market) is kept as written.
        Assert.Equal("A (Market)\n", Encoding.UTF8.GetString(body.Bytes));
    }

    [Fact]
    public void AWritableFlagWithoutCreate_IsRefusedWhenLoaded()
    {
        // Act.
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(BindingJson("""
            [ { "name": "item", "type": "Item", "at": "/item", "id": { "from": { "node": "NAME" } },
                "attributes": { "market": { "node": "mark", "holds": "market" } } } ]
            """)), null, out var document);

        // Assert: FBL 0.18 section 14.1, step 6.
        Assert.Null(document);
        Assert.Contains(problems, problem => problem.Severity == LoadSeverity.Error && problem.Pointer.EndsWith("/attributes/market/holds", StringComparison.Ordinal));
    }

    [Fact]
    public void TheVendoredFlagOnATextAttribute_Loads_AndIsRefusedByTheToolTypesAttributeTypes()
    {
        // Arrange: the causal loop diagram's Loop.title is a string.
        var problems = FblDocumentLoader.Load(Path.Combine(Repository.Conformance, "refused", "holds-not-bool.fbl"), out var document);
        Assert.DoesNotContain(problems, problem => problem.Severity == LoadSeverity.Error);
        var binding = Assert.IsType<FblDocument>(document).Bindings["loops"];

        // Act.
        var text = binding.CheckFlagAttributes((type, name) => !(type == "Loop" && name == "title"));
        var flag = binding.CheckFlagAttributes((_, _) => true);

        // Assert: FBL 0.18 section 14.1, step 6.
        Assert.Equal(["/bindings/loops/elements/0/attributes/title/holds"], text.Where(problem => problem.Severity == LoadSeverity.Error).Select(problem => problem.Pointer));
        Assert.Empty(flag);
    }

    [Fact]
    public void TheVendoredWritableFlagWithoutCreate_IsRefusedWhenLoaded()
    {
        // Act.
        var problems = FblDocumentLoader.Load(Path.Combine(Repository.Conformance, "refused", "holds-without-create.fbl"), out var document);

        // Assert: FBL 0.18 section 14.1, step 6.
        Assert.Null(document);
        Assert.Contains(problems, problem => problem.Severity == LoadSeverity.Error && problem.Pointer.EndsWith("/holds", StringComparison.Ordinal));
    }

    private static (bool Market, bool Build) Flags(OpenBody body, string id)
    {
        var element = Assert.IsType<FblElement>(body.Model.Find(id));
        return (Assert.IsType<bool>(element.Attributes["market"]), Assert.IsType<bool>(element.Attributes["build"]));
    }

    private static OpenBody Open(string text)
    {
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(BindingJson($$"""
            [ { "name": "item", "type": "Item", "at": "/item", "id": { "from": { "node": "NAME" } },
                "attributes": {
                  "name": { "node": "NAME", "readOnly": true },
                  {{Flag("market")}},
                  {{Flag("build")}} } } ]
            """)), null, out var document);
        Assert.True(document is not null, string.Join("\n", problems));
        return OpenBody.Open(Encoding.UTF8.GetBytes(text), document.Bindings["b"], new FblOptions { FileName = "body.t" });
    }

    private static string Flag(string name) =>
        $$"""
        "{{name}}": { "node": "mark", "match": "\\([ \\t]*(?<value>[^ \\t)]+)[ \\t]*\\)", "holds": "{{name}}", "create": { "emit": " ({value})", "after": ["name"] } }
        """;

    private static string Describe(Splice splice) => $"{WireName.Of(splice.Operation)} {splice.Start} {splice.End} '{splice.Text}'";

    private static string BindingJson(string elements) =>
        $$"""{ "fbl": "0.18", "bindings": { "b": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "grammar", "grammar": {{JsonSerializer.Serialize(Items)}} }, "reader": "declared", "elements": {{elements}} } } }""";
}
