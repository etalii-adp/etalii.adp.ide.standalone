namespace EtAlii.Adp.Files;

/// <summary>
/// What <see cref="WatchedFolderStore{TSnapshot}.Watch"/> hands back: unsubscribing is the whole
/// of what disposal means here.
/// </summary>
/// <remarks>
/// Kept apart from the shared <see cref="CallbackDisposable"/> deliberately: disposal here is
/// idempotent - a second Dispose does nothing - which that type does not guarantee.
/// </remarks>
internal sealed class WatchedFolderSubscription(Action unsubscribe) : IDisposable
{
    private int _disposed;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            unsubscribe();
        }
    }
}
