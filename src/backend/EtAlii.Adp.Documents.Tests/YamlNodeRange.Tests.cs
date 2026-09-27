using Xunit;
using YamlDotNet.RepresentationModel;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// The lines a parsed YAML node occupies (backend-centralization R7).
/// </summary>
/// <remarks>
/// These pin the shared rule on inputs whose outcome follows from YamlDotNet's documented behaviour. The
/// converted module's own parser tests - dependency-graph's first - are the rest of the proof, because
/// they now reach this code instead of a private copy.
/// </remarks>
public class YamlNodeRangeTests
{
    private const string TwoElements =
        "elements:\n" +
        "  - id: a\n" +
        "    label: Alpha\n" +
        "  - id: b\n" +
        "    label: Beta\n";

    [Fact]
    public void AnElementInABlockSequence_TakesItsOwnLines_NotTheNextElementsFirstLine()
    {
        // A block mapping has no closing token, so YamlDotNet ends it where the next element's "-"
        // begins. Taking that literally gives this element the next element's first line too, and a
        // splice then edits the wrong declaration. Seen to fail against the node's own end mark.
        var element = Elements(TwoElements)[0];

        var range = YamlNodeRange.Of(element, LineDocument.Parse(TwoElements).Lines);

        Assert.Equal(new LineRange(1, 2), range);
    }

    [Fact]
    public void TheLastElement_EndsAtItsLastContentLine()
    {
        var element = Elements(TwoElements)[1];

        var range = YamlNodeRange.Of(element, LineDocument.Parse(TwoElements).Lines);

        Assert.Equal(new LineRange(3, 4), range);
    }

    [Fact]
    public void ABlockScalarFollowedStraightByTheNextElement_DoesNotTakeThatElementsFirstLine()
    {
        // THE COLUMN-ONE STEP BACK. A block scalar's end mark is the start of the NEXT line, column
        // one, whatever that line's indentation - so without stepping back off it, this element's
        // range takes "  - id: b" and a splice edits the wrong declaration. Nothing guarded this until
        // a plant removing the step left 444 tests green; this input was then found by measurement:
        // with the step the element is (1,3), without it (1,4).
        const string text =
            "elements:\n" +
            "  - id: a\n" +
            "    notes: |\n" +
            "      line one\n" +
            "  - id: b\n";

        var range = YamlNodeRange.Of(Elements(text)[0], LineDocument.Parse(text).Lines);

        Assert.Equal(new LineRange(1, 3), range);
    }

    [Fact]
    public void ABlockScalarFollowedByABlankAndAComment_LeavesThemToTheNextElement()
    {
        // THE TRAILING TRIM, which is R7's own user story: trailing blank lines and comments are
        // treated alike. The comment introduces the next element, so an edit of this one must not
        // rewrite it. Also unguarded until a plant removing the trim left 444 tests green; measured
        // on this input, with the trim (1,3), without it (1,4).
        const string text =
            "elements:\n" +
            "  - id: a\n" +
            "    notes: |\n" +
            "      line one\n" +
            "\n" +
            "  # introduces b\n" +
            "  - id: b\n";

        var range = YamlNodeRange.Of(Elements(text)[0], LineDocument.Parse(text).Lines);

        Assert.Equal(new LineRange(1, 3), range);
    }

    [Fact]
    public void AMappingEntry_TakesItsKeyAndItsValueTogether()
    {
        // databricks's form: removing or moving an entry has to take the key line and the value's lines.
        const string text =
            "a:\n" +
            "  b: 1\n" +
            "  c: 2\n";
        var root = Root(text);
        var entry = root.Children.First();

        var range = YamlNodeRange.Of(entry.Key, entry.Value, LineDocument.Parse(text).Lines);

        Assert.Equal(new LineRange(0, 2), range);
    }

    [Fact]
    public void ABlockScalarsLastLine_LedByATab_IsContentAndStaysInTheRange_WhichIsAzurePipelinesBehaviour()
    {
        // R7.2's pin, on the input that tells the copies apart. The four range bodies were identical;
        // what differed was what a comment IS. databricks, dependency-graph and timeline trimmed every
        // kind of leading whitespace before looking for '#', azure-pipeline trimmed spaces only. The
        // only way a line whose first non-space character is a tab reaches a range's end is as a block
        // scalar's CONTENT - YamlDotNet refuses a tab-led comment line in block context - so the three's
        // rule cut the scalar's last line out of the element that holds it, and a splice of the element
        // would leave that line behind. Measured on this input: azure-pipeline's rule (1,4), the three's
        // (1,3). azure-pipeline's behaviour is kept, by the design's S7 rule.
        const string text =
            "elements:\n" +
            "  - id: a\n" +
            "    notes: |\n" +
            "      line one\n" +
            "      \t# still line two of the notes\n" +
            "  - id: b\n";
        var element = Elements(text)[0];
        Assert.Equal("line one\n\t# still line two of the notes\n", ((YamlScalarNode)((YamlMappingNode)element).Children[new YamlScalarNode("notes")]).Value);

        var range = YamlNodeRange.Of(element, LineDocument.Parse(text).Lines);

        Assert.Equal(new LineRange(1, 4), range);
    }

    [Fact]
    public void AModulesOwnLineType_GetsTheSameRangeAsLine()
    {
        // databricks and azure-pipeline still hold their lines in types of their own, and reach the
        // rule through the overload that reads a line's text. The same input must give the same range
        // through both doors, including the trailing trim, or the two doors are two rules.
        const string text =
            "elements:\n" +
            "  - id: a\n" +
            "    notes: |\n" +
            "      line one\n" +
            "\n" +
            "  # introduces b\n" +
            "  - id: b\n";
        var element = Elements(text)[0];
        var texts = text.Split('\n')[..^1];

        var range = YamlNodeRange.Of(element, texts, line => line);

        Assert.Equal(YamlNodeRange.Of(element, LineDocument.Parse(text).Lines), range);
        Assert.Equal(new LineRange(1, 3), range);
    }

    private static YamlMappingNode Root(string text)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(text));
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    private static IList<YamlNode> Elements(string text) =>
        ((YamlSequenceNode)Root(text).Children[new YamlScalarNode("elements")]).Children;
}
