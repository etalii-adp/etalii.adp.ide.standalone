using System.Reflection;
using Xunit;

namespace EtAlii.Adp.Diagram.Tests;

internal sealed class DiagramValidatorsStubValidator(DiagramOrigin origin) : IDiagramValidator
{
    public DiagramOrigin Origin { get; } = origin;

    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        string document, string baseName, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<DiagramProblem>>([]);
}
