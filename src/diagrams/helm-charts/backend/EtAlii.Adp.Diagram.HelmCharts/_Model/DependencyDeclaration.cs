namespace EtAlii.Adp.Diagram.HelmCharts;

/// <summary>One declared dependency - from <c>Chart.yaml</c> for v2, from <c>requirements.yaml</c> for v1.</summary>
/// <param name="Name">The dependency chart name.</param>
/// <param name="Alias">The alias it is mounted under, when set - which is then the name everything else matches.</param>
/// <param name="VersionConstraint">The declared version or range, verbatim; never evaluated (the design refuses a range engine).</param>
/// <param name="Repository">Where it comes from - an https or oci reference, verbatim.</param>
/// <param name="Condition">The values path that switches it, when declared.</param>
/// <param name="State">That condition resolved against the default values at read time.</param>
/// <param name="Line">The 1-based line the dependency entry starts on.</param>
public sealed record DependencyDeclaration(
    string Name,
    string? Alias,
    string? VersionConstraint,
    string? Repository,
    string? Condition,
    ConditionState State,
    uint Line)
{
    /// <summary>Alias over name: the key that <c>charts/</c> content and parent values are matched against (Requirement 5.2/5.4).</summary>
    public string EffectiveName => Alias is { Length: > 0 } ? Alias : Name;
}
