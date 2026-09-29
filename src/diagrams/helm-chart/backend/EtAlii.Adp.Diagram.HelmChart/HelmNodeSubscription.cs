namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// What <see cref="HelmContextSourceResolver.Track"/> hands back: unsubscribing is the whole
/// of what disposal means here.
/// </summary>
/// <remarks>
/// A named type rather than an inline lambda holder, per tech.md's no-nested-types rule - a
/// reader meeting this in a stack trace has something to look up. Disposal is idempotent: a
/// second Dispose does nothing.
/// </remarks>
internal sealed class HelmNodeSubscription(Action unsubscribe) : IDisposable
{
    private readonly Action _unsubscribe = unsubscribe;
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _unsubscribe();
    }
}
