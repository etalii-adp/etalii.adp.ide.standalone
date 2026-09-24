using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Context.Wire;
using Path = EtAlii.Adp.Documents.Wire.Path;
namespace EtAlii.Adp.Context;

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

        var claimants = _resolvers.Where(candidate => candidate.CanResolve(id)).ToList();
        if (claimants.Count == 0)
        {
            // No resolver means no way to verify anything about this level; recording
            // it on trust is exactly what the seam exists to prevent.
            return ValueTask.FromResult<ContextLevelResolution>(new RejectedContextLevel(GenericRejection));
        }

        return ResolveThroughAnyAsync(claimants, watchId, rootPath, source, id, clientPath, parent, cancellationToken);
    }

    /// <summary>
    /// Offers the level to each resolver that claims its id shape, and takes the first that
    /// actually accepts it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="IContextSourceResolver.CanResolve"/> answers a question about the <em>shape</em>
    /// of an id - "is this an element id?" - and every diagram type answers yes to that one. Which
    /// type an element belongs to cannot be known from the id at all: it depends on the file the
    /// enclosing level names, which only <see cref="IContextSourceResolver.ResolveAsync"/> looks
    /// at. So the first claimant is not the owner, it is merely the first to be asked.
    /// </para>
    /// <para>
    /// Taking the first claimant meant whichever module registered earliest silently owned every
    /// element selection in the application and rejected the rest - a C4 element could not be
    /// selected at all, because the mindmap resolver claimed it first and then correctly observed
    /// that the file was not a mindmap. Nothing caught it: every test that selected an element
    /// selected a mindmap one.
    /// </para>
    /// <para>
    /// When every claimant refuses, the refusal is the generic one every rejection carries -
    /// <see cref="ResolveThroughAsync"/> replaces each resolver's own wording on the way out, so
    /// a caller learns nothing about ids it may not see. That rule is deliberate and this does not
    /// weaken it: asking several resolvers must not become a way to find out which module
    /// recognised the file.
    /// </para>
    /// </remarks>
    private static async ValueTask<ContextLevelResolution> ResolveThroughAnyAsync(
        IReadOnlyList<IContextSourceResolver> claimants,
        ShortGuid watchId,
        string rootPath,
        ContextSelectionSource source,
        ContextSource id,
        IReadOnlyList<string> clientPath,
        ContextResolvedLevel? parent,
        CancellationToken cancellationToken)
    {
        ContextLevelResolution? firstRefusal = null;
        foreach (var claimant in claimants)
        {
            var resolution = await ResolveThroughAsync(
                claimant, watchId, rootPath, source, id, clientPath, parent, cancellationToken);
            if (resolution is ResolvedContextLevel)
            {
                return resolution;
            }

            firstRefusal ??= resolution;
        }

        return firstRefusal ?? new RejectedContextLevel(GenericRejection);
    }

    private static async ValueTask<ContextLevelResolution> ResolveThroughAsync(
        IContextSourceResolver resolver,
        ShortGuid watchId,
        string rootPath,
        ContextSelectionSource source,
        ContextSource id,
        IReadOnlyList<string> clientPath,
        ContextResolvedLevel? parent,
        CancellationToken cancellationToken)
    {
        var resolution = await resolver.ResolveAsync(watchId, rootPath, source, id, clientPath, parent, cancellationToken);

        // A resolver may word its own reason for logging; the caller only ever sees the generic one.
        return resolution is RejectedContextLevel
            ? new RejectedContextLevel(GenericRejection)
            : resolution;
    }
}
