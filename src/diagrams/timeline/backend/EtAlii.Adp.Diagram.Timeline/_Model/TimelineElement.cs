using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Common;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// One thing on the timeline: a period when it has an end, a moment when it does not.
/// </summary>
/// <param name="Id">The stable identifier, from the document. Identity lives in the file because the schema is ADP's own.</param>
/// <param name="Label">What it is called.</param>
/// <param name="Begin">When it starts. Required.</param>
/// <param name="End">When it finishes, or null for a moment (Requirement 3.3).</param>
/// <param name="Row">Which row it sits on - a placement grid, never a lane (Requirement 3.6).</param>
/// <param name="Range">The lines that declare it, for the writer and for diagnostics.</param>
public sealed record TimelineElement(
    string Id,
    string Label,
    TimelineInstant Begin,
    TimelineInstant? End,
    int Row,
    LineRange Range)
{
    /// <summary>Whether this element occupies a period rather than a single moment.</summary>
    public bool IsPeriod => End is not null;
}
