using EtAlii.Adp.Diagram;

namespace EtAlii.Adp.Problems.Tests;

internal sealed class StartupRevalidationGatedValidator(ToolOrigin origin) : IDiagramValidator
{
    private int _calls;

    public ToolOrigin Origin { get; } = origin;
    public int Calls => _calls;
    public TaskCompletionSource? HoldFirstCall { get; set; }

    public async ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        var call = Interlocked.Increment(ref _calls);
        if (call == 1 && HoldFirstCall is not null)
        {
            await HoldFirstCall.Task;
        }
        return [];
    }
}
