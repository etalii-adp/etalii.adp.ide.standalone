using System.Text;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Rules;
using EtAlii.Adp.Specification.Fbl.Text;
using EtAlii.Adp.Specification.Fbl.Yaml;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Yaml;

/// <summary>
/// FBL §4.3, the yaml family's reading: the body is read as YAML reads it, in one pass over YamlDotNet's
/// tokens (spec 025, FR-081). Each case is one a hand-written line reader got wrong: content on a
/// <c>---</c> line, block scalar chomping, folding and tabs, escapes in double quotes, keys with an
/// anchor, a tag or an alias, comments and keys in flow collections, and a pair in a flow sequence.
/// </summary>
public class YamlReadingTests
{
    private static readonly FblBinding _binding = Load("""
        [{ "name": "item", "type": "Item", "at": "/items/*", "id": { "from": { "key": "id" } } }]
        """);

    private static FblBinding Load(string elements)
    {
        var json = $$"""{ "fbl": "0.1", "bindings": { "t": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "yaml" }, "reader": "declared", "elements": {{elements}} } } }""";
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), out var document);
        Assert.Empty(problems);
        return document!.Bindings["t"];
    }

    private static YamlFamily Parse(string body)
    {
        var family = new YamlFamily(new BodyText(Encoding.UTF8.GetBytes(body)), _binding, new FblOptions { FileName = "plan.t" });
        family.Parse();
        return family;
    }

    private static TreeValue Root(string body)
    {
        var family = Parse(body);
        Assert.Null(family.Unreadable);
        return family.Root!.Value;
    }

    private static TreeValue Member(string body, string key) => Root(body).Member(key)!.Value;

    [Theory]
    [InlineData("--- |\n  text\n", "Literal", "text\n")]
    [InlineData("--- \"quoted\"\n", "Double", "quoted")]
    [InlineData("--- plain\n", "Plain", "plain")]
    public void ContentOnTheDocumentStartLine_IsTheDocumentsValue(string body, string style, string text)
    {
        // Act.
        var root = Root(body);

        // Assert.
        Assert.Equal(style, root.Style.ToString());
        Assert.Equal(text, root.Text);
    }

    [Theory]
    [InlineData("run: |\n  a\n  b", "a\nb")]
    [InlineData("keep: |+\n\nnext: 1\n", "\n")]
    [InlineData("keep: |+\n  x\n   \nnext: 1\n", "x\n \n")]
    [InlineData("fold: >\n  ab\n  cd\n\n  ef\n", "ab cd\nef\n")]
    [InlineData("tab: |-\n \tbar\n", "\tbar")]
    public void ABlockScalar_ReadsAsYamlChompsAndFoldsIt(string body, string text)
    {
        // Act.
        var value = Member(body, body[..body.IndexOf(':', StringComparison.Ordinal)]);

        // Assert.
        Assert.Equal(text, value.Text);
    }

    [Theory]
    [InlineData("k: \"a\\\n  b\"\n", "ab")]
    [InlineData("k: \"x\\\t\n  y\"\n", "x\t y")]
    public void ADoubleQuotedScalar_KeepsWhatItsEscapedLineBreakAndTabSay(string body, string text)
    {
        // Act.
        var value = Member(body, "k");

        // Assert.
        Assert.Equal(text, value.Text);
    }

    [Fact]
    public void KeysWithAnAnchorATagOrAnAlias_AreRead()
    {
        // Act.
        var root = Root("a: &v val\n&k b: 1\n!!str c: 2\n*v : 3\n");

        // Assert.
        Assert.Equal(["a", "b", "c", "val"], root.Entries.Select(entry => entry.Name));
        Assert.Equal(3L, root.Member("val")!.Value.Typed);
    }

    [Fact]
    public void AnEmptyKey_MakesTheBodyUnreadable()
    {
        // Act.
        var family = Parse(": a\n");

        // Assert.
        Assert.Equal((0, "A mapping key is empty."), family.Unreadable);
    }

    [Fact]
    public void ACommentInAMultiLineFlowCollection_IsNotPartOfAMember()
    {
        // Act.
        var value = Member("k: [ a\n  # note\n  , b ]\n", "k");

        // Assert.
        Assert.Equal(["a", "b"], Assert.IsType<List<object?>>(value.FlowText));
    }

    [Fact]
    public void ACollectionAsAFlowKey_IsNamedByItsTextAsWritten()
    {
        // Act.
        var value = Member("name: {{ .Release.Name }}\n", "name");

        // Assert.
        var flow = Assert.IsType<CelMap>(value.Flow);
        Assert.Equal(["{ .Release.Name }"], flow.Keys);
        Assert.Equal(["{ .Release.Name }"], Assert.IsType<CelMap>(value.FlowText).Keys);
    }

    [Fact(Timeout = 10_000)]
    public async Task APairInAFlowSequence_IsAMappingOfOneMember()
    {
        // Act.
        var value = await Task.Run(() => Member("indent: [2, 2, SwitchCase: 1]\n", "indent"), TestContext.Current.CancellationToken);

        // Assert.
        var items = Assert.IsType<List<object?>>(value.Flow);
        Assert.Equal([2L, 2L], items.Take(2));
        var pair = Assert.IsType<CelMap>(items[2]);
        Assert.Equal(1L, Assert.Single(pair, member => member.Key == "SwitchCase").Value);
    }
}
