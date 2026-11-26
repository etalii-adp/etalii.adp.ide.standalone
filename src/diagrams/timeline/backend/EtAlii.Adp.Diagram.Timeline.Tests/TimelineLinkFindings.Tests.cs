using EtAlii.Adp.Specification.Fbl.Links;
using Xunit;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// The findings of a timeline's links in the problems list (FBL 17.6, etalii.adp spec 092, T017), at the element that holds
/// the link.
/// </summary>
public sealed class TimelineLinkFindingsTests
{
    private const string Root = "/work";

    [Theory]
    [InlineData("$k7q2m9xa@features.yaml#signin.Name=Sign-in", null)]
    [InlineData("$zzzzzzzz@missing.yaml#signin.Name=Sign-in", "link.broken-document")]
    [InlineData("$k7q2m9xa@features.yaml#gone.Name=Gone", "link.broken-element")]
    [InlineData("features.yaml#signin", "link.malformed")]
    public async Task ALink_IsJudgedAtTheElementThatHoldsIt(string link, string? code)
    {
        // Arrange.
        var validator = new TimelineValidator(new TimelineOpeningIndex(Workspace()));

        // Act.
        var problems = await validator.ValidateAsync(Request(link), TestContext.Current.CancellationToken);

        // Assert.
        var found = problems.Where(problem => problem.RuleId.StartsWith("link.", StringComparison.Ordinal)).ToList();
        if (code is null)
        {
            Assert.Empty(found);
            return;
        }

        var problem = Assert.Single(found);
        Assert.Equal(code, problem.RuleId);
        Assert.Equal(code == "link.malformed" ? DiagramProblemSeverity.Error : DiagramProblemSeverity.Warning, problem.Severity);
        Assert.Equal(new DiagramProblemElementLocation("f-signin"), problem.Location);
    }

    [Fact]
    public async Task TwoDocumentsWithTheLinksIdAndName_AreAmbiguous()
    {
        // Arrange: a copy of the target, made beside it in another folder, carries its id and name.
        var signin = new IndexedElement("signin", [new KeyValuePair<string, string>("Name", "Sign-in")]);
        var validator = new TimelineValidator(new TimelineOpeningIndex(new LinkWorkspace(Root,
        [
            new IndexedDocument(Root + "/features.yaml", "k7q2m9xa", "Name", [signin], []),
            new IndexedDocument(Root + "/copy/features.yaml", "k7q2m9xa", "Name", [signin], []),
        ])));

        // Act.
        var problems = await validator.ValidateAsync(Request("$k7q2m9xa@features.yaml#signin.Name=Sign-in"), TestContext.Current.CancellationToken);

        // Assert.
        var problem = Assert.Single(problems, problem => problem.RuleId.StartsWith("link.", StringComparison.Ordinal));
        Assert.Equal("link.ambiguous-document", problem.RuleId);
        Assert.Equal(new DiagramProblemElementLocation("f-signin"), problem.Location);
    }

    [Fact]
    public async Task WithoutAnIndex_OnlyALinkThatDoesNotParse_IsReported()
    {
        // Arrange.
        var validator = new TimelineValidator();

        // Act.
        var broken = await validator.ValidateAsync(Request("$zzzzzzzz@missing.yaml#signin.Name=Sign-in"), TestContext.Current.CancellationToken);
        var malformed = await validator.ValidateAsync(Request("features.yaml#signin"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.DoesNotContain(broken, problem => problem.RuleId.StartsWith("link.", StringComparison.Ordinal));
        Assert.Contains(malformed, problem => problem.RuleId == "link.malformed");
    }

    private static DiagramValidationRequest Request(string link) => new(
        $"timeline: 1\nid: r6d3w8ne\nelements:\n  - id: f-signin\n    link: {link}\n    begin: 2027-01-04\n    row: 1\n",
        "roadmap",
        ProjectRoot.From(Root),
        Root + "/roadmap.tml",
        null);

    private static LinkWorkspace Workspace() => new(Root,
    [
        new IndexedDocument(Root + "/features.yaml", "k7q2m9xa", "Name", [new IndexedElement("signin", [new KeyValuePair<string, string>("Name", "Sign-in")])], []),
    ]);
}
