namespace EtAlii.Adp.Backend.Context;

/// <summary>
/// Turns a client's selection chain into resolved levels by walking it outer to inner
/// through whichever <see cref="IContextSourceResolver"/> answers for each level's id,
/// handing every level its already-resolved parent so nesting rules can be enforced.
/// It knows no concrete kind of thing: a new kind is one more registered resolver.
/// </summary>
/// <remarks>
/// Every failure is answered with the same generic reason, so a caller learns nothing
/// about ids it may not see - the same rule the context actions already follow.
/// </remarks>
public sealed class ContextSelectionResolver
{
    /// <summary>Deeper chains than this are almost certainly a client bug, not a selection.</summary>
    public const int MaxDepth = 8;

    private const string GenericRejection = "This item is no longer available.";

    private readonly IReadOnlyList<IContextSourceResolver> _resolvers;

    public ContextSelectionResolver(IEnumerable<IContextSourceResolver> resolvers)
    {
        _resolvers = resolvers.ToList();
    }

    /// <summary>
    /// Resolves the whole chain, or nothing: a rejection at any level discards what was
    /// resolved before it. The returned record carries no actions and no tracks yet -
    /// the service adds those once it knows the chain is good.
    /// </summary>
    public async ValueTask<ChainResolution> ResolveChainAsync(
        ShortGuid watchId, string rootPath, ContextSelection selection, CancellationToken cancellationToken)
    {
        var levels = new List<ContextResolvedLevel>();
        var acceptedChain = new ContextSelection();
        var acceptedCursor = acceptedChain;
        ContextResolvedLevel? parent = null;
        ContextSelectionAction? action = null;

        var current = selection;
        while (true)
        {
            if (levels.Count >= MaxDepth)
            {
                return new RejectedChain(GenericRejection);
            }

            var resolution = await ResolveLevelAsync(
                watchId, rootPath, current.Source, current.Id, current.Path?.Segments ?? [], parent, cancellationToken);
            if (resolution is not ResolvedContextLevel resolved)
            {
                return new RejectedChain(((RejectedContextLevel)resolution).Reason);
            }

            var level = resolved.Level;
            levels.Add(level);

            acceptedCursor.Source = current.Source;
            acceptedCursor.Id = current.Id;
            acceptedCursor.Path = new Path();
            acceptedCursor.Path.Segments.AddRange(level.RelativePath);

            switch (current.DetailCase)
            {
                case ContextSelection.DetailOneofCase.Child:
                    if (level.Resolver.NestingOf(level) == ContextNesting.NotNestable)
                    {
                        return new RejectedChain(GenericRejection);
                    }

                    acceptedCursor.Child = new ContextSelection();
                    acceptedCursor = acceptedCursor.Child;
                    parent = level;
                    current = current.Child;
                    continue;

                case ContextSelection.DetailOneofCase.Action:
                    action = current.Action;
                    acceptedCursor.Action = current.Action;
                    break;

                default:
                    // An unset oneof is treated as `none`: a minimal client that sends
                    // nothing still makes a plain selection rather than being rejected.
                    acceptedCursor.None = new Google.Protobuf.WellKnownTypes.Empty();
                    break;
            }

            break;
        }

        return new ResolvedChain(new ContextSelectionRecord(acceptedChain, levels, [], action, []));
    }

    /// <summary>
    /// Resolves a single level - what <c>ExecuteAction</c>/<c>DiscoverActions</c> need
    /// when a caller names its target explicitly.
    /// </summary>
    public ValueTask<ContextLevelResolution> ResolveLevelAsync(
        ShortGuid watchId,
        string rootPath,
        ContextSelectionSource source,
        ContextSource? id,
        IReadOnlyList<string> clientPath,
        ContextResolvedLevel? parent,
        CancellationToken cancellationToken)
    {
        if (id is null || id.SourceCase == ContextSource.SourceOneofCase.None)
        {
            return ValueTask.FromResult<ContextLevelResolution>(new RejectedContextLevel(GenericRejection));
        }

        var candidates = _resolvers.Where(candidate => candidate.CanResolve(id)).ToArray();
        if (candidates.Length == 0)
        {
            // No resolver means no way to verify anything about this level; recording
            // it on trust is exactly what the seam exists to prevent.
            return ValueTask.FromResult<ContextLevelResolution>(new RejectedContextLevel(GenericRejection));
        }

        return ResolveThroughAsync(candidates, watchId, rootPath, source, id, clientPath, parent, cancellationToken);
    }

    /// <summary>Asks each resolver that answers for this member, in turn, until one accepts.</summary>
    /// <remarks>
    /// Several may answer for one member while only one of them owns the thing: every diagram
    /// type resolves an <c>element_id</c>, and none of them can tell from the id alone whether
    /// the element is one of its own - only the level above it says that. Asking just the first
    /// would make element selection work for whichever module happened to be registered first,
    /// and silently stop working for the rest.
    /// </remarks>
    private static async ValueTask<ContextLevelResolution> ResolveThroughAsync(
        IReadOnlyList<IContextSourceResolver> resolvers,
        ShortGuid watchId,
        string rootPath,
        ContextSelectionSource source,
        ContextSource id,
        IReadOnlyList<string> clientPath,
        ContextResolvedLevel? parent,
        CancellationToken cancellationToken)
    {
        foreach (var resolver in resolvers)
        {
            var resolution = await resolver.ResolveAsync(watchId, rootPath, source, id, clientPath, parent, cancellationToken);
            if (resolution is not RejectedContextLevel)
            {
                return resolution;
            }
        }

        // A resolver may word its own reason for logging; the caller only ever sees the generic one.
        return new RejectedContextLevel(GenericRejection);
    }
}
