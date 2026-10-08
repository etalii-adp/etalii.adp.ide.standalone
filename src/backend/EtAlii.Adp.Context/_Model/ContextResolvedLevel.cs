using EtAlii.Adp.Documents.Wire;
namespace EtAlii.Adp.Context;

/// <summary>
/// One level of a selection after the backend resolved it: where it is, what it is, and
/// which resolver vouched for it. Everything a provider or a watcher needs, with the
/// absolute location kept inside <see cref="Target"/> and never sent to a client.
/// </summary>
/// <param name="Id">The id the client used to name it.</param>
/// <param name="RelativePath">Project-relative (outermost) or parent-relative (nested) segments; what the client receives.</param>
/// <param name="Scope">The provider scope this level's kind routes to.</param>
/// <param name="Target">The containment-checked absolute location, for providers.</param>
/// <param name="Detail">Backend-resolved detail for the client (kind, availability, ...).</param>
/// <param name="Resolver">The resolver that produced this level; also the one that tracks it.</param>
public sealed record ContextResolvedLevel(
    ContextSource Id,
    IReadOnlyList<string> RelativePath,
    ContextScope Scope,
    ContextTarget Target,
    ContextLevelDetail Detail,
    IContextSourceResolver Resolver);
