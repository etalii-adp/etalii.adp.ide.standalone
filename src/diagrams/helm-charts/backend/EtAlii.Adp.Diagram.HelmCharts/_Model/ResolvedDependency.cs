namespace EtAlii.Adp.Diagram.HelmCharts;

/// <summary>One declared dependency and what it resolved to.</summary>
/// <param name="Dependency">The declaration.</param>
/// <param name="Vendored">The matching <c>charts/</c> entry, or null - the Unvendored open end, drawn but never a finding.</param>
public sealed record ResolvedDependency(DependencyDeclaration Dependency, VendoredEntry? Vendored)
{
    /// <summary>Something in <c>charts/</c> answers this declaration.</summary>
    public bool IsResolved => Vendored is not null;
}
