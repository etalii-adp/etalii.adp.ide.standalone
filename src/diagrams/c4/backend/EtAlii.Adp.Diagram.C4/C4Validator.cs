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
        var body = C4Body.Read(request.Document);
        var workspace = body.Workspace;
        IReadOnlyList<DiagramProblem> problems = [.. C4Definitions.Problems(workspace, C4Body.FileOf(request.BodyPath), Origin), .. LayoutProblems(request.BodyPath, body)];
        return ValueTask.FromResult(problems);
    }

    /// <summary>
    /// The entries of the layout file beside the body under ids the body does not store, reported so that the position the
    /// next placement drops is not lost unseen (Q7). They are about the layout file, not a line of the body, so they have no
    /// location.
    /// </summary>
    private static IEnumerable<DiagramProblem> LayoutProblems(string bodyPath, C4Body body) =>
        bodyPath is { Length: > 0 }
            ? C4Positions.Findings(bodyPath, body.Workspace.Views.Select(view => view.Key), body.StoredIds())
                .Select(each => new DiagramProblem(DiagramProblemSeverity.Warning, $"View '{each.ViewKey}': {each.Finding.Message}", each.Finding.Code))
            : [];
}
