using EtAlii.Adp.Specification.Fbl.Links;
using Xunit;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>The findings of a dependency graph's links in the problems list (FBL 17.6, etalii.adp spec 092, T017), at the element that holds the link.</summary>
public sealed class DependencyGraphLinkFindingsTests
{
    private const string Root = "/work";

    [Theory]
    [InlineData("checkout", null)]
    [InlineData("gone", "link.broken-element")]
    public async Task ALink_IsJudgedAtTheElementThatHoldsIt(string element, string? code)
    {
        // Arrange.
        var validator = new DependencyGraphValidator(new DependencyGraphOpeningIndex(new LinkWorkspace(Root,
        [
            new IndexedDocument(Root + "/features.yaml", "k7q2m9xa", "Name", [new IndexedElement("checkout", [new KeyValuePair<string, string>("Name", "Checkout")])], []),
        ])));

        // Act.
        var problems = await validator.ValidateAsync(new DiagramValidationRequest($"dependencies: 1\nid: s2v5b9hu\nelements:\n  - id: pay\n    link: $k7q2m9xa@features.yaml#{element}.Name=Checkout\n    x: 0\n    row: 2\n", "services", ProjectRoot.From(Root), Root + "/services.dgr", null), TestContext.Current.CancellationToken);

        // Assert.
        var found = problems.Where(problem => problem.RuleId.StartsWith("link.", StringComparison.Ordinal)).ToList();
        if (code is null)
        {
            Assert.Empty(found);
            return;
        }

        var problem = Assert.Single(found);
        Assert.Equal(code, problem.RuleId);
        Assert.Equal(new DiagramProblemElementLocation("pay"), problem.Location);
    }
}
