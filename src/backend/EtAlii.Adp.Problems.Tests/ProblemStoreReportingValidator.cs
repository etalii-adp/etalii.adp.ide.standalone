using EtAlii.Adp.Diagram;
using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Problems.Tests;

internal sealed class ProblemStoreReportingValidator(DiagramOrigin origin) : IDiagramValidator
{
    public DiagramOrigin Origin { get; } = origin;

    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        DiagramValidationRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<DiagramProblem>>(
            [new DiagramProblem(DiagramProblemSeverity.Warning, "Something to remember.", "test.remember")]);
}
