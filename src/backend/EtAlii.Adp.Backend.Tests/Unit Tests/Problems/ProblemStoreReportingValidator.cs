using System.Reflection;
using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Backend.Problems;
using EtAlii.Adp.Diagram;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

internal sealed class ProblemStoreReportingValidator(DiagramOrigin origin) : IDiagramValidator
{
    public DiagramOrigin Origin { get; } = origin;

    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        string document, string baseName, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<DiagramProblem>>(
            [new DiagramProblem(DiagramProblemSeverity.Warning, "Something to remember.", "test.remember")]);
}
