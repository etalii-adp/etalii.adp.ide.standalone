namespace EtAlii.Adp;

/// <summary>Disposables made from a delegate (code-structure FR-032).</summary>
public static class Disposable
{
    /// <summary>
    /// A disposable that runs <paramref name="dispose"/> once, on the first <see cref="IDisposable.Dispose"/>,
    /// whichever thread gets there first: the shape of the private unsubscriber types it replaces.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="CallbackDisposable"/>, which runs its callback on every call, this one is
    /// idempotent, because an observation may be stopped both by its owner and by a teardown that
    /// does not know the owner already did.
    /// </remarks>
    public static IDisposable From(Action dispose)
    {
        ArgumentNullException.ThrowIfNull(dispose);
        return new Once(dispose);
    }

    private sealed class Once(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
