using EtAlii.Adp.Common;
namespace EtAlii.Adp.Documents;

/// <summary>
/// A run of lines captured before a removal, with the index it sat at - what a byte-exact undo
/// puts back.
/// </summary>
/// <remarks>
/// Moved here from the timeline and dependency-graph modules, which held it as
/// <c>TimelineLineSegment</c> and <c>DependencyGraphLineSegment</c> - identical apart from the
/// type name. It is the counterpart to <see cref="LineDocument.Remove"/>: what that takes out,
/// this is what puts it back.
/// </remarks>
/// <param name="Start">The line index the run began at, in the document as it was.</param>
/// <param name="Lines">The line texts, without their terminators.</param>
public sealed record LineSegment(int Start, IReadOnlyList<string> Lines);
