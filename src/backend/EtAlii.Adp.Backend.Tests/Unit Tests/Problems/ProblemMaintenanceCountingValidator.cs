using EtAlii.Adp.Diagram;

namespace EtAlii.Adp.Backend.Tests;

internal sealed class ProblemMaintenanceCountingValidator(DiagramOrigin origin) : IDiagramValidator
{
    private int _calls;

    public DiagramOrigin Origin { get; } = origin;
    public int Calls => _calls;

    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        string document, string baseName, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return ValueTask.FromResult<IReadOnlyList<DiagramProblem>>([]);
    }
}
