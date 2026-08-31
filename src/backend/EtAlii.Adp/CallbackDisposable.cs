namespace EtAlii.Adp;

/// <summary>
/// The trivial "dispose runs this callback" disposable: <see cref="Dispose"/> invokes
/// <paramref name="dispose"/>, every time it is called. Deliberately not idempotent and
/// deliberately without an argument null-check - a caller that needs a guarded dispose
/// keeps its own type and says so (see the azure-pipeline and ansible-structure modules).
/// </summary>
public sealed class CallbackDisposable(Action dispose) : IDisposable
{
    public void Dispose() => dispose();
}
