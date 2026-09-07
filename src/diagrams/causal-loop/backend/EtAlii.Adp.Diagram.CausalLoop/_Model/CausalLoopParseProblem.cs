using EtAlii.Adp.Common;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>A line the parser could not read, and why.</summary>
/// <remarks>
/// Collected rather than thrown: one unreadable line should cost the reader that line, not the
/// whole diagram. What the document does state still draws, and the problems panel names what
/// did not - which is more use than an empty canvas and an exception.
/// </remarks>
/// <param name="Lines">Where the unreadable statement sits.</param>
/// <param name="Message">What the reading could not make of it.</param>
public sealed record CausalLoopParseProblem(LineRange Lines, string Message);
