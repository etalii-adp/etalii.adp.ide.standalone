using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// Reads a document and reports what is wrong with it, without ever refusing to read it, through
/// the seam every diagram type's rules reach the Errors and Warnings panel by (Requirement 5.5).
/// </summary>
/// <remarks>
/// <para>
/// <b>The findings are the DISL definition's</b> (<see cref="FdgDefinition.Findings"/>): its built-ins and
/// rules over the document's DISL model, with the reader's own sentences, held to the tool's findings by
/// the stand-ins each named for its gap or ruling. The eight codes, their sentences and lines are the
/// tool's; <c>fdg.unreadable-entry</c> is a warning and every other code an error, as the definition states.
/// </para>
/// <para>
/// <b>The location is the line</b>, one-based as the panel counts, rather than an element: every finding
/// has a line, while its elements may be missing (a dangling reference) or ambiguous (a duplicate id).
/// The message names the elements involved.
/// </para>
/// <para>
/// <b>Until this implemented <see cref="IDiagramValidator"/> nothing reached the panel.</b> The
/// rules were written and tested, but the class was static and never registered, so a file edited
/// by hand into a broken state opened silently. The registration test names this seam so that
/// cannot come back unnoticed.
/// </para>
/// </remarks>
public sealed class FdgValidator : IDiagramValidator
{
    /// <inheritdoc />
    public ToolOrigin Origin => Diagram.FunctionalDecompositionGraph.Origin;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<DiagramProblem> problems = [.. Validate(LineDocument.Parse(request.Document)).Select(ToProblem)];
        return ValueTask.FromResult(problems);
    }

    /// <summary>Everything wrong with the document, reading problems and rule breaches together.</summary>
    internal static IReadOnlyList<DislFinding> Validate(LineDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return FdgDefinition.Findings(FdgParser.Parse(document));
    }

    private static DiagramProblem ToProblem(DislFinding finding) => new(
        DislDiagramDefinition.SeverityOf(finding),
        finding.Message,
        finding.Code,
        new DiagramProblemLineLocation((uint)Math.Max(1, finding.Line ?? 1)));
}
