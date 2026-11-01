using System.Text;
using System.Text.Json;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Grammar;

/// <summary>
/// Writing a body of FBL 0.9's grammar family (FBL §4.9 <i>Writing</i>, §6.3; etalii.adp spec 072, Requirement 6): a set
/// replaces the value's span, an add writes the rule's <c>emit</c> where <c>insert.place</c> says, a removal takes the
/// entry's line span, a rename rewrites its references, nothing is written that would not read back, and every edit has
/// its exact undo and redo, refused on drift.
/// </summary>
public class GrammarWritingTests
{
    /// <summary>Items of a key, a value and an optional tag, and notes, one or more to a line, with comments.</summary>
    private const string Items = "start: (item | note | _NL)*\nitem: KEY \"=\" VALUE [TAG]\nnote: \"!\" KEY\nKEY: /[a-z]+/\nVALUE: /[a-z0-9.]+/\nTAG: /@[a-z]*/\nCOMMENT: /#[^\\r\\n]*/\n_NL: /\\r?\\n/\nWS: /[ \\t]+/\n%ignore WS\n%ignore COMMENT\n";

    [Fact]
    public void ASet_ReplacesTheValuesSpan_AndKeepsEveryOtherByte()
    {
        // Arrange.
        var body = Open("a = 1  # one\nb = 2\n", "after-last");

        // Act.
        var result = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["value"] = "7" }));

        // Assert.
        var splice = Assert.Single(Assert.IsType<PlanResult.Planned>(result).Edit.Splices);
        Assert.Equal(new Splice(SpliceOperation.ReplaceValue, 4, 5, "7"), splice);
        Assert.Equal("a = 7  # one\nb = 2\n", Text(body));
    }

    [Theory]
    [InlineData(2.50, "2.5")]
    [InlineData(1e21, "1e+21")]
    [InlineData(true, "true")]
    public void ANumberOrABoolean_IsWrittenAsFblWritesIt(object value, string written)
    {
        // Arrange.
        var body = Open("a = 1\n", "after-last", valuePattern: "/[a-z0-9.+]+/");

        // Act.
        var result = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["value"] = value }));

        // Assert.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal($"a = {written}\n", Text(body));
    }

    [Theory]
    [InlineData("after-last", "a = 1\n!x\nb = 2\nc = 9\n!y\n")]
    [InlineData("end", "a = 1\n!x\nb = 2\n!y\nc = 9\n")]
    [InlineData("start", "c = 9\na = 1\n!x\nb = 2\n!y\n")]
    [InlineData("end-of-document", "a = 1\n!x\nb = 2\n!y\nc = 9\n")]
    public void AnAdd_GoesWhereItsPlaceSays(string place, string expected)
    {
        // Arrange.
        var body = Open("a = 1\n!x\nb = 2\n!y\n", place);

        // Act.
        var result = body.Change(Add("c", "9"));

        // Assert: one insert-entry of the emit, on a line of its own.
        Assert.Equal(SpliceOperation.InsertEntry, Assert.Single(Assert.IsType<PlanResult.Planned>(result).Edit.Splices).Operation);
        Assert.Equal(expected, Text(body));
    }

    [Fact]
    public void AnAddAfterAnEntry_GoesRightAfterIt()
    {
        // Arrange: next-sibling, the entry the gesture was made on being 'a'.
        var body = Open("a = 1\n!x\nb = 2\n", "next-sibling");

        // Act.
        var result = body.Change(new ModelChange.AddAfter("Item", new Dictionary<string, object?> { ["key"] = "c", ["value"] = "9" }, "a"));

        // Assert.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("a = 1\nc = 9\n!x\nb = 2\n", Text(body));
    }

    [Theory]
    [InlineData("after-last", "a = 1\nb = 2", "a = 1\nb = 2\nc = 9")]
    [InlineData("end-of-document", "a = 1\n!x", "a = 1\n!x\nc = 9")]
    public void ABodyWithoutAFinalLineEnding_KeepsLackingOne(string place, string text, string expected)
    {
        // Arrange.
        var body = Open(text, place);

        // Act.
        var result = body.Change(Add("c", "9"));

        // Assert.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal(expected, Text(body));
    }

    [Fact]
    public void AnAddAfterAnEntryThatDoesNotEndItsLine_IsWrittenAfterItsOwnSpan()
    {
        // Arrange: 'b' shares its line with 'a', so it has no line span.
        var body = Open("a = 1 b = 2\n", "after-last");

        // Act.
        var result = body.Change(Add("c", "9"));

        // Assert: after a line ending and the line's indentation, ending with nothing.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("a = 1 b = 2\nc = 9\n", Text(body));
    }

    [Fact]
    public void InACrlfBody_EachLineFeedOfTheEmitIsTheBodysLineEndingAndTheEntrysIndentation()
    {
        // Arrange: an emit of two lines, the second a comment.
        var body = Open("  a = 1\r\n  b = 2\r\n", "after-last", emit: "{key} = {value}\n# {key} added");

        // Act.
        var result = body.Change(Add("c", "9"));

        // Assert.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("  a = 1\r\n  b = 2\r\n  c = 9\r\n  # c added\r\n", Text(body));
    }

    [Fact]
    public void ARemoval_TakesTheLineSpanWithItsLeadingComment()
    {
        // Arrange.
        var body = Open("# the first\na = 1\nb = 2\n", "after-last");

        // Act.
        var result = body.Change(new ModelChange.Remove("a"));

        // Assert.
        Assert.Equal(new Splice(SpliceOperation.RemoveEntry, 0, 18, ""), Assert.Single(Assert.IsType<PlanResult.Planned>(result).Edit.Splices));
        Assert.Equal("b = 2\n", Text(body));
    }

    [Fact]
    public void ARemovalOfAnEntryThatDoesNotStartItsLine_TakesItsOwnSpan()
    {
        // Arrange.
        var body = Open("a = 1 b = 2\n", "after-last");

        // Act.
        var result = body.Change(new ModelChange.Remove("b"));

        // Assert.
        Assert.Equal(new Splice(SpliceOperation.RemoveEntry, 6, 11, ""), Assert.Single(Assert.IsType<PlanResult.Planned>(result).Edit.Splices));
    }

    [Fact]
    public void ASetOfAnAttributeWhoseNodeIsAbsent_IsRefused()
    {
        // Arrange: 'a' has no tag.
        var body = Open("a = 1\n", "after-last");

        // Act.
        var result = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["tag"] = "@x" }));

        // Assert: the family writes no part an entry lacks (FBL 0.9 4.9).
        Assert.Contains("has no tag in this file to set", Assert.IsType<PlanResult.Refused>(result).Reason, StringComparison.Ordinal);
        Assert.Equal("a = 1\n", Text(body));
    }

    [Fact]
    public void AnAttributeWhoseNodeIsPresent_IsNeverRemoved()
    {
        // Arrange.
        var body = Open("a = 1 @t\n", "after-last");

        // Act: emptying the tag would leave no tag node, which a set does not do.
        var result = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["tag"] = "" }));

        // Assert.
        Assert.IsType<PlanResult.Refused>(result);
        Assert.Equal("a = 1 @t\n", Text(body));
    }

    [Fact]
    public void EmptyingAnAttributeWhosePartIsRemovedWhenEmpty_RemovesThePartWithTheWhitespaceBeforeIt()
    {
        // Arrange: a tag that empty: "remove" takes away, after a value and before a comment.
        var body = Open("a = 1  @t # note\nb = 2\n", "after-last", tag: """{ "node": "TAG", "empty": "remove" }""");

        // Act.
        var result = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["tag"] = "" }));

        // Assert: one remove-key of the part and the whitespace between it and the token before it (FBL 0.11 4.9).
        var splice = Assert.Single(Assert.IsType<PlanResult.Planned>(result).Edit.Splices);
        Assert.Equal((SpliceOperation.RemoveKey, 5, 9), (splice.Operation, splice.Start, splice.End));
        Assert.Equal("a = 1 # note\nb = 2\n", Text(body));
        Assert.False(body.Model.Elements[0].Attributes.ContainsKey("tag"));
    }

    [Fact]
    public void APartsCreate_WritesItsEmitAsText_ItsBracketsIncluded()
    {
        // Arrange: a tag written in brackets, which 'a' lacks.
        var body = Open("a = 1\n", "after-last", tag: """{ "node": "TAG", "match": "\\[(?<value>[a-z]*)\\]", "create": { "emit": " [{value}]", "after": ["value"] } }""", tagPattern: @"/\[[a-z]*\]/");

        // Act.
        var result = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["tag"] = "x" }));

        // Assert: a create's emit is its text with {value} replaced (FBL 0.11 5.2), not an insert's emit with optional segments.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("a = 1 [x]\n", Text(body));
    }

    [Fact]
    public void ASetThatWouldNotReadBack_IsRefusedAndWritesNothing()
    {
        // Arrange.
        var body = Open("a = 1\nb = 2\n", "after-last");

        // Act: a value with a space would read as a value and a key.
        var result = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["value"] = "x y" }));

        // Assert.
        Assert.Equal("The new value would not read back from the text.", Assert.IsType<PlanResult.Refused>(result).Reason);
        Assert.Equal("a = 1\nb = 2\n", Text(body));
    }

    [Fact]
    public void AnAddThatWouldNotReadBack_IsRefusedAndWritesNothing()
    {
        // Arrange.
        var body = Open("a = 1\n", "after-last");

        // Act.
        var result = body.Change(Add("c", "x y"));

        // Assert.
        Assert.Equal("The new Item would not read back from the text.", Assert.IsType<PlanResult.Refused>(result).Reason);
        Assert.Equal("a = 1\n", Text(body));
    }

    [Fact]
    public void ARename_RewritesTheReferencesToIt()
    {
        // Arrange.
        const string grammar = "start: (node | link | _NL)*\nnode: \"node\" NAME _NL\nlink: NAME \"->\" target _NL\ntarget: NAME\nNAME: /[a-z]+/\n_NL: /\\n/\nWS: /[ \\t]+/\n%ignore WS\n";
        var binding = Binding(grammar,
            """[ { "name": "node", "type": "Node", "at": "/node", "id": { "from": { "node": "NAME" } }, "attributes": { "name": { "node": "NAME" } } } ]""",
            """[ { "name": "link", "type": "Link", "at": "/link", "source": { "node": "NAME", "reference": { "to": ["node"] } }, "target": { "node": "target", "reference": { "to": ["node"] } } } ]""");
        var body = OpenBody.Open("node a\nnode b\na -> b\nb -> a\n"u8.ToArray(), binding, new FblOptions { FileName = "body.t" });

        // Act.
        var result = body.Change(new ModelChange.Set("a", new Dictionary<string, object?> { ["name"] = "c" }));

        // Assert.
        var splices = Assert.IsType<PlanResult.Planned>(result).Edit.Splices;
        Assert.Equal(2, splices.Count(s => s.Operation == SpliceOperation.RewriteReference));
        Assert.Equal("node c\nnode b\nc -> b\nb -> c\n", Text(body));
    }

    [Fact]
    public void AnEdit_IsUndoneAndRedoneByItsBytes_AndRefusedOnDrift()
    {
        // Arrange.
        const string text = "a = 1\nb = 2\n";
        var body = Open(text, "after-last");
        Assert.IsType<PlanResult.Planned>(body.Change(Add("c", "9")));
        var added = body.Bytes.ToArray();
        var drifted = "a = 1\nb = 3\nc = 9\n"u8.ToArray();

        // Act and assert: undo refused on drift, then done; redo refused on drift, then done.
        Assert.Equal(SplicedFile.DriftUndo, Assert.IsType<UndoResult.Refused>(body.Undo(drifted)).Reason);
        Assert.IsType<UndoResult.Done>(body.Undo());
        Assert.Equal(text, Text(body));
        Assert.Equal(SplicedFile.DriftRedo, Assert.IsType<UndoResult.Refused>(body.Redo("a = 0\nb = 2\n"u8.ToArray())).Reason);
        Assert.IsType<UndoResult.Done>(body.Redo());
        Assert.Equal(added, body.Bytes);
    }

    private static ModelChange.Add Add(string key, string value) =>
        new("Item", key, new Dictionary<string, object?> { ["key"] = key, ["value"] = value });

    private static OpenBody Open(string text, string place, string emit = "{key} = {value}", string valuePattern = "/[a-z0-9.]+/", string tag = """{ "node": "TAG" }""", string tagPattern = "/@[a-z]*/")
    {
        var grammar = Items.Replace("VALUE: /[a-z0-9.]+/", "VALUE: " + valuePattern, StringComparison.Ordinal).Replace("TAG: /@[a-z]*/", "TAG: " + tagPattern, StringComparison.Ordinal);
        var elements = $$"""
            [ { "name": "item", "type": "Item", "at": "/item", "id": { "from": { "node": "KEY" } },
                "attributes": { "key": { "node": "KEY" }, "value": { "node": "VALUE" }, "tag": {{tag}} },
                "insert": { "place": "{{place}}", "emit": {{JsonSerializer.Serialize(emit)}} }, "remove": {} },
              { "name": "note", "type": "Note", "at": "/note", "id": { "from": { "node": "KEY" } } } ]
            """;
        return OpenBody.Open(Encoding.UTF8.GetBytes(text), Binding(grammar, elements), new FblOptions { FileName = "body.t" });
    }

    private static FblBinding Binding(string grammar, string elements, string relations = "[]")
    {
        var json = $$"""{ "fbl": "0.9", "bindings": { "b": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "grammar", "grammar": {{JsonSerializer.Serialize(grammar)}} }, "reader": "declared", "elements": {{elements}}, "relations": {{relations}} } } }""";
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), null, out var document);
        Assert.True(document is not null, string.Join("\n", problems));
        return document.Bindings["b"];
    }

    private static string Text(OpenBody body) => Encoding.UTF8.GetString(body.Bytes);
}
