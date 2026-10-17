using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// What this reading has to say about a causal loop diagram - which is only ever about the
/// document, never a correction to it (causal-loop-diagram Requirements 3.3, 3.4, 3.5).
/// </summary>
/// <remarks>
/// <para>
/// <b>The stated label and the computed one are reported together and neither is overwritten.</b>
/// That is the whole discipline here. A loop the document calls <c>R2</c> whose arrows carry an
/// odd number of negatives is a disagreement between two claims, and this module does not know
/// which one the author meant: the label may be the intent and an arrow the typo, or the arrows
/// may be right and the label stale. Silently rewriting either would destroy the only evidence
/// that anything was wrong.
/// </para>
/// <para>
/// <b>The findings are the DISL definition's</b> (causal-loop-disl-fbl Requirement 3.3):
/// <see cref="CausalLoopDefinition.Findings"/> evaluates its rules and built-ins over the body as the
/// binding reads it, with the tool's codes, severities, sentences, lines and order. A finding with a
/// line is located on it; one without, a cycle no loop names or the bound, has no location.
/// </para>
/// <para>
/// <b>An unlabelled cycle is the finding this notation most exists to produce.</b> A feedback
/// loop nobody noticed is the thing a causal loop diagram is drawn to find, and a tool that can
/// enumerate cycles and does not mention the ones the author never named has kept the answer to
/// itself.
/// </para>
/// </remarks>
public sealed class CausalLoopValidator(DiagramOrigin origin) : IDiagramValidator
{
    /// <summary>A line the grammar could not read.</summary>
    public const string UnreadableRuleId = "causal-loop.unreadable-line";

    /// <summary>A link naming a variable the document never declares.</summary>
    public const string DanglingLinkRuleId = "causal-loop.dangling-link";

    /// <summary>A loop whose stated identifier disagrees with what its arrows compute to.</summary>
    public const string LabelDisagreesRuleId = "causal-loop.label-disagrees";

    /// <summary>A cycle the arrows form that the document labels not at all.</summary>
    public const string UnlabelledLoopRuleId = "causal-loop.unlabelled-loop";

    /// <summary>A loop whose polarity cannot be counted, because some link around it states none.</summary>
    public const string UndecidableLoopRuleId = "causal-loop.undecidable-loop";

    /// <summary>A loop statement naming a path the arrows do not form.</summary>
    public const string LoopIsNotACycleRuleId = "causal-loop.loop-is-not-a-cycle";

    /// <inheritdoc />
    public DiagramOrigin Origin { get; } = origin;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<DiagramProblem> problems =
        [
            .. CausalLoopDefinition.Findings(CausalLoopBody.Parse(request.Document)).Select(finding => new DiagramProblem(
                DislDiagramDefinition.SeverityOf(finding),
                finding.Message,
                finding.Code,
                finding.Line is { } line ? new DiagramProblemLineLocation((uint)line) : null)),
        ];

        return ValueTask.FromResult(problems);
    }
}
