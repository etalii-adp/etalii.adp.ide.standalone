using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests;

public class AbmParserTests
{
    private static AbmModel Parse(params string[] lines) => AbmParser.Parse(LineDocument.Parse(string.Join("\n", lines) + "\n"));

    [Fact]
    public void ReadsTheTree_KindsLabelsAndPlaces()
    {
        // Act.
        var model = Parse(
            "# Agent",
            "",
            "## Behavior",
            "",
            "- **Try in order:** Help",
            "  - **Check:** The user asked",
            "  - **Do in order:** Work",
            "    - **Do:** Act",
            "    - **Retry up to 2 times:** Test",
            "      - **Ask the user:** Which test?");

        // Assert.
        Assert.Equal(["1", "1.1", "1.2", "1.2.1", "1.2.2", "1.2.2.1"], model.Nodes.Select(node => node.Id));
        Assert.Equal(
            [AbmNodeKinds.Fallback, AbmNodeKinds.Check, AbmNodeKinds.Sequence, AbmNodeKinds.Action, AbmNodeKinds.Retry, AbmNodeKinds.Ask],
            model.Nodes.Select(node => node.Kind));
        Assert.Equal(["Help", "The user asked", "Work", "Act", "Test", "Which test?"], model.Nodes.Select(node => node.Label));
        Assert.Equal(2, model.NodeOf("1.2.2")!.RetryCount);
        Assert.Equal(["1.1", "1.2"], model.NodeOf("1")!.ChildIds);
        Assert.Equal(9, model.NodeOf("1")!.SubtreeEnd);
        Assert.Empty(model.Problems);
    }

    [Fact]
    public void ReadsNotes_WithoutTheirIndentation_AndKeepsThemOutOfTheTree()
    {
        // Act.
        var model = Parse(
            "## Behavior",
            "- **Do in order:** Work",
            "  First line of the notes.",
            "",
            "    Indented further.",
            "  - **Do:** Act");

        // Assert.
        var root = model.NodeOf("1")!;
        Assert.Equal("First line of the notes.\n\n  Indented further.", root.Notes);
        Assert.Equal(new LineRange(2, 4), root.NotesRange);
        Assert.Equal(["1.1"], root.ChildIds);
    }

    [Fact]
    public void ReadsOnlyTheBehaviorSection_AndNotAHeadingInsideACodeBlock()
    {
        // Act.
        var model = Parse(
            "# Agent",
            "- not a node: the list before the section",
            "```",
            "## Behavior",
            "- **Do:** inside a code block",
            "```",
            "## Behaviour",
            "- **Do:** The real one",
            "## Other",
            "- **Do:** After the section");

        // Assert.
        var node = Assert.Single(model.Nodes);
        Assert.Equal("The real one", node.Label);
        Assert.Equal(6, model.SectionLine);
        Assert.Equal(7, model.SectionEnd);
    }

    [Theory]
    [InlineData("- **Do**: Act", "Act")]
    [InlineData("* **do:** Act", "Act")]
    [InlineData("+ **Do:**Act", "Act")]
    public void ReadsTheKeywordsWrittenByHand(string line, string label)
    {
        // Act.
        var node = Assert.Single(Parse("## Behavior", line).Nodes);

        // Assert.
        Assert.Equal(AbmNodeKinds.Action, node.Kind);
        Assert.Equal(label, node.Label);
        Assert.True(node.HasKeyword);
    }

    [Fact]
    public void AnItemWithoutAKeyword_IsReadAsADo_AndReported()
    {
        // Act.
        var model = Parse("## Behavior", "- Just do the thing");

        // Assert.
        var node = Assert.Single(model.Nodes);
        Assert.Equal(AbmNodeKinds.Action, node.Kind);
        Assert.False(node.HasKeyword);
        Assert.Equal("Just do the thing", node.Label);
        Assert.Equal(AbmRuleIds.NoKeyword, Assert.Single(model.Problems).RuleId);
    }

    [Fact]
    public void ATabIndentedChild_IsAChild()
    {
        // Act.
        var model = Parse("## Behavior", "- **Do in order:** Work", "\t- **Do:** Act");

        // Assert.
        Assert.Equal("1", model.NodeOf("1.1")!.ParentId);
    }

    [Fact]
    public void AFileWithoutASection_HasNoTree()
    {
        // Act.
        var model = Parse("# Notes", "- **Do:** Something");

        // Assert.
        Assert.Null(model.SectionLine);
        Assert.Empty(model.Nodes);
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void EveryExample_ReadsWithoutAProblem(string name)
    {
        // Act.
        var model = AbmParser.Parse(LineDocument.Parse(File.ReadAllText(AbmExamples.BodyOf(name))));

        // Assert.
        Assert.True(model.Nodes.Count > 5, $"{name} read only {model.Nodes.Count} nodes");
        Assert.Empty(AbmRuleSet.Breaches(model));
    }

    [Fact]
    public void TheExamples_UseEveryKind()
    {
        // Act.
        var kinds = AbmExamples.Names
            .SelectMany(name => AbmParser.Parse(LineDocument.Parse(File.ReadAllText(AbmExamples.BodyOf(name)))).Nodes)
            .Select(node => node.Kind)
            .ToHashSet();

        // Assert.
        Assert.Equal(AbmNodeKinds.All.Select(kind => kind.Id).Order(), kinds.Order());
    }

    public static TheoryData<string> Examples => [.. AbmExamples.Names];
}
