using System.Text;
using System.Text.Json;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Grammar;

/// <summary>
/// FBL 0.15's further slots and its value above the entry in the grammar family (section 4.9, <i>Further slots</i> and
/// <i>A value above the entry</i>; section 5.2, <c>or</c>): an attribute is read from the first of its slots that is present,
/// and is written as that slot writes, and a <c>{node}</c> slot reaches the node that holds the entry with <c>..</c>. The
/// shape is Structurizr's relationship written without a source in its element's block (etalii.adp spec 068, finding B8).
/// </summary>
public class GrammarFurtherSlotsTests
{
    /// <summary>Things, each with an optional block of links; a link names its source, or leaves it to the thing whose block holds it.</summary>
    private const string Grammar = "start: _item*\n_item: thing _NL\n     | _NL\nthing: NAME [_block]\n_block: \"{\" _NL _inner* \"}\"\n_inner: link _NL\n      | _NL\nlink: [source] \"->\" NAME\nsource: NAME\nNAME: /[a-z]+/\n_NL: /(\\r?\\n[ \\t]*)+/\nWS: /[ \\t]+/\n%ignore WS\n";

    private const string Elements = """[ { "name": "thing", "type": "Thing", "at": "/thing", "id": { "from": { "node": "NAME" } } }, { "name": "link", "type": "Link", "at": "/thing/link", "attributes": { "from": { "node": "source", "or": [ { "node": "../NAME" } ], "reference": { "to": [ "thing" ], "by": "id" } }, "to": { "node": "NAME", "reference": { "to": [ "thing" ], "by": "id" } } } } ]""";

    private const string Body = "a {\n  -> b\n  c -> b\n}\nb\nc\n";

    [Fact]
    public void TheBinding_LoadsWithNoWarningOfItsFurtherSlot()
    {
        // Act.
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(Json()), null, out var document);

        // Assert: 'or' and '..' are read, so no construct is warned of as one this library does not read (FBL 0.17 section 15.1),
        // and the selector '..' is no error; the one warning is the version's (section 2.7).
        Assert.NotNull(document);
        Assert.DoesNotContain(problems, problem => problem.Severity == LoadSeverity.Error || problem.Message.Contains("does not read", StringComparison.Ordinal));
    }

    [Fact]
    public void ALinkWithoutASource_IsReadFromTheThingWhoseBlockHoldsIt_AndOneWithASourceFromItsOwn()
    {
        // Act.
        var model = Open(Body).Model;

        // Assert.
        Assert.Equal(["a -> b", "c -> b"], Links(model));
    }

    [Fact]
    public void AFurtherSlotAboveTheEntry_ReachesNothingFromTheTop()
    {
        // Arrange: a thing's own name read from the node above it, which is the root.
        var elements = Elements.Replace("\"id\": { \"from\": { \"node\": \"NAME\" } } }, {", "\"id\": { \"from\": { \"node\": \"NAME\" } }, \"attributes\": { \"above\": { \"node\": \"../NAME\" } } }, {", StringComparison.Ordinal);
        Assert.NotEqual(Elements, elements);

        // Act.
        var model = OpenBody.Open(Encoding.UTF8.GetBytes(Body), Binding(elements), new FblOptions { FileName = "body.t" }).Model;

        // Assert.
        Assert.All(model.Elements.Where(element => element.Type == "Thing"), thing => Assert.False(thing.Attributes.ContainsKey("above")));
    }

    [Fact]
    public void ASetOfTheValueAFurtherSlotRead_IsWrittenWhereItWasRead()
    {
        // Arrange.
        var body = Open(Body);
        var link = body.Model.Elements.First(element => element.Type == "Link");

        // Act: the source of the link in a's block is set, which that link reads from a's name (FBL 0.15 section 4.9).
        var same = body.Change(new ModelChange.Set(link.Id, new Dictionary<string, object?> { ["from"] = "a" }));
        var other = body.Change(new ModelChange.Set(link.Id, new Dictionary<string, object?> { ["from"] = "d" }));

        // Assert: both are planned, where a link whose source is not read has no slot for it and refuses the set; the value it
        // reads is written as it stands, and another value is written in the slot it was read from.
        Assert.Equal(["replace-value 0 1 'a'"], Assert.IsType<PlanResult.Planned>(same).Edit.Splices.Select(Describe));
        Assert.Equal("replace-value 0 1 'd'", Describe(Assert.Single(Assert.IsType<PlanResult.Planned>(other).Edit.Splices)));
        Assert.Equal("d {\n  -> b\n  c -> b\n}\nb\nc\n", Encoding.UTF8.GetString(body.Bytes));
    }

    [Fact]
    public void ARenameOfTheThing_WritesItsNameOnce_AndRewritesTheLinksThatNameIt()
    {
        // Arrange.
        var body = Open(Body);

        // Act.
        var result = body.Change(new ModelChange.Set("c", new Dictionary<string, object?> { ["id"] = "e" }));
        var outer = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["id"] = "f" }));

        // Assert: the link in a's block reads a's name, so the rename's replace is the rewrite of its source too.
        Assert.Equal(["rewrite-reference 13 14 'e'", "replace-value 24 25 'e'"], Assert.IsType<PlanResult.Planned>(result).Edit.Splices.Select(Describe));
        Assert.Equal(["replace-value 0 1 'f'"], Assert.IsType<PlanResult.Planned>(outer).Edit.Splices.Select(Describe));
        Assert.Equal("f {\n  -> b\n  e -> b\n}\nb\ne\n", Encoding.UTF8.GetString(body.Bytes));
        Assert.Equal(["f -> b", "e -> b"], Links(body.Model));
    }

    private static string[] Links(FblModel model) =>
        [.. model.Elements.Where(element => element.Type == "Link").Select(link => $"{link.Attributes.GetValueOrDefault("from")} -> {link.Attributes.GetValueOrDefault("to")}")];

    private static string Json(string elements = Elements) =>
        $$"""{ "fbl": "0.15", "bindings": { "b": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "grammar", "grammar": {{JsonSerializer.Serialize(Grammar)}} }, "reader": "declared", "elements": {{elements}} } } }""";

    private static FblBinding Binding(string elements)
    {
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(Json(elements)), null, out var document);
        Assert.True(document is not null, string.Join("\n", problems));
        return document.Bindings["b"];
    }

    private static OpenBody Open(string text) => OpenBody.Open(Encoding.UTF8.GetBytes(text), Binding(Elements), new FblOptions { FileName = "body.t" });

    private static string Describe(Splice splice) => $"{WireName.Of(splice.Operation)} {splice.Start} {splice.End} '{splice.Text}'";
}
