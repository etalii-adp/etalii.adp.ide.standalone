using EtAlii.Adp.Backend.Context;

namespace EtAlii.Adp.Backend.Problems;

/// <summary>
/// Resolves the <c>problems</c> selection source - the errors-and-warnings panel selecting
/// itself when it takes focus (Requirement 7.4) - to a <see cref="ContextScope.ProblemsPanel"/>
/// target, which is what routes <b>Validate all</b> into the ribbon's contextual groups.
/// </summary>
/// <remarks>
/// The panel is a place, not a thing in the project: it nests under nothing, contains no
/// selectable children, and neither moves nor vanishes - so tracking it is a no-op.
/// </remarks>
public sealed class ProblemsContextSourceResolver : IContextSourceResolver
{
    private static readonly NoProblemTracking Untracked = new();

    public bool CanResolve(ContextSource source) => source.SourceCase == ContextSource.SourceOneofCase.Problems;

    public ValueTask<ContextLevelResolution> ResolveAsync(
        ShortGuid watchId,
        string rootPath,
        ContextSelectionSource source,
        ContextSource id,
        IReadOnlyList<string> clientPath,
        ContextResolvedLevel? parent,
        CancellationToken cancellationToken)
    {
        if (parent is not null)
        {
            return ValueTask.FromResult<ContextLevelResolution>(new RejectedContextLevel("The problems panel nests under nothing."));
        }

        var target = new ContextTarget(ContextScope.ProblemsPanel, rootPath, IsContainer: false, SourceId: default, RootPath: rootPath, WatchId: watchId);
        var level = new ContextResolvedLevel(source, id, [], ContextScope.ProblemsPanel, target, new ContextLevelDetail(), this);
        return ValueTask.FromResult<ContextLevelResolution>(new ResolvedContextLevel(level));
    }

    public ContextNesting NestingOf(ContextResolvedLevel level) => ContextNesting.NotNestable;

    public IDisposable Track(ShortGuid watchId, string rootPath, ContextResolvedLevel level, Action<IReadOnlyList<string>?> onChange) => Untracked;

}
