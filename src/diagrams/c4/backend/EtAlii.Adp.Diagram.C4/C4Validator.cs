namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// Adapts the findings of the C4 definitions (<see cref="C4Definitions.Problems"/>) to core's validator
/// seam, so C4's rules reach the errors and warnings panel like any other type's. One instance per C4
/// diagram type, because core resolves validators by origin, and each evaluates its own type's
/// definition; the rules are about the model, so the six give one list (c4-disl-fbl Requirement 3.3).
/// </summary>
/// <remarks>
/// The rules themselves stay a pure function over a parsed workspace. This class only parses
/// and forwards, which is what keeps every rule testable without a file (c4-diagrams
/// Requirement 10, and errors-and-warnings-panel Requirement 3.1).
/// </remarks>
public sealed class C4Validator : IDiagramValidator
{
    public C4Validator(ToolOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        Origin = origin;
    }

    public ToolOrigin Origin { get; }

    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();
        var workspace = C4Body.Read(request.Document).Workspace;
        return ValueTask.FromResult(C4Definitions.Problems(workspace, C4Body.FileOf(request.BodyPath), Origin));
    }
}
