using EtAlii.Adp.Diagram;

namespace EtAlii.Adp.Problems.Tests;

internal sealed class ProblemStoreReportingValidator(ToolOrigin origin) : IDiagramValidator
{
    public ToolOrigin Origin { get; } = origin;

    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        DiagramValidationRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<DiagramProblem>>(
            [new DiagramProblem(DiagramProblemSeverity.Warning, "Something to remember.", "test.remember")]);
}
