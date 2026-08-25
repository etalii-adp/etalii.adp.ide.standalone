using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Backend.Problems;
using EtAlii.Adp.Diagram;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

internal sealed class ProjectValidatorTestValidator(
    DiagramOrigin origin, IReadOnlyList<DiagramProblem> problems, bool throwing, bool hanging) : IDiagramValidator
{
    private int _calls;

    public DiagramOrigin Origin { get; } = origin;
    public int Calls => _calls;
    public TaskCompletionSource? Hold { get; set; }

    public async ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        string document, string baseName, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        if (Hold is not null)
        {
            await Hold.Task;
        }
        if (throwing)
        {
            throw new InvalidOperationException("This module is broken.");
        }
        if (hanging)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);
        }
        return problems;
    }
}
