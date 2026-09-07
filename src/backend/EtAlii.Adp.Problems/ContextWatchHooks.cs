using EtAlii.Adp.Context;
using EtAlii.Adp.Context.Wire;
using ContextService = EtAlii.Adp.Context.Wire.ContextService;

namespace EtAlii.Adp.Problems;

/// <summary>
/// The problems area's answer to <see cref="IContextWatchHooks"/>: when a project gains a
/// watcher its problems get tracked and its revalidation moves up the queue, and a new
/// watcher's stream is seeded with what is currently known. A thin adapter over the three
/// concretes rather than the concretes themselves in ContextService's constructor, so the
/// two areas meet at an interface Context owns (backend-project-decomposition task 10);
/// it travels with the problems area when that extracts.
/// </summary>
internal sealed class ContextWatchHooks(
    ProblemBroadcaster broadcaster,
    ProblemMaintenance maintenance,
    StartupRevalidation revalidation) : IContextWatchHooks
{
    public void ProjectWatched(string rootPath)
    {
        maintenance.Track(rootPath);
        revalidation.Prioritize(rootPath);
    }

    public ProjectProblems CurrentFor(string rootPath) => broadcaster.CurrentFor(rootPath);
}
