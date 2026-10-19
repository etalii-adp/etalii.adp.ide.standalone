using EtAlii.Adp.Documents.Wire;

namespace EtAlii.Adp.Context;

/// <summary>
/// Resolves the <c>project</c> source - the project itself, which the History group and the
/// global undo and redo shortcuts name as their target (diagram-undo-redo Requirement 5.1) - to
/// the <see cref="ContextScope.Project"/> target the project-scope providers answer for.
/// </summary>
/// <remarks>
/// Like the problems panel's, the project is a place rather than a thing in it: it nests under
/// nothing, has no selectable children through this source, and neither moves nor vanishes
/// while it is open - so tracking it is a no-op.
/// </remarks>
public sealed class ProjectContextSourceResolver : IContextSourceResolver
{
    private static readonly Untracked NotTracked = new();

    public bool CanResolve(ContextSource source) => source.SourceCase == ContextSource.SourceOneofCase.Project;

    public ValueTask<ContextLevelResolution> ResolveAsync(
        ShortGuid watchId,
        ProjectRoot root,
        ContextSource id,
        IReadOnlyList<string> clientPath,
        ContextResolvedLevel? parent,
        CancellationToken cancellationToken)
    {
        if (parent is not null)
        {
            return ValueTask.FromResult<ContextLevelResolution>(new RejectedContextLevel("The project nests under nothing."));
        }

        var target = HistoryActionsBroadcaster.ProjectTarget(root);
        var level = new ContextResolvedLevel(id, [], ContextScope.Project, target, new ContextLevelDetail(), this);
        return ValueTask.FromResult<ContextLevelResolution>(new ResolvedContextLevel(level));
    }

    public ContextNesting NestingOf(ContextResolvedLevel level) => ContextNesting.NotNestable;

    public IDisposable Track(ShortGuid watchId, ProjectRoot root, ContextResolvedLevel level, Action<IReadOnlyList<string>?> onChange) => NotTracked;

    private sealed class Untracked : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
