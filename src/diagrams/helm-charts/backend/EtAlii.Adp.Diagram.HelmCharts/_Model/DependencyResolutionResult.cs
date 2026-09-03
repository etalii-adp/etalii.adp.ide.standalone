namespace EtAlii.Adp.Diagram.HelmCharts;

/// <summary>What <see cref="DependencyResolution.Match"/> made of a chart's dependencies.</summary>
/// <param name="Dependencies">Every declaration with its resolution, in declaration-list order.</param>
/// <param name="Undeclared">Vendored entries no declaration claims - reported by validation (Requirement 10.4).</param>
public sealed record DependencyResolutionResult(
    IReadOnlyList<ResolvedDependency> Dependencies,
    IReadOnlyList<VendoredEntry> Undeclared);
