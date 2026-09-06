using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.CausalLoop;

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

    /// <summary>The cycle search stopped at its bound before finishing.</summary>
    public const string CycleBoundRuleId = "causal-loop.cycle-bound-reached";

    /// <inheritdoc />
    public DiagramOrigin Origin { get; } = origin;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var parsed = CausalLoopParser.Parse(CausalLoopDocument.Parse(request.Document));
        var model = parsed.Model;
        var problems = new List<DiagramProblem>();

        foreach (var problem in parsed.Problems)
        {
            problems.Add(new DiagramProblem(
                DiagramProblemSeverity.Warning,
                problem.Message,
                UnreadableRuleId,
                new DiagramProblemLineLocation((uint)problem.Lines.Start + 1)));
        }

        foreach (var link in model.Links)
        {
            foreach (var end in new[] { link.From, link.To }.Where(end => !model.Declares(end)))
            {
                problems.Add(new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"The link from '{link.From}' to '{link.To}' names '{end}', which this document does not declare as a variable, so the link is not drawn.",
                    DanglingLinkRuleId,
                    new DiagramProblemLineLocation((uint)link.Lines.Start + 1)));
            }
        }

        var found = CycleFinder.Find(model);
        if (found.Truncated)
        {
            problems.Add(new DiagramProblem(
                DiagramProblemSeverity.Info,
                $"This diagram contains more feedback loops than are examined; {found.Examined} were read and the rest were not, so any claim below about unlabelled loops covers only those.",
                CycleBoundRuleId));
        }

        AddStatedLoopFindings(model, found, problems);
        AddUnlabelledCycleFindings(model, found, problems);

        return ValueTask.FromResult<IReadOnlyList<DiagramProblem>>(problems);
    }

    /// <summary>What the document claims, checked against what its arrows compute to.</summary>
    private static void AddStatedLoopFindings(
        CausalLoopModel model, CycleFinderResult found, List<DiagramProblem> problems)
    {
        foreach (var loop in model.Loops)
        {
            var line = new DiagramProblemLineLocation((uint)loop.Lines.Start + 1);

            if (!IsACycle(found, loop.Variables))
            {
                problems.Add(new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"Loop {loop.Identifier} names a path the links do not close into a cycle, so nothing can be said about whether it reinforces or balances.",
                    LoopIsNotACycleRuleId,
                    line));
                continue;
            }

            var computed = LoopPolarity.Of(model, loop.Variables);
            if (computed == LoopPolarityResult.Undecidable)
            {
                problems.Add(new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"Loop {loop.Identifier} runs through a link with no stated polarity, so whether it reinforces or balances cannot be counted. Mark every link in it with + or - to decide it.",
                    UndecidableLoopRuleId,
                    line));
                continue;
            }

            // The claim and the arithmetic, side by side. Neither is corrected: the label may be
            // the intent and an arrow the typo, and this module cannot tell which.
            if (loop.ClaimsReinforcing is { } claimed && claimed != (computed == LoopPolarityResult.Reinforcing))
            {
                var negatives = LoopPolarity.NegativeCount(model, loop.Variables) ?? 0;
                problems.Add(new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"Loop {loop.Identifier} is labelled {(claimed ? "reinforcing" : "balancing")} but its links make it {(computed == LoopPolarityResult.Reinforcing ? "reinforcing" : "balancing")}: "
                        + $"it runs through {negatives} negative {(negatives == 1 ? "link" : "links")}, and a loop reinforces on an even count and balances on an odd one. "
                        + "Either the label or one of the arrows is wrong, and this reading does not assume which.",
                    LabelDisagreesRuleId,
                    line));
            }
        }
    }

    /// <summary>The cycles the arrows form that no loop statement claims.</summary>
    private static void AddUnlabelledCycleFindings(
        CausalLoopModel model, CycleFinderResult found, List<DiagramProblem> problems)
    {
        var claimed = model.Loops
            .Select(loop => Signature(loop.Variables))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var cycle in found.Cycles.Where(cycle => !claimed.Contains(Signature(cycle))))
        {
            var computed = LoopPolarity.Of(model, cycle);
            var reads = computed switch
            {
                LoopPolarityResult.Reinforcing => "reinforcing",
                LoopPolarityResult.Balancing => "balancing",
                _ => "of a polarity that cannot be counted, because a link in it states none",
            };

            problems.Add(new DiagramProblem(
                DiagramProblemSeverity.Info,
                $"The links form a feedback loop through {string.Join(" → ", cycle)} that no loop statement names. It is {reads}.",
                UnlabelledLoopRuleId));
        }
    }

    /// <summary>Whether the arrows actually close the path this loop statement names.</summary>
    private static bool IsACycle(CycleFinderResult found, IReadOnlyList<string> variables) =>
        variables.Count > 0 && found.Cycles.Any(cycle => Signature(cycle) == Signature(variables));

    /// <summary>
    /// A cycle's identity independent of where it was entered: the same loop written starting
    /// from a different member is the same loop, and must not be reported as unlabelled because
    /// the author began it elsewhere.
    /// </summary>
    private static string Signature(IReadOnlyList<string> cycle) => CycleFinder.CanonicalSignature(cycle);
}
