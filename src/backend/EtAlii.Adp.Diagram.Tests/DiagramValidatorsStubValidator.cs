namespace EtAlii.Adp.Diagram.Tests;

internal sealed class DiagramValidatorsStubValidator(ToolOrigin origin) : IDiagramValidator
{
    public ToolOrigin Origin { get; } = origin;

    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        DiagramValidationRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<DiagramProblem>>([]);
}
