using EtAlii.Adp.Documents.Wire;

namespace EtAlii.Adp.Context;

/// <summary>
/// What the context stream needs from the problems machinery at the one seam where they
/// meet: a project's Watch registration. Declared here rather than in Common because only
/// Context consumes it and only the problems area implements it - the same
/// interface-where-it-is-consumed split <c>ICommandNoticeSink</c> uses in History, applied in the
/// other direction (backend-project-decomposition task 10: ContextService held three
/// Problems concretes, which would have made the two areas' projects mutually referencing).
/// </summary>
public interface IContextWatchHooks
{
    /// <summary>A project has gained a watcher: move it up the revalidation queue. Its problems are tracked by the connection's scope, which acquired them before.</summary>
    void ProjectWatched(ProjectRoot root);

    /// <summary>The problems to seed a new watcher's stream with.</summary>
    ProjectProblems CurrentFor(ProjectRoot root);
}
