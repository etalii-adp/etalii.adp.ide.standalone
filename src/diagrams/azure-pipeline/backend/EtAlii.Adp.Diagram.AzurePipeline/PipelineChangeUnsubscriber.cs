namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// Detaches a document-change handler when the thing that attached it goes away.
/// </summary>
/// <remarks>
/// The context mechanism hands back an <see cref="IDisposable"/> for tracking, and the store's
/// event is a plain .NET event - so something has to carry the unsubscribe across. A named type
/// rather than a lambda-holding helper because nested types are not used here, and because a
/// selection that never unsubscribes keeps a whole document graph alive. Kept separate from
/// the shared <see cref="CallbackDisposable"/> deliberately: disposal here is idempotent -
/// at most once, however often it is called - and the constructor null-checks its argument,
/// neither of which the shared type does.
/// </remarks>
public sealed class PipelineChangeUnsubscriber : IDisposable
{
    private Action? _detach;

    /// <summary>Creates an unsubscriber that runs <paramref name="detach"/> once.</summary>
    public PipelineChangeUnsubscriber(Action detach)
    {
        ArgumentNullException.ThrowIfNull(detach);
        _detach = detach;
    }

    /// <summary>Detaches, at most once however often this is called.</summary>
    public void Dispose()
    {
        var detach = _detach;
        _detach = null;
        detach?.Invoke();
    }
}
