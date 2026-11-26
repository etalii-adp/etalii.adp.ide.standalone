using EtAlii.Adp.Specification.Fbl.Links;
using Xunit;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

/// <summary>The findings of a mind map's links in the problems list (FBL 17.6, etalii.adp spec 092, T017), at the element that holds the link.</summary>
public sealed class MindmapLinkFindingsTests
{
    private const string Root = "/work";

    [Theory]
    [InlineData("checkout", null)]
    [InlineData("gone", "link.broken-element")]
    public async Task ALink_IsJudgedAtTheElementThatHoldsIt(string element, string? code)
    {
        // Arrange.
        var validator = new MindmapValidator(new MindmapOpeningIndex(new LinkWorkspace(Root,
        [
            new IndexedDocument(Root + "/features.yaml", "k7q2m9xa", "Name", [new IndexedElement("checkout", [new KeyValuePair<string, string>("Name", "Checkout")])], []),
        ])));

        // Act.
        var problems = await validator.ValidateAsync(new DiagramValidationRequest($"<map version=\"freeplane 1.11.5\">\n<node TEXT=\"Shop relaunch\" ID=\"ID_1\">\n<attribute NAME=\"adp-id\" VALUE=\"p4r8t1zc\"/>\n<node TEXT=\"Checkout\" ID=\"ID_2\">\n<attribute NAME=\"adp-link\" VALUE=\"$k7q2m9xa@features.yaml#{element}.Name=Checkout\"/>\n</node>\n</node>\n</map>\n", "ideas", ProjectRoot.From(Root), Root + "/ideas.mm", null), TestContext.Current.CancellationToken);

        // Assert.
        var found = problems.Where(problem => problem.RuleId.StartsWith("link.", StringComparison.Ordinal)).ToList();
        if (code is null)
        {
            Assert.Empty(found);
            return;
        }

        var problem = Assert.Single(found);
        Assert.Equal(code, problem.RuleId);
        Assert.Equal(new DiagramProblemElementLocation("ID_2"), problem.Location);
    }
}
