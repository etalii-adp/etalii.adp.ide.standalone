namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests.Parity;

/// <summary>What a parse produced: the model the document states, and the lines it could not read.</summary>
/// <remarks>The parity oracle (causal-loop-disl-fbl task 7): the module's hand-written reading and writing, kept in the test project to hold the definition and binding to what the tool did, and used by no product code.</remarks>
/// <param name="Model">Everything the document does state.</param>
/// <param name="Problems">The lines the grammar did not recognise, in document order.</param>
internal sealed record CausalLoopParseResult(
    CausalLoopModel Model,
    IReadOnlyList<CausalLoopParseProblem> Problems);
