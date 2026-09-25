using EtAlii.Adp.Documents.Wire;

namespace EtAlii.Adp.Context;

/// <summary>
/// What the context stream needs from the problems machinery at the one seam where they
/// meet: a project's Watch registration. Declared here rather than in Common because only
/// Context consumes it and only the problems area implements it - the same
/// interface-where-it-is-consumed split <c>IContextNoticeSink</c> uses, applied in the
/// other direction (backend-project-decomposition task 10: ContextService held three
/// Problems concretes, which would have made the two areas' projects mutually referencing).
/// </summary>
public interface IContextWatchHooks
{
    /// <summary>A project has gained a watcher: start tracking its problems and move it up the revalidation queue.</summary>
    void ProjectWatched(string rootPath);

    /// <summary>The problems to seed a new watcher's stream with.</summary>
    ProjectProblems CurrentFor(string rootPath);
}
