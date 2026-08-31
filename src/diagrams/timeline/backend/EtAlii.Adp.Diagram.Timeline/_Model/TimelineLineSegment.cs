namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// A run of lines captured before a removal, with the index it sat at - what a byte-exact undo
/// puts back.
/// </summary>
/// <param name="Start">The line index the run began at, in the document as it was.</param>
/// <param name="Lines">The line texts, without their terminators.</param>
public sealed record TimelineLineSegment(int Start, IReadOnlyList<string> Lines);
