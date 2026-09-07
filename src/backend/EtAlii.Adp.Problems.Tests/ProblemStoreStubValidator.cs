using EtAlii.Adp.Common;

namespace EtAlii.Adp.Problems.Tests;

internal sealed class ProblemStoreStubValidator(DiagramOrigin origin) : IDiagramValidator
{
    public DiagramOrigin Origin { get; } = origin;

    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        DiagramValidationRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<DiagramProblem>>([]);
}
