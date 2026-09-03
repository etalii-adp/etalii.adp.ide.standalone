namespace EtAlii.Adp.Diagram.HelmCharts;

/// <summary>One file of the values stack.</summary>
/// <param name="RelativePath">Chart-root-relative.</param>
/// <param name="IsDefault">True only for <c>values.yaml</c> itself; every other <c>values*.y(a)ml</c> sibling is an override layer.</param>
/// <param name="HasGlobal">The file carries a top-level <c>global:</c> key - visible to every subchart, so the node is marked rather than fanning out an edge per dependency (Requirement 5.4).</param>
/// <param name="TopLevelKeys">The top-level keys, ordinally sorted - what the configures edges match dependency names against.</param>
/// <param name="Failure">Why the file did not parse, when it did not.</param>
public sealed record ValuesFile(
    string RelativePath,
    bool IsDefault,
    bool HasGlobal,
    IReadOnlyList<string> TopLevelKeys,
    HelmYamlFailure? Failure);
