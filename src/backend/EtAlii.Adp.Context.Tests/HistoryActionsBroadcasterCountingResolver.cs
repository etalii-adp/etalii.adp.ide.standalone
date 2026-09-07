using EtAlii.Adp.Common;

namespace EtAlii.Adp.Context.Tests;

internal sealed class HistoryActionsBroadcasterCountingResolver : IContextActionResolver
{
    private int _discoverCount;

    public bool Throw { get; init; }

    public int DiscoverCount => Volatile.Read(ref _discoverCount);

    public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _discoverCount);
        if (Throw)
        {
            throw new InvalidOperationException("boom");
        }

        var group = new ContextActionGroupDefinition([new ContextActionDefinition("history.undo", "Undo", "mdi-undo")]);
        return ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>([group]);
    }

    // The broadcaster only discovers; these two are never reached from it.
    public ValueTask<ContextActionOwner?> ResolveByActionIdAsync(ContextTarget target, string actionId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public ValueTask<ContextActionOwner?> ResolveByShortcutAsync(ContextTarget target, ContextShortcutDefinition shortcut, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
