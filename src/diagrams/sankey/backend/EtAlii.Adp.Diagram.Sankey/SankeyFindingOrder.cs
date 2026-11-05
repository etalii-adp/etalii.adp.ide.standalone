using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>
/// The Sankey diagram's findings in the order of their lines, as the panel has always listed them: a stable sort by
/// line, so findings on one line keep the order the evaluator gives them.
/// </summary>
/// <remarks>
/// <b>It stands in for a language gap</b> (gap 8 of <c>sankey.md</c>): DISL orders findings by its <c>order</c> list
/// and by reading (§8.6) and cannot state an order by line. It only sorts; what is found is the definition's.
/// </remarks>
internal static class SankeyFindingOrder
{
    /// <summary><paramref name="findings"/> by line, a finding without one first, ties in the order given.</summary>
    public static IReadOnlyList<DislFinding> ByLine(IReadOnlyList<DislFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        return [.. findings.OrderBy(finding => finding.Line ?? 0)];
    }
}
