using EtAlii.Adp.Diagram;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// Adapts <see cref="C4RuleSet"/> to core's validator seam, so C4's rules reach the errors and
/// warnings panel like any other type's. One instance per C4 diagram type, because core
/// resolves validators by origin.
/// </summary>
/// <remarks>
/// The rules themselves stay a pure function over a parsed workspace. This class only parses
/// and forwards, which is what keeps every rule testable without a file (c4-diagrams
/// Requirement 10, and errors-and-warnings-panel Requirement 3.1).
/// </remarks>
public sealed class C4Validator : IDiagramValidator
{
    public C4Validator(DiagramOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        Origin = origin;
    }

    public DiagramOrigin Origin { get; }

    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(string document, string baseName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        cancellationToken.ThrowIfCancellationRequested();
        var workspace = C4Parser.Parse(C4Document.Parse(document));
        return ValueTask.FromResult(C4RuleSet.Validate(workspace));
    }
}
