using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// Reads a document and reports what is wrong with it, without ever refusing to read it, through
/// the seam every diagram type's rules reach the Errors and Warnings panel by (Requirement 5.5).
/// </summary>
/// <remarks>
/// <para>
/// <b>Two halves, and they are different questions.</b> The parser answers *what could I read*,
/// and records what it could not. The rule set answers *what does the notation forbid*, which
/// needs the whole document - a second parent cannot be seen from one entry. This puts the two
/// together, which is the only thing a caller wants.
/// </para>
/// <para>
/// <b>A connect command asks the rule set rather than this.</b> Validation is about a document
/// that already exists; a refusal is about a link that does not yet. They share the table
/// (<see cref="FdgRelations"/>) and nothing else, so a scripted request cannot write what the
/// canvas would not offer while a document that already contains it still opens and reports it.
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
    public DiagramOrigin Origin => Diagram.FunctionalDecompositionGraph.Origin;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var problems = Validate(LineDocument.Parse(request.Document)).Select(ToProblem).ToList();
        return ValueTask.FromResult<IReadOnlyList<DiagramProblem>>(problems);
    }

    /// <summary>Everything wrong with the document, parse problems and rule breaches together.</summary>
    public static IReadOnlyList<FdgBreach> Validate(LineDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return FdgRuleSet.Breaches(FdgParser.Parse(document));
    }

    /// <summary>One breach as the panel reads it.</summary>
    /// <remarks>
    /// <para>
    /// <b>An unreadable entry is a warning, every other breach an error.</b> The parser keeps the
    /// lines it passed over and the document still draws, so the author has lost nothing; a link
    /// the notation forbids, a second parent or an ownership loop is a graph that says something
    /// the notation cannot mean.
    /// </para>
    /// <para>
    /// <b>The location is the line</b>, one-based as the panel counts, rather than an element:
    /// every breach has a line, while its elements may be missing (a dangling reference) or
    /// ambiguous (a duplicate id). The message names the elements involved.
    /// </para>
    /// </remarks>
    private static DiagramProblem ToProblem(FdgBreach breach) => new(
        breach.RuleId == FdgRuleIds.UnreadableEntry ? DiagramProblemSeverity.Warning : DiagramProblemSeverity.Error,
        breach.Message,
        breach.RuleId,
        new DiagramProblemLineLocation((uint)Math.Max(breach.Line, 0) + 1));
}
