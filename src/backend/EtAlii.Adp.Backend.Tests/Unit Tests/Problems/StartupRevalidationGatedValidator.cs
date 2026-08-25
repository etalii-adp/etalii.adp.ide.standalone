using EtAlii.Adp.Diagram;

namespace EtAlii.Adp.Backend.Tests;

internal sealed class StartupRevalidationGatedValidator(DiagramOrigin origin) : IDiagramValidator
{
    private int _calls;

    public DiagramOrigin Origin { get; } = origin;
    public int Calls => _calls;
    public TaskCompletionSource? HoldFirstCall { get; set; }

    public async ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        string document, string baseName, CancellationToken cancellationToken)
    {
        var call = Interlocked.Increment(ref _calls);
        if (call == 1 && HoldFirstCall is not null)
        {
            await HoldFirstCall.Task;
        }
        return [];
    }
}
