using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Planning;

/// <summary>
/// Writing the words of a lines-family statement (FBL §4.6, §6.1): a list is written as its words, and a
/// value removed takes the word it was read from with it, so the line reads back as the model says.
/// </summary>
public class LinesWordsTests
{
    private const string Binding = """
        { "fbl": "0.2", "bindings": { "t": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "lines" }, "reader": "declared",
          "elements": [
            { "name": "item", "type": "Item", "line": "^item\\s+(?<id>\\S+)(?<tail>(?:\\s+\\S+)*)\\s*$", "id": { "from": { "group": "id" } },
              "attributes": { "size": { "group": "tail", "word": "^size=(?<value>.+)$", "number": "shortest", "empty": "remove" },
                              "label": { "group": "tail", "word": "^[^=]+$" } },
              "insert": { "place": "after-last", "emit": "item {id}[ size={size}][ {label}]" } },
            { "name": "flags", "type": "Flags", "line": "^flags\\s+(?<id>\\S+)(?<tail>(?:\\s+\\S+)*)\\s*$", "id": { "from": { "group": "id" } },
              "attributes": { "one": { "group": "tail", "word": "^one$", "flag": true }, "two": { "group": "tail", "word": "^two$", "flag": true },
                              "three": { "group": "tail", "word": "^\".*\"$" } },
              "insert": { "place": "after-last", "emit": "flags {id}[ {one}][ {two}][ \"{three}\"]" } },
            { "name": "note", "type": "Note", "line": "^note\\s+(?<id>\\S+)(?:\\s+(?<text>\"[^\"]*\"|\\S+))?\\s*$", "id": { "from": { "group": "id" } },
              "attributes": { "text": { "group": "text", "word": "^.+$" } },
              "insert": { "place": "after-last", "emit": "note {id}[ \"{text}\"]" } },
            { "name": "label", "type": "Label", "line": "^label\\s+(?<id>[^\\s\"]+)(?:\\s+(?<text>\"[^\"]*\"|[^\\s\"]+))?(?<rest>.*)$", "id": { "from": { "group": "id" } },
              "attributes": { "text": { "group": "text", "word": "^.+$", "empty": "remove" } },
              "insert": { "place": "after-last", "emit": "label {id}[ \"{text}\"]" } },
            { "name": "group", "type": "Group", "line": "^group\\s+(?<id>\\S+)(?<members>(?:\\s+\\S+)+)\\s*$", "id": { "from": { "group": "id" } },
              "attributes": { "members": { "group": "members" } },
              "insert": { "place": "after-last", "emit": "group {id} {members}" } }
          ] } } }
        """;

    private static OpenBody Open(string text)
    {
        Assert.Empty(FblDocumentLoader.Load(Encoding.UTF8.GetBytes(Binding), out var document));
        return OpenBody.Open(Encoding.UTF8.GetBytes(text), document!.Bindings["t"]);
    }

    private static string Changed(string text, ModelChange change)
    {
        var body = Open(text);
        var result = body.Change(change);
        Assert.IsNotType<PlanResult.Refused>(result);
        return Encoding.UTF8.GetString(body.Bytes);
    }

    [Fact]
    public void AListSet_IsWrittenAsItsWords_AfterTheSpacingTheGroupHad()
    {
        // Act.
        var text = Changed("item a\ngroup g   a b\n", new ModelChange.Set("g", new Dictionary<string, object?> { ["members"] = new List<object?> { "b", "a", "c" } }));

        // Assert.
        Assert.Equal("item a\ngroup g   b a c\n", text);
    }

    [Fact]
    public void AListSetToTheWordsItHolds_KeepsItsSpacing()
    {
        // Act.
        var text = Changed("group g a  b\n", new ModelChange.Set("g", new Dictionary<string, object?> { ["members"] = new List<object?> { "a", "b" } }));

        // Assert.
        Assert.Equal("group g a  b\n", text);
    }

    [Fact]
    public void AListAdded_IsWrittenAsItsWords()
    {
        // Act.
        var text = Changed("group g a\n", new ModelChange.Add("Group", "h", new Dictionary<string, object?> { ["members"] = new List<object?> { "x", "y" } }));

        // Assert.
        Assert.Equal("group g a\ngroup h x y\n", text);
    }

    [Fact]
    public void ABareWordGivenAValueWithASpace_IsQuoted_SoItReadsBackAsOneWord()
    {
        // Act.
        var replaced = Changed("note n plain\n", new ModelChange.Set("n", new Dictionary<string, object?> { ["text"] = "two words" }));
        var inserted = Changed("item a\n", new ModelChange.Set("a", new Dictionary<string, object?> { ["label"] = "two words" }));
        var added = Changed("item a\n", new ModelChange.Add("Item", "b", new Dictionary<string, object?> { ["label"] = "two words" }));
        var kept = Changed("note n plain\n", new ModelChange.Add("Note", "m", new Dictionary<string, object?> { ["text"] = "two words" }));

        // Assert: an emit that quotes the placeholder itself is not quoted twice.
        Assert.Equal("note n \"two words\"\n", replaced);
        Assert.Equal("item a \"two words\"\n", inserted);
        Assert.Equal("item a\nitem b \"two words\"\n", added);
        Assert.Equal("note n plain\nnote m \"two words\"\n", kept);
    }

    [Theory]
    [InlineData("flags f one \"x\" extra\n", "two", "flags f one two \"x\" extra\n")]
    [InlineData("flags f \"x\" extra\n", "one", "flags f one \"x\" extra\n")]
    [InlineData("flags f two extra\n", "one", "flags f one two extra\n")]
    public void AnAbsentValue_IsInsertedAfterTheValueBeforeIt_KeepingTheWordsAfterIt(string text, string flag, string expected)
    {
        // Act: FBL §6.3 prefers insert-key to re-emitting the line, which would lose the word no rule binds.
        var changed = Changed(text, new ModelChange.Set("f", new Dictionary<string, object?> { [flag] = true }));

        // Assert.
        Assert.Equal(expected, changed);
    }

    [Fact]
    public void AnAbsentValue_WhoseNeighboursStandOutOfTheirOrder_ReEmitsTheLine()
    {
        // Act: the quoted word stands before the flag the emit puts first, so no insertion keeps the emit's order.
        var changed = Changed("flags f \"x\" one\n", new ModelChange.Set("f", new Dictionary<string, object?> { ["two"] = true }));

        // Assert.
        Assert.Equal("flags f one two \"x\"\n", changed);
    }

    [Fact]
    public void ANumberSetToTheValueItHolds_KeepsItsSpelling()
    {
        // Act.
        var text = Changed("item a size=1e21\n", new ModelChange.Set("a", new Dictionary<string, object?> { ["size"] = 1e21 }));

        // Assert.
        Assert.Equal("item a size=1e21\n", text);
    }

    [Theory]
    [InlineData("label l \"Say \"hi\"\"\n", "label l\n")]
    [InlineData("label l \"x\"y\n", "label l\n")]
    [InlineData("label l \"x\" y\n", "label l\n")]
    public void AValueRemoved_WhoseRemovalWouldChangeHowTheLineReads_ReEmitsTheLine(string text, string expected)
    {
        // Act: removing the quoted word with the space before it would glue the next word to the id, or move it into the value's place.
        var changed = Changed(text, new ModelChange.Set("l", new Dictionary<string, object?> { ["text"] = "" }));

        // Assert.
        Assert.Equal(expected, changed);
    }

    [Fact]
    public void AValueReadFromPartOfAWord_IsRemovedWithTheWholeWord()
    {
        // Act.
        var text = Changed("item a size=1.5 other\n", new ModelChange.Set("a", new Dictionary<string, object?> { ["size"] = null }));

        // Assert.
        Assert.Equal("item a other\n", text);
    }
}
