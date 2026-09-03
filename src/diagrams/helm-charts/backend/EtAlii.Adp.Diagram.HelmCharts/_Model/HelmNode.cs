namespace EtAlii.Adp.Diagram.HelmCharts;

/// <summary>
/// One box in the diagram, with the identity everything downstream agrees on.
/// </summary>
/// <param name="Id">
/// Stable and content-derived - <c>chart</c>, <c>values:values.yaml</c>,
/// <c>tpl:templates/deployment.yaml</c>, <c>dep:cache</c>, <c>sub:charts/redis</c> - because
/// the registration's <c>layout:</c> block stores these across sessions: a renamed file
/// intentionally forfeits its stored position (the design's id rule).
/// </param>
/// <param name="Kind">Which node kind this is.</param>
/// <param name="Name">What a reader calls it.</param>
/// <param name="RelativePath">
/// The file or folder it stands for, chart-root-relative - what activation opens
/// (Requirement 8.1); empty for a node with no backing artifact of its own.
/// </param>
public sealed record HelmNode(
    string Id,
    HelmNodeKind Kind,
    string Name,
    string RelativePath);
