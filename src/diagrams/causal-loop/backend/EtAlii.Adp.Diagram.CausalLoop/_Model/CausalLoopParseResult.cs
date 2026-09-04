namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>What a parse produced: the model the document states, and the lines it could not read.</summary>
/// <param name="Model">Everything the document does state.</param>
/// <param name="Problems">The lines the grammar did not recognise, in document order.</param>
public sealed record CausalLoopParseResult(
    CausalLoopModel Model,
    IReadOnlyList<CausalLoopParseProblem> Problems);
