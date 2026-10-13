using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>The refusals the graph's edits share, each in one wording.</summary>
internal static class GhgRefusals
{
    /// <summary>The refusal for a trend or influence that is not there, in one wording.</summary>
    public static EditOutcome Gone => EditOutcome.Refused("That is no longer in this graph.");

    /// <summary>The refusal for a trend without a readable span, which nothing can move or resize.</summary>
    public static EditOutcome NoSpan => EditOutcome.Refused("This trend's dates cannot be read, so it cannot be changed until they are fixed in the file.");

    /// <summary>
    /// The refusal for a span too short for the phases it shows: every phase is at least a month
    /// (Requirement 4.6), so N phases need N months. Null when the span is long enough.
    /// </summary>
    public static EditOutcome? TooShort(int months, int phases) =>
        months < Math.Max(1, phases)
            ? EditOutcome.Refused(phases <= 1
                ? "A trend must be at least one month long."
                : $"A trend showing {phases} phases must be at least {phases} months long, one per phase.")
            : null;
}
