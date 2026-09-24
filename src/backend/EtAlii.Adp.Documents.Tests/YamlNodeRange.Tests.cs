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
    public void ATabLedLine_IsAComment_WhichIsTheBehaviourKeptOverAzurePipelines()
    {
        // R7.2's pin, on the input that tells the copies apart. The four range bodies were identical;
        // what differed was what a comment IS. azure-pipeline's line type trimmed only spaces before
        // looking for '#', so a tab-led comment stayed inside a node's range there and a splice would
        // eat it. databricks, dependency-graph and timeline trimmed every kind of leading whitespace,
        // and the three win. Pinned on the predicate the range uses, so it holds whether or not
        // YamlDotNet accepts a tab-led comment line in block context - which was not measured.
        Assert.True(new Line("\t# introduces the next stage", "\n").IsComment);
        Assert.True(new Line("  # introduces the next stage", "\n").IsComment);

        // And the other direction, so a predicate that called everything a comment would not pass.
        Assert.False(new Line("  - id: a # trailing, not a comment line", "\n").IsComment);
        Assert.False(new Line("\t- id: a", "\n").IsComment);
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
