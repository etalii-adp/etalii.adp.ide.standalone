namespace EtAlii.Adp.Backend.Context;

/// <summary>
/// What a context action applies to, already resolved by the service layer: a provider
/// receives the absolute location it should act on and never resolves anything itself,
/// so containment is enforced in exactly one place.
/// </summary>
/// <param name="Scope">The surface this target belongs to.</param>
/// <param name="ResolvedFullPath">The absolute, containment-checked location this source resolved to.</param>
/// <param name="IsContainer">Whether the target holds other targets (for the hierarchy scope: a folder).</param>
/// <param name="SourceId">The id the client used to name this target, echoed back for correlation.</param>
/// <param name="RootPath">The project root the target was resolved within; what a provider reaches the project's own state through.</param>
/// <param name="WatchId">The connection the target was resolved for; what a provider reaches per-connection state through (a viewer's fold state, say).</param>
/// <param name="ElementId">For a target inside a diagram file, the element's id; empty for a file or folder.</param>
public sealed record ContextTarget(
    ContextScope Scope,
    string ResolvedFullPath,
    bool IsContainer,
    ShortGuid SourceId,
    string RootPath = "",
    ShortGuid WatchId = default,
    string ElementId = "");
