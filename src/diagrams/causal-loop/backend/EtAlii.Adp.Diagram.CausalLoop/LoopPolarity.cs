namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>
/// Whether a cycle is reinforcing or balancing, derived from its links rather than from what the
/// document calls it.
/// </summary>
/// <remarks>
/// <para>
/// The rule the notation has always had, and the reason this module is more than a drawing tool:
/// </para>
/// <para>
/// <b>A loop is reinforcing when it contains an even number of negative links, and balancing when
/// it contains an odd number.</b>
/// </para>
/// <para>
/// <b>Zero is even</b>, so a loop of nothing but positive links is reinforcing. That is the case
/// readers most often get wrong, and it is the case a new document lands in, so it has a test of
/// its own.
/// </para>
/// <para>
/// The arithmetic is a parity count and nothing more, which is what makes it worth doing: a
/// reader can check a small loop by eye and will not check a large one, and the tool never tires.
/// Where any link around the cycle has no stated polarity the answer is
/// <see cref="LoopPolarityResult.Undecidable"/> rather than a guess - "unknown" is not "none"
/// (Requirement 3.6).
/// </para>
/// </remarks>
public static class LoopPolarity
{
    /// <summary>Derives the polarity of the cycle running through <paramref name="cycle"/>, in order.</summary>
    public static LoopPolarityResult Of(CausalLoopModel model, IReadOnlyList<string> cycle)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(cycle);

        if (cycle.Count == 0)
        {
            return LoopPolarityResult.Undecidable;
        }

        var negatives = 0;

        for (var index = 0; index < cycle.Count; index++)
        {
            // The cycle closes: the last variable links back to the first.
            var from = cycle[index];
            var to = cycle[(index + 1) % cycle.Count];

            var link = model.Links.FirstOrDefault(candidate =>
                string.Equals(candidate.From, from, StringComparison.Ordinal)
                && string.Equals(candidate.To, to, StringComparison.Ordinal));

            switch (link?.Polarity)
            {
                case CausalLoopPolarity.Negative:
                    negatives++;
                    break;

                case CausalLoopPolarity.Positive:
                    break;

                // An unstated polarity, or no link at all where the cycle says there is one:
                // either way the parity cannot be counted, and a count that cannot be taken is
                // not a count of zero.
                default:
                    return LoopPolarityResult.Undecidable;
            }
        }

        return negatives % 2 == 0 ? LoopPolarityResult.Reinforcing : LoopPolarityResult.Balancing;
    }

    /// <summary>How many negative links the cycle runs through, or null where any is unstated.</summary>
    /// <remarks>Exposed so a finding can say <i>why</i> rather than only <i>what</i>.</remarks>
    public static int? NegativeCount(CausalLoopModel model, IReadOnlyList<string> cycle)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(cycle);

        var negatives = 0;

        for (var index = 0; index < cycle.Count; index++)
        {
            var from = cycle[index];
            var to = cycle[(index + 1) % cycle.Count];
            var link = model.Links.FirstOrDefault(candidate =>
                string.Equals(candidate.From, from, StringComparison.Ordinal)
                && string.Equals(candidate.To, to, StringComparison.Ordinal));

            switch (link?.Polarity)
            {
                case CausalLoopPolarity.Negative:
                    negatives++;
                    break;

                case CausalLoopPolarity.Positive:
                    break;

                default:
                    return null;
            }
        }

        return negatives;
    }
}
