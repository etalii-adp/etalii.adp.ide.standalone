using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests.Parity;

/// <summary>
/// The findings as <c>CausalLoopValidator</c> computed them by hand before they were derived from the DISL
/// definition (causal-loop-disl-fbl task 6), kept verbatim as the oracle the derived ones are compared
/// with: the parser's unreadable lines, the dangling ends, the cycle bound, each stated loop against the
/// arrows, and every cycle no loop claims, from the module's <see cref="CycleFinder"/>.
/// </summary>
/// <remarks>It goes with the parser (task 7).</remarks>
internal static class HandWrittenCausalLoopFindings
{
    /// <summary>The findings of <paramref name="text"/>, in the order the validator gave them.</summary>
    public static IReadOnlyList<DiagramProblem> Of(string text)
    {
        var parsed = CausalLoopParser.Parse(CausalLoopDocument.Parse(text));
        var model = parsed.Model;
        var problems = new List<DiagramProblem>();

        foreach (var problem in parsed.Problems)
        {
            problems.Add(new DiagramProblem(
                DiagramProblemSeverity.Warning,
                problem.Message,
                CausalLoopValidator.UnreadableRuleId,
                new DiagramProblemLineLocation((uint)problem.Lines.Start + 1)));
        }

        foreach (var link in model.Links)
        {
            foreach (var end in new[] { link.From, link.To }.Where(end => !model.Declares(end)))
            {
                problems.Add(new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"The link from '{link.From}' to '{link.To}' names '{end}', which this document does not declare as a variable, so the link is not drawn.",
                    CausalLoopValidator.DanglingLinkRuleId,
                    new DiagramProblemLineLocation((uint)link.Lines.Start + 1)));
            }
        }

        var found = CycleFinder.Find(model);
        if (found.Truncated)
        {
            problems.Add(new DiagramProblem(
                DiagramProblemSeverity.Info,
                $"This diagram contains more feedback loops than are examined; {found.Examined} were read and the rest were not, so any claim below about unlabelled loops covers only those.",
                "causal-loop.cycle-bound-reached"));
        }

        AddStatedLoopFindings(model, found, problems);
        AddUnlabelledCycleFindings(model, found, problems);

        return problems;
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
                    CausalLoopValidator.LoopIsNotACycleRuleId,
                    line));
                continue;
            }

            var computed = LoopPolarity.Of(model, loop.Variables);
            if (computed == LoopPolarityResult.Undecidable)
            {
                problems.Add(new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"Loop {loop.Identifier} runs through a link with no stated polarity, so whether it reinforces or balances cannot be counted. Mark every link in it with + or - to decide it.",
                    CausalLoopValidator.UndecidableLoopRuleId,
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
                    CausalLoopValidator.LabelDisagreesRuleId,
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
                CausalLoopValidator.UnlabelledLoopRuleId));
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
