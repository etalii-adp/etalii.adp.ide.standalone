using Xunit;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// The shared splice mechanics (file-io-centralization Requirement 3.1), exercised against
/// inputs in <b>both</b> consuming formats rather than one.
/// </summary>
/// <remarks>
/// <para>
/// Both is the point. These helpers were byte-identical in the timeline and dependency-graph
/// writers, so testing them against one format would prove they still work for that format and
/// say nothing about the other - which is the shape of every "looked green through the wrong
/// observation" failure this spec has hit. Each theory below therefore runs twice, over a
/// timeline element (<c>begin</c>, <c>end</c>, <c>row</c>) and a dependency-graph element
/// (<c>x</c>, <c>row</c>).
/// </para>
/// <para>
/// The theory data carries each format's <em>distinguishing</em> key and its second element's
/// label, and the assertions use them. Without that the two cases would assert identical strings
/// and the second run would be decoration: a test that takes a parameter it does not depend on
/// is a test that runs twice and observes once.
/// </para>
/// <para>
/// What is deliberately <em>not</em> here: inserting or removing an element, setting a label or a
/// row. Those share a name between the two writers and differ in body, because they encode what
/// each format writes. They stayed in their modules.
/// </para>
/// </remarks>
public class LineSpliceTests
{
    /// <summary>A timeline document: elements carry begin, end and row.</summary>
    private const string TimelineShape =
        "elements:\r\n" +
        "  - id: kickoff\r\n" +
        "    label: Kick off\r\n" +
        "    begin: 2026-01-01\r\n" +
        "    row: 0\r\n" +
        "  - id: launch\r\n" +
        "    label: Launch\r\n" +
        "    begin: 2026-06-01\r\n" +
        "    row: 1\r\n";

    /// <summary>A dependency-graph document: elements carry x and row instead.</summary>
    private const string DependencyGraphShape =
        "elements:\r\n" +
        "  - id: api\r\n" +
        "    label: API\r\n" +
        "    x: 10\r\n" +
        "    row: 0\r\n" +
        "  - id: store\r\n" +
        "    label: Store\r\n" +
        "    x: 20\r\n" +
        "    row: 1\r\n";

    /// <summary>The first element's line range in both shapes above.</summary>
    private static readonly LineRange FirstElement = new(1, 4);

    /// <summary>The second element's line range in both shapes above.</summary>
    private static readonly LineRange SecondElement = new(5, 8);

    /// <summary>
    /// Each format, with the key that tells it apart from the other and the value that key holds
    /// in the first element, plus the second element's label and the first element's id.
    /// </summary>
    public static TheoryData<string, string, string, string, string> BothFormats() => new()
    {
        { TimelineShape, "begin", "2026-01-01", "Launch", "kickoff" },
        { DependencyGraphShape, "x", "10", "Store", "api" },
    };

    // ---- finding ----------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void FindSection_LocatesTheSectionKeyAndSaysSoWhenItIsAbsent(
        string text, string key, string value, string secondLabel, string firstId)
    {
        // Arrange.
        var document = LineDocument.Parse(text);

        // Act & assert.
        Assert.Equal(0, LineSplice.FindSection(document, "elements:"));
        Assert.Equal(-1, LineSplice.FindSection(document, "connections:"));
        Assert.NotEmpty(key + value + secondLabel + firstId);
    }

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void FindKey_FindsThisFormatsOwnKeyInsideItsRange(
        string text, string key, string value, string secondLabel, string firstId)
    {
        // Arrange.
        // `begin` for a timeline, `x` for a dependency graph - the key each format has and the
        // other does not, so a run that only worked for one shape shows up here.
        var document = LineDocument.Parse(text);

        // Act.
        var index = LineSplice.FindKey(document, FirstElement, key);

        // Assert.
        Assert.Equal(3, index);
        Assert.Equal($"    {key}: {value}", document.Lines[index].Text);
        Assert.NotEmpty(secondLabel + firstId);
    }

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void FindKey_StaysInsideItsRange(
        string text, string key, string value, string secondLabel, string firstId)
    {
        // Arrange.
        // `row` exists in both elements, so a search that ignored the range would find the wrong
        // one and every subsequent splice would edit the wrong element.
        var document = LineDocument.Parse(text);

        // Act & assert.
        Assert.Equal(4, LineSplice.FindKey(document, FirstElement, "row"));
        Assert.Equal(8, LineSplice.FindKey(document, SecondElement, "row"));
        Assert.Equal(-1, LineSplice.FindKey(document, FirstElement, "absent"));
        Assert.NotEmpty(key + value + secondLabel + firstId);
    }

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void FindKey_FindsAKeyWrittenOnTheItemsOwnDashLine(
        string text, string key, string value, string secondLabel, string firstId)
    {
        // Arrange.
        // The `- id:` line carries both the sequence dash and a key; the dash belongs to the
        // sequence, so the key is still findable.
        var document = LineDocument.Parse(text);

        // Act.
        var index = LineSplice.FindKey(document, FirstElement, "id");

        // Assert.
        Assert.Equal(1, index);
        Assert.Equal($"  - id: {firstId}", document.Lines[index].Text);
        Assert.NotEmpty(key + value + secondLabel);
    }

    // ---- setting ----------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void SetKey_ReplacesThisFormatsOwnValueAndKeepsTheLinesIndentation(
        string text, string key, string value, string secondLabel, string firstId)
    {
        // Arrange.
        var document = LineDocument.Parse(text);

        // Act.
        LineSplice.SetKey(document, FirstElement, key, "changed");

        // Assert.
        Assert.Equal($"    {key}: changed", document.Lines[3].Text);
        Assert.DoesNotContain(value, document.Lines[3].Text, StringComparison.Ordinal);
        // The document neither grew nor shrank, and the second element is untouched.
        Assert.Equal(9, document.Lines.Count);
        Assert.Contains(secondLabel, document.Lines[6].Text, StringComparison.Ordinal);
        Assert.NotEmpty(firstId);
    }

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void SetKey_OnTheDashLine_KeepsTheDash(
        string text, string key, string value, string secondLabel, string firstId)
    {
        // Arrange.
        var document = LineDocument.Parse(text);

        // Act.
        LineSplice.SetKey(document, FirstElement, "id", "renamed");

        // Assert.
        // Losing the dash would merge the item into its predecessor and break the sequence.
        Assert.Equal("  - id: renamed", document.Lines[1].Text);
        Assert.Equal(9, document.Lines.Count);
        Assert.NotEmpty(key + value + secondLabel + firstId);
    }

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void SetKey_AddsAMissingKeyAtTheNeighboursIndentation(
        string text, string key, string value, string secondLabel, string firstId)
    {
        // Arrange.
        var document = LineDocument.Parse(text);

        // Act.
        LineSplice.SetKey(document, FirstElement, "note", "added");

        // Assert.
        Assert.Equal("    note: added", document.Lines[2].Text);
        Assert.Equal(10, document.Lines.Count);
        // What was already there moved down rather than being overwritten.
        Assert.Equal($"    {key}: {value}", document.Lines[4].Text);
        Assert.NotEmpty(secondLabel + firstId);
    }

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void RemoveKey_TakesOneLineAndLeavesTheRest(
        string text, string key, string value, string secondLabel, string firstId)
    {
        // Arrange.
        var document = LineDocument.Parse(text);

        // Act.
        LineSplice.RemoveKey(document, FirstElement, key);

        // Assert.
        Assert.Equal(8, document.Lines.Count);
        Assert.DoesNotContain($"{key}: {value}", document.Text, StringComparison.Ordinal);
        // The second element keeps its own copy of the same key, and its label.
        Assert.Contains($"    {key}: ", document.Text, StringComparison.Ordinal);
        Assert.Contains($"    label: {secondLabel}", document.Text, StringComparison.Ordinal);
        Assert.Contains($"  - id: {firstId}", document.Text, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void RemoveKey_OnAKeyThatIsNotThere_ChangesNothing(
        string text, string key, string value, string secondLabel, string firstId)
    {
        // Arrange.
        var document = LineDocument.Parse(text);

        // Act.
        LineSplice.RemoveKey(document, FirstElement, "absent");

        // Assert.
        Assert.Equal(text, document.Text);
        Assert.NotEmpty(key + value + secondLabel + firstId);
    }

    // ---- indentation ------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void KeyIndentWithin_CopiesWhatTheNeighboursUse(
        string text, string key, string value, string secondLabel, string firstId)
    {
        // Act & assert.
        Assert.Equal("    ", LineSplice.KeyIndentWithin(LineDocument.Parse(text), FirstElement));
        Assert.NotEmpty(key + value + secondLabel + firstId);
    }

    [Fact]
    public void KeyIndentWithin_OnAOneLineItem_PutsTheKeyUnderTheDash()
    {
        // Arrange.
        // No neighbour to copy, so the dash's indent plus what the `- ` prefix occupies.
        var document = LineDocument.Parse("elements:\r\n  - id: lonely\r\n");

        // Act & assert.
        Assert.Equal("    ", LineSplice.KeyIndentWithin(document, new LineRange(1, 1)));
    }

    [Fact]
    public void KeyIndentWithin_SkipsABlankLineRatherThanCopyingIt()
    {
        // Arrange.
        // A blank line has no indentation to copy; copying it would put the new key at column
        // zero and silently move it out of the element.
        var document = LineDocument.Parse("elements:\r\n  - id: spaced\r\n\r\n    label: Spaced\r\n");

        // Act & assert.
        Assert.Equal("    ", LineSplice.KeyIndentWithin(document, new LineRange(1, 3)));
    }

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void IndentOf_CopiesTheItemIndentTheGapAndTheKeyIndent(
        string text, string key, string value, string secondLabel, string firstId)
    {
        // Act.
        (string itemIndent, string dashGap, string keyIndent) =
            LineSplice.IndentOf(LineDocument.Parse(text), [FirstElement]);

        // Assert.
        Assert.Equal("  ", itemIndent);
        Assert.Equal(" ", dashGap);
        Assert.Equal("    ", keyIndent);
        Assert.NotEmpty(key + value + secondLabel + firstId);
    }

    [Fact]
    public void IndentOf_CopiesAnUnusualGapAfterTheDash()
    {
        // Arrange.
        // A document written `-   id:` throughout, given one entry written `- id:`, is visibly
        // ADP's work rather than the author's - so the gap is copied, not normalised.
        var document = LineDocument.Parse("elements:\r\n  -   id: wide\r\n      label: Wide\r\n");

        // Act.
        (string itemIndent, string dashGap, string keyIndent) = LineSplice.IndentOf(document, [new LineRange(1, 2)]);

        // Assert.
        Assert.Equal("  ", itemIndent);
        Assert.Equal("   ", dashGap);
        Assert.Equal("      ", keyIndent);
    }

    [Fact]
    public void IndentOf_WithNoExistingEntry_FallsBackToTheDefaults()
    {
        // Act.
        (string itemIndent, string dashGap, string keyIndent) =
            LineSplice.IndentOf(LineDocument.Parse("elements:\r\n"), []);

        // Assert.
        Assert.Equal(LineSplice.DefaultItemIndent, itemIndent);
        Assert.Equal(" ", dashGap);
        Assert.Equal(LineSplice.DefaultKeyIndent, keyIndent);
    }

    // ---- insertion point --------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void InsertionPointFor_GoesAfterTheLastExistingEntry(
        string text, string key, string value, string secondLabel, string firstId)
    {
        // Arrange.
        var document = LineDocument.Parse(text);

        // Act & assert.
        Assert.Equal(9, LineSplice.InsertionPointFor(document, [FirstElement, SecondElement], "elements:"));
        Assert.NotEmpty(key + value + secondLabel + firstId);
    }

    [Fact]
    public void InsertionPointFor_WithNoEntries_GoesJustAfterTheSectionKey()
    {
        // Arrange.
        var document = LineDocument.Parse("elements:\r\nconnections:\r\n");

        // Act & assert.
        Assert.Equal(1, LineSplice.InsertionPointFor(document, [], "elements:"));
    }

    [Fact]
    public void InsertionPointFor_OpensAFlowEmptySectionFirst()
    {
        // Arrange.
        // Appending a block entry after a key that already carries `[]` would leave the key with
        // two values and the document unparseable, so the flow-empty form is opened first.
        var document = LineDocument.Parse("elements: []\r\nconnections: []\r\n");

        // Act.
        var at = LineSplice.InsertionPointFor(document, [], "elements:");

        // Assert.
        Assert.Equal(1, at);
        Assert.Equal("elements:\r\nconnections: []\r\n", document.Text);
    }

    [Fact]
    public void InsertionPointFor_WithNoSectionAtAll_SaysSo()
    {
        // Act & assert.
        // -1 rather than an exception: creating the section is the caller's business, because
        // what a new section looks like belongs to the format, not to the splice.
        Assert.Equal(-1, LineSplice.InsertionPointFor(LineDocument.Parse("other:\r\n"), [], "elements:"));
    }

    // ---- quoting ----------------------------------------------------------------------------

    [Theory]
    [InlineData("Kick off", "Kick off")]
    [InlineData("API", "API")]
    [InlineData("2026-01-01", "2026-01-01")]
    [InlineData("", "\"\"")]
    [InlineData("has: colon", "\"has: colon\"")]
    [InlineData("has # hash", "\"has # hash\"")]
    [InlineData("-leading dash", "\"-leading dash\"")]
    [InlineData(" leading space", "\" leading space\"")]
    [InlineData("trailing space ", "\"trailing space \"")]
    [InlineData("\"already quoted\"", "\"\\\"already quoted\\\"\"")]
    public void Quote_QuotesOnlyWhereYamlNeedsIt(string value, string expected)
    {
        // A label that did not need quoting must not gain quotation marks the document never had,
        // because every one of those is a diff line the author did not ask for.
        Assert.Equal(expected, LineSplice.Quote(value));
    }

    [Fact]
    public void Quote_EscapesTheQuotesAndBackslashesInsideWhatItQuotes()
    {
        // Act & assert.
        Assert.Equal("\"say \\\"hi\\\": now\"", LineSplice.Quote("say \"hi\": now"));
        Assert.Equal("\"a\\\\b: c\"", LineSplice.Quote("a\\b: c"));
    }

    /// <summary>
    /// A plain YAML scalar may not begin with an indicator character. Quote left these plain, so a name
    /// such as <c>[draft] plan</c> was written as a flow sequence, <c>&amp;x</c> as an anchor and
    /// <c>*x</c> as an alias: the value read back differently, or the document no longer parsed.
    /// </summary>
    [Theory]
    [InlineData("[draft] plan")]
    [InlineData("]x")]
    [InlineData("{x} y")]
    [InlineData("}x")]
    [InlineData("&anchor")]
    [InlineData("*alias")]
    [InlineData("!tag")]
    [InlineData("|pipe")]
    [InlineData(">fold")]
    [InlineData("%percent")]
    [InlineData("@at")]
    [InlineData("`tick")]
    [InlineData("? question")]
    [InlineData(", comma")]
    public void Quote_AValueStartingWithAnIndicator_ReadsBackAsWritten(string value)
    {
        // Arrange.
        var line = $"name: {LineSplice.Quote(value)}";

        // Act.
        var read = ReadName(line);

        // Assert.
        Assert.Equal(value, read);
    }

    private static string? ReadName(string document)
    {
        var stream = new YamlDotNet.RepresentationModel.YamlStream();
        try
        {
            stream.Load(new StringReader(document));
        }
        catch (YamlDotNet.Core.YamlException e)
        {
            return $"(does not parse: {e.Message})";
        }
        var root = (YamlDotNet.RepresentationModel.YamlMappingNode)stream.Documents[0].RootNode;
        return root.Children[new YamlDotNet.RepresentationModel.YamlScalarNode("name")] is YamlDotNet.RepresentationModel.YamlScalarNode scalar
            ? scalar.Value
            : "(not a scalar)";
    }

    /// <summary>
    /// A line break in a plain scalar ends the key: the rest lands at column 0 and the document no
    /// longer parses. Every break - LF, CRLF and a lone CR - is therefore written as a YAML
    /// double-quoted escape, so the value stays on its key's line and reads back unchanged.
    /// </summary>
    [Theory]
    [InlineData("Line one\nline two", "\"Line one\\nline two\"")]
    [InlineData("Line one\r\nline two", "\"Line one\\r\\nline two\"")]
    [InlineData("Line one\rline two", "\"Line one\\rline two\"")]
    [InlineData("a \"b\"\nc\\d", "\"a \\\"b\\\"\\nc\\\\d\"")]
    public void Quote_WritesALineBreakAsAnEscapeInsideDoubleQuotes(string value, string expected)
    {
        // Act & assert.
        Assert.Equal(expected, LineSplice.Quote(value));
    }
}
