namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// What <see cref="AnsibleContextSourceResolver.Track"/> hands back: unsubscribing is the whole
/// of what disposal means here.
/// </summary>
/// <remarks>
/// A named type rather than an inline lambda holder, per tech.md's no-nested-types rule - a
/// reader meeting this in a stack trace has something to look up. Kept separate from the
/// shared <see cref="CallbackDisposable"/> deliberately: disposal here is idempotent - a
/// second Dispose does nothing - which the shared type does not guarantee.
/// </remarks>
internal sealed class AnsibleNodeSubscription(Action unsubscribe) : IDisposable
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
