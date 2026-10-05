using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>
/// Reads a behavior model and reports what an agent following it would trip over, without ever
/// refusing to read it, through the seam every diagram type's rules reach the Errors and Warnings
/// panel by.
/// </summary>
/// <remarks>
/// <para>
/// <b>The findings are the DISL definition's</b> (<see cref="ConstraintEvaluator"/>, runtime plan step
/// S19b): its rules over the document's DISL model, in the order of its <c>constraints.x-order</c> -
/// the item without a keyword first, then more than one root, then the rest by their node's line.
/// </para>
/// <para>
/// <b>A file with no tree is the plugin's to say</b>: <c>abm.no-behavior</c>, information, comes from
/// what <see cref="AbmMarkdownPlugin"/> reads, after the definition's findings.
/// </para>
/// <para>
/// <b>A document that breaks a rule still opens and still draws</b>: the agent reading the file meets
/// it as written, so the diagram shows it as written and the panel names what an agent would trip over.
/// </para>
/// </remarks>
public sealed class AbmValidator : IDiagramValidator
{
    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.AgentBehaviorModelling.Origin;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var problems = Validate(AbmBody.Parse(request.Document)).Select(ToProblem).ToList();
        return ValueTask.FromResult<IReadOnlyList<DiagramProblem>>(problems);
    }

    /// <summary>Everything wrong with the document: the definition's findings, then the plugin's.</summary>
    public static IReadOnlyList<AbmBreach> Validate(AbmBody document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var findings = ConstraintEvaluator.Evaluate(AbmDefinition.Specification, document.Disl.Diagram, new DislConstraintOptions(AbmDefinition.Env));
        return
        [
            .. findings.Select(finding => new AbmBreach(
                finding.Code,
                finding.Message,
                Math.Max(0, (finding.Line ?? 1) - 1),
                IsError: finding.Severity == "error",
                IsInformation: finding.Severity is "info" or "hint")),
            .. document.Reading.Findings.Select(finding => new AbmBreach(
                finding.Code,
                finding.Message,
                Math.Max(0, (finding.Location?.Line ?? 1) - 1),
                IsError: finding.Severity == FindingSeverity.Error,
                IsInformation: finding.Severity == FindingSeverity.Info)),
        ];
    }

    /// <summary>One breach as the panel reads it; the location is the line, one-based as the panel counts.</summary>
    private static DiagramProblem ToProblem(AbmBreach breach) => new(
        breach.IsInformation ? DiagramProblemSeverity.Info : breach.IsError ? DiagramProblemSeverity.Error : DiagramProblemSeverity.Warning,
        breach.Message,
        breach.RuleId,
        new DiagramProblemLineLocation((uint)Math.Max(breach.Line, 0) + 1));
}
