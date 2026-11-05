using System.Text;
using System.Text.Json;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Grammar;

/// <summary>
/// FBL 0.15's <c>requote</c> in a slot's <c>escape</c> (section 5.2, <i>The sequences of a string</i>; section 6.3, <i>A
/// string</i>): a set is a <c>replace-value</c> of the whole node the slot reaches, with the value escaped between two of
/// <c>requote</c>, so that a value written bare can be set to one that needs quotes. The shape is Structurizr's name, which
/// may be written with or without quotes (etalii.adp spec 068, findings B6 and B18).
/// </summary>
public class GrammarRequoteTests
{
    /// <summary>Items of a key and a label, the label a bare word or a quoted string.</summary>
    private const string Grammar = "start: (item | _NL)*\nitem: KEY \"=\" LABEL\nKEY: /[a-z]+/\nLABEL: /\"(?:[^\"\\\\]|\\\\[\\\\\"])*\"|[A-Z][A-Za-z]*/\n_NL: /\\r?\\n/\nWS: /[ \\t]+/\n%ignore WS\n";

    private const string Escape = """{ "with": "\\", "quote": "\"", "requote": "\"" }""";

    private const string Body = "a = Bare\nb = \"Quoted\" \n";

    [Fact]
    public void TheBinding_LoadsWithNoWarningOfItsRequote()
    {
        // Act.
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(Json(Escape)), null, out var document);

        // Assert: requote is read, so it is no construct this library warns of as one it does not read (FBL 0.17 section 15.1);
        // the one warning is the version's (section 2.7).
        Assert.NotNull(document);
        Assert.DoesNotContain(problems, problem => problem.Severity == LoadSeverity.Error || problem.Message.Contains("does not read", StringComparison.Ordinal));
    }

    [Fact]
    public void ABareValue_SetToOneThatNeedsQuotes_IsWrittenQuoted()
    {
        // Arrange.
        var body = Open(Body, Escape);

        // Act.
        var result = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["label"] = "Two \"words\"" }));

        // Assert: the whole node is replaced, its value escaped between two quotes, and it reads back as set.
        Assert.Equal(["replace-value 4 8 '\"Two \\\"words\\\"\"'"], Assert.IsType<PlanResult.Planned>(result).Edit.Splices.Select(Describe));
        Assert.Equal("a = \"Two \\\"words\\\"\"\nb = \"Quoted\" \n", Encoding.UTF8.GetString(body.Bytes));
        Assert.Equal("Two \"words\"", body.Model.Elements.Single(element => element.Id == "a").Attributes["label"]);
    }

    [Fact]
    public void ABareValue_SetToOneThatNeedsNone_IsWrittenQuotedToo()
    {
        // Arrange.
        var body = Open(Body, Escape);

        // Act.
        var result = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["label"] = "Other" }));

        // Assert: a set writes the whole node in requote, whatever the value (FBL 0.15 section 6.3).
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("a = \"Other\"\nb = \"Quoted\" \n", Encoding.UTF8.GetString(body.Bytes));
    }

    [Fact]
    public void AQuotedValue_IsSetInItsQuotes_AndWhatFollowsTheNodeStays()
    {
        // Arrange.
        var body = Open(Body, Escape);

        // Act.
        var result = body.Change(new ModelChange.Set("b", new Dictionary<string, object?> { ["label"] = "New" }));

        // Assert.
        Assert.Equal(["replace-value 13 21 '\"New\"'"], Assert.IsType<PlanResult.Planned>(result).Edit.Splices.Select(Describe));
        Assert.Equal("a = Bare\nb = \"New\" \n", Encoding.UTF8.GetString(body.Bytes));
    }

    [Fact]
    public void WithoutRequote_ABareValueThatNeedsQuotes_IsRefused()
    {
        // Arrange: the same binding without requote writes the value's span, which keeps a bare value bare.
        var body = Open(Body, """{ "with": "\\", "quote": "\"" }""");

        // Act.
        var result = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["label"] = "Two words" }));

        // Assert.
        Assert.Contains("would not read back", Assert.IsType<PlanResult.Refused>(result).Reason, StringComparison.Ordinal);
        Assert.Equal(Body, Encoding.UTF8.GetString(body.Bytes));
    }

    [Fact]
    public void ARequoteOfMoreThanOneCharacter_IsRefusedWhenTheBindingIsLoaded()
    {
        // Act.
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(Json("""{ "with": "\\", "quote": "\"", "requote": "''" }""")), null, out _);

        // Assert.
        Assert.Contains(problems, problem => problem is { Severity: LoadSeverity.Error, Pointer: "/bindings/b/elements/0/attributes/label/escape" });
    }

    private static string Json(string escape) =>
        $$"""{ "fbl": "0.15", "bindings": { "b": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "grammar", "grammar": {{JsonSerializer.Serialize(Grammar)}} }, "reader": "declared", "elements": [ { "name": "item", "type": "Item", "at": "/item", "id": { "from": { "node": "KEY" } }, "attributes": { "label": { "node": "LABEL", "match": "\"?(?<value>(?:[^\"\\\\]|\\\\.)*)\"?", "escape": {{escape}} } } } ] } } }""";

    private static OpenBody Open(string text, string escape)
    {
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(Json(escape)), null, out var document);
        Assert.True(document is not null, string.Join("\n", problems));
        return OpenBody.Open(Encoding.UTF8.GetBytes(text), document.Bindings["b"], new FblOptions { FileName = "body.t" });
    }

    private static string Describe(Splice splice) => $"{WireName.Of(splice.Operation)} {splice.Start} {splice.End} '{splice.Text}'";
}
