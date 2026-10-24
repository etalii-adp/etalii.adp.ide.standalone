using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.Sankey.Tests;

/// <summary>The findings are listed in the order of their lines, findings on one line in the order they were given.</summary>
public class SankeyFindingOrderTests
{
    [Fact]
    public void Findings_AreListedByLine_AndOneLinesKeepTheirOrder()
    {
        // Arrange: two codes on line 3, two on line 1, and one without a line.
        IReadOnlyList<DislFinding> findings =
        [
            Finding("sankey.missing-value", 3),
            Finding("sankey.duplicate-id", 1),
            Finding("sankey.unknown-color", 3),
            Finding("sankey.dangling-flow", 1),
            Finding("sankey.unreadable", null),
        ];

        // Act.
        var ordered = SankeyFindingOrder.ByLine(findings);

        // Assert.
        Assert.Equal(
            ["sankey.unreadable", "sankey.duplicate-id", "sankey.dangling-flow", "sankey.missing-value", "sankey.unknown-color"],
            ordered.Select(finding => finding.Code));
    }

    private static DislFinding Finding(string code, int? line) => new(code, code, "warning", code, [], line);
}
