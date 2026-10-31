using EtAlii.Adp.Specification.Fbl.Text;

namespace EtAlii.Adp.Specification.Fbl.Grammar;

/// <summary>One item of a <c>{node}</c> slot of a <c>many</c> attribute (FBL 0.13 §4.9): its value, escapes resolved, the span it is written in, and the node that holds it.</summary>
internal sealed record GrammarItem(string Value, Span Span, GrammarNode Node);

/// <summary>
/// What a <c>{node}</c> slot of a <c>many</c> attribute reads (FBL 0.11 §5.2, FBL 0.13 §4.9), for writing: every node its
/// selector reaches with the span of its value, and every item they hold, in document order.
/// </summary>
internal sealed record GrammarItems(IReadOnlyList<(GrammarNode Node, Span Span)> Values, IReadOnlyList<GrammarItem> Items);
