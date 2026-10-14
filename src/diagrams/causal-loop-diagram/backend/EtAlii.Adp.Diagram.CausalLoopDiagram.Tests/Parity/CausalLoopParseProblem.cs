using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests.Parity;

/// <summary>A line the parser could not read, and why.</summary>
/// <remarks>
/// <para>The parity oracle (causal-loop-disl-fbl task 7): the module's hand-written reading and writing, kept in the test project to hold the definition and binding to what the tool did, and used by no product code.</para>
/// <para>
/// Collected rather than thrown: one unreadable line should cost the reader that line, not the
/// whole diagram. What the document does state still draws, and the problems panel names what
/// did not - which is more use than an empty canvas and an exception.
/// </para>
/// </remarks>
/// <param name="Lines">Where the unreadable statement sits.</param>
/// <param name="Message">What the reading could not make of it.</param>
internal sealed record CausalLoopParseProblem(LineRange Lines, string Message);
