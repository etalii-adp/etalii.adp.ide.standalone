namespace EtAlii.Adp.Specification.Fbl.Grammar;

/// <summary>
/// What a <c>{node}</c> slot with <c>holds</c> reads (FBL 0.18 §4.9), for writing: the nodes its selector reaches that hold
/// its text, in document order, and the last node it reaches whatever that holds, after which a new one is written.
/// </summary>
internal sealed record GrammarFlag(IReadOnlyList<GrammarNode> Holding, GrammarNode? Last);
