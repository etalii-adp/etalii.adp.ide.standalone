using System.Text.RegularExpressions;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.HierarchicalRequirement.Tests;

/// <summary>The module's examples read as their readme says (T043): what it lists of them is there, and what it says they do not demonstrate is absent.</summary>
public sealed partial class ExamplesTests
{
    private static HierarchicalRequirementDocument WebShop() => HierarchicalRequirementBody.Read(
        File.ReadAllBytes(IoPath.Combine(HierarchicalRequirementFiles.ExamplesFolder, "web-shop.md")), "web-shop.md",
        File.ReadAllBytes(IoPath.Combine(HierarchicalRequirementFiles.ExamplesFolder, "web-shop.adp")), "web-shop.adp").Document;

    [Fact]
    public void TheWebShop_HasWhatItsReadmeLists()
    {
        // Act.
        var document = WebShop();

        // Assert: six items at three levels with ten numbered requirements and one placeholder, and four assessments, one of each kind.
        Assert.Equal(6, document.Entries.Count(entry => entry is Item));
        Assert.Equal(11, document.Entries.Count(entry => entry is Requirement));
        Assert.Equal(1, document.Entries.Count(entry => entry.Placeholder));
        Assert.Equal(3, Depth(document, "ITEM-0003"));
        Assert.Equal(Enum.GetValues<AssessmentKind>(), document.Assessments.Select(assessment => assessment.Kind).Order());

        // The registration's lists, two collapsed entries and two stored filters.
        Assert.Equal(9, document.Tags.Count);
        Assert.Equal(2, document.Entries.Count(entry => entry.Collapsed));
        Assert.Equal(["Open security", "Backend"], document.Filters.Select(filter => filter.Name));
        Assert.Equal(["#security state:proposed sort:state", "item:back* sort:number"], document.Filters.Select(filter => filter.Query));
        Assert.Equal(["blue", "purple", "brown"], document.Categories.Select(category => category.Colour));
        Assert.Equal(["gray", "green", "red", "blue"], document.States.Select(state => state.Colour));
    }

    [Fact]
    public void TheWebShop_UsesTheThreeMarkersAndAnAssessmentWithoutACheckbox()
    {
        // Arrange.
        var lines = File.ReadAllLines(IoPath.Combine(HierarchicalRequirementFiles.ExamplesFolder, "web-shop.md"));

        // Assert.
        foreach (var marker in new[] { '-', '*', '+' })
        {
            Assert.Contains(lines, line => line.TrimStart().StartsWith(marker + " REQ-", StringComparison.Ordinal));
        }
        Assert.Contains(lines, line => line.StartsWith("- Idea ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("- [ ] remark ", StringComparison.Ordinal));
    }

    [Fact]
    public void TheWebShop_DoesNotHoldWhatItsReadmeSaysItDoesNot()
    {
        // Act.
        var document = WebShop();
        var text = File.ReadAllText(IoPath.Combine(HierarchicalRequirementFiles.ExamplesFolder, "web-shop.md"));

        // Assert: nothing wrong but the placeholder, and both headings written at level two as the binding names them.
        Assert.Equal(["std.missingId"], document.Findings.Select(finding => finding.Code));
        Assert.Contains("REQ-NEW", Assert.Single(document.Findings).Message, StringComparison.Ordinal);
        Assert.Equal(2, Headings().Matches(text).Count);
    }

    /// <summary>How many items stand above and at <paramref name="id"/>, counting itself.</summary>
    private static int Depth(HierarchicalRequirementDocument document, string id)
    {
        var depth = 0;
        for (var entry = document.Entries.First(e => e.Id == id); entry is not null; entry = document.Entries.FirstOrDefault(e => e.Id == entry.ParentId)) depth++;
        return depth;
    }

    [GeneratedRegex(@"^## (?:Requirements|Assessments)\r?$", RegexOptions.Multiline)]
    private static partial Regex Headings();
}
