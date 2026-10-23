using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// Judges a query file, through the seam every diagram type's rules reach the Errors and
/// Warnings panel by (Requirement 7): facts about the text, and nothing else. It never executes
/// the query, never contacts an endpoint, and never resolves a <c>SERVICE</c> clause - not as a
/// policy applied here, but because this module references no HTTP client at all.
/// </summary>
/// <remarks>
/// <b>The findings are the definition's</b> (<see cref="SparqlDefinition.Findings"/>, sparql-disl-fbl, task 8): a query
/// the parser cannot read is the one finding <c>std.unparseable</c>, which the definition words with the parser's
/// sentence at the line it stopped at; any other query is judged by the definition's rules. This holds no rule, code or
/// sentence of its own.
/// </remarks>
public sealed class SparqlValidator(ToolOrigin origin) : IDiagramValidator
{
    /// <inheritdoc />
    public ToolOrigin Origin { get; } = origin;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract - Reason: IDiagramValidator.ValidateAsync is a public module seam and the record's non-nullable Document is a compile-time annotation the runtime does not enforce, so a caller that passes null (null!, or code built without nullable checks) gets an empty document here rather than a NullReferenceException; ProjectValidator, the one caller in this repository, always passes the text it read, so an empty file arrives as "", not null.
        var text = request.Document ?? "";
        IReadOnlyList<DiagramProblem> problems = [.. Findings(text).Select(finding => new DiagramProblem(
            DislDiagramDefinition.SeverityOf(finding),
            finding.Message,
            finding.Code,
            new DiagramProblemLineLocation((uint)Math.Max(1, finding.Line ?? 1))))];
        return ValueTask.FromResult(problems);
    }

    /// <summary>The definition's findings over <paramref name="text"/>, read as the store reads it.</summary>
    internal static IReadOnlyList<DislFinding> Findings(string text)
    {
        var outcome = SparqlParser.Outcome(text);
        var document = new SparqlDocument(outcome.Model, text);
        if (outcome.IsUsable)
        {
            return SparqlDefinition.Findings(document);
        }

        // One finding, not a list of consequences: running the rules over an empty model would bury
        // the reason the file could not be read (Requirement 7.1).
        var problem = outcome.Problems[0];
        return SparqlDefinition.Findings(document, DislReaderFinding.NotParsed(problem.Message, problem.Line + 1));
    }
}
