using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests;

public class AbmDocumentFactoryTests
{
    [Fact]
    public void ANewModel_HasOneRoot_AndExplainsEveryKeywordToTheAgent()
    {
        // Act.
        var text = new AbmDocumentFactory().CreateEmptyDocument("Release notes writer");
        var model = AbmParser.Parse(LineDocument.Parse(text));

        // Assert: the legend's list is prose, not part of the tree.
        Assert.StartsWith("# Release notes writer\r\n", text, StringComparison.Ordinal);
        var root = Assert.Single(model.Nodes);
        Assert.Equal(AbmNodeKinds.Sequence, root.Kind);
        foreach (var kind in AbmNodeKinds.All)
        {
            Assert.Contains($"- **{kind.Keyword}** {kind.Meaning}.", text, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(AbmValidator.Validate(AbmBody.Parse(text)), breach => breach.IsError);
        Assert.True(Diagram.SuggestsBody(text));
    }

    [Theory]
    [InlineData("## Behavior\n", true)]
    [InlineData("### behaviour ###\n", true)]
    [InlineData("# Agent\n\nThe behavior is described below.\n", false)]
    [InlineData("", false)]
    public void SuggestsBody_OnlyForAFileWithABehaviorHeading(string text, bool suggested)
    {
        // Act and assert.
        Assert.Equal(suggested, Diagram.SuggestsBody(text));
    }

    [Fact]
    public void TheExamples_ExplainTheKeywordsAsANewModelDoes()
    {
        // Arrange.
        var legend = string.Join("\r\n", AbmDocumentFactory.Legend());

        // Act and assert.
        foreach (var name in AbmExamples.Names)
        {
            Assert.Contains(legend, File.ReadAllText(AbmExamples.BodyOf(name)), StringComparison.Ordinal);
        }
    }
}
