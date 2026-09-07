using EtAlii.Adp.Common;

namespace EtAlii.Adp.Problems.Tests;

internal sealed class ProblemMaintenanceCountingValidator(DiagramOrigin origin) : IDiagramValidator
{
    private int _calls;

    public DiagramOrigin Origin { get; } = origin;
    public int Calls => _calls;

    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return ValueTask.FromResult<IReadOnlyList<DiagramProblem>>([]);
    }
}
