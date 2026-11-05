using EtAlii.Adp.Specification.Fbl.Rules;

namespace EtAlii.Adp.Specification.Fbl.Grammar;

/// <summary>One structured value of a slot with <c>items</c> (FBL 0.12 §5.2): the node that holds it, whose span is the item's, and what each of its item attributes reads there.</summary>
internal sealed record GrammarRecord(GrammarNode Node, IReadOnlyDictionary<string, SlotRead> Values);

/// <summary>What a slot with <c>items</c> reads (FBL 0.12 §5.2), for writing: one structured value for each node its selector reaches, in document order.</summary>
internal sealed record GrammarRecords(IReadOnlyList<GrammarRecord> Records);
