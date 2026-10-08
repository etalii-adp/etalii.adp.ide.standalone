using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// Reads a document and reports what is wrong with it, without ever refusing to read it - and the
/// seam that carries each breach to the Errors and Warnings panel (Requirement 2.4).
/// </summary>
/// <remarks>
/// <para>
/// <b>The findings are the DISL definition's</b> (<see cref="ConstraintEvaluator"/>, runtime plan step
/// S12): its live invariants and its built-ins over the document's DISL model, in the order of its
/// <c>constraints.order</c>. What the reader could not read - an unknown key, a value that is not a
/// date or a number, an entry that is not a mapping, a body that is not YAML - is the parser's to say
/// (<see cref="GhgParser.ReaderFindings"/>); the definition gives those their code and wording.
/// </para>
/// <para>
/// <b>An element is named by the id written in the document</b>, its <c>storedId</c>: a later entry
/// reusing an id has an ephemeral id in the model, but the panel names it as the file does.
/// </para>
/// <para>
/// <b>Registered as the module's <see cref="IDiagramValidator"/></b>, which is where this module
/// parts from FDG: FDG's validator is reachable from its tests only, while Requirement 2.4 here asks
/// for every breach in the panel.
/// </para>
/// </remarks>
public sealed class GhgValidator : IDiagramValidator
{
    /// <summary>Everything wrong with the document, reading problems and rule breaches together.</summary>
    public static IReadOnlyList<GhgBreach> Validate(GhgBody document) =>
        [.. Findings(document).Select(finding => new GhgBreach(finding.Code, finding.Message, Math.Max(0, (finding.Line ?? 1) - 1)))];

    /// <summary>The definition's findings for <paramref name="document"/>.</summary>
    private static IReadOnlyList<DislFinding> Findings(GhgBody document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var options = new DislConstraintOptions(
            new DislEnv(Viewpoint: GhgDefinition.Viewpoint),
            GhgParser.ReaderFindings(document, GhgParser.Parse(document)),
            GhgDefinition.WrittenId);
        return ConstraintEvaluator.Evaluate(GhgDefinition.Specification, document.Disl.Diagram, options);
    }

    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.HypeCycleGraph.Origin;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<DiagramProblem> problems =
        [
            .. Findings(GhgBody.Parse(request.Document)).Select(finding => new DiagramProblem(
                Severity(finding.Severity),
                finding.Message,
                finding.Code,
                new DiagramProblemLineLocation((uint)Math.Max(1, finding.Line ?? 1)))),
        ];

        return ValueTask.FromResult(problems);
    }

    private static DiagramProblemSeverity Severity(string severity) => severity switch
    {
        "error" => DiagramProblemSeverity.Error,
        "info" or "hint" => DiagramProblemSeverity.Info,
        _ => DiagramProblemSeverity.Warning,
    };
}
