using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// The definition's findings as the tool lists them: one list by line across every code, ties in the order they arose,
/// and each one as the panel shows it.
/// </summary>
/// <remarks>
/// <b>The order is the tool's, not the definition's</b> (finding A9): DISL orders findings by its <c>constraints.order</c>
/// and has no way to say "by line", so the list is sorted here, stably. It is the stand-in of Requirement 3.4 for that
/// gap, and goes when DISL can state the order.
/// </remarks>
internal static class SupplyChainFindings
{
    /// <summary><paramref name="findings"/> by line, a finding without a line first, ties kept in the order they arose.</summary>
    public static IReadOnlyList<DislFinding> ByLine(IEnumerable<DislFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        return [.. findings.OrderBy(finding => finding.Line ?? 0)];
    }

    /// <summary><paramref name="finding"/> as the Errors and Warnings panel shows it, on its one-based line.</summary>
    public static DiagramProblem ToProblem(DislFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        return new DiagramProblem(
            DislDiagramDefinition.SeverityOf(finding),
            finding.Message,
            finding.Code,
            new DiagramProblemLineLocation((uint)Math.Max(1, finding.Line ?? 1)));
    }

    /// <summary><paramref name="finding"/> as a breach, on its zero-based line.</summary>
    public static SupplyChainBreach ToBreach(DislFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        return new SupplyChainBreach(
            finding.Code,
            finding.Message,
            Math.Max(0, (finding.Line ?? 1) - 1),
            DislDiagramDefinition.SeverityOf(finding) != DiagramProblemSeverity.Error);
    }
}
