namespace EtAlii.Adp.Specification.Fbl.History;

internal sealed record HistoryEntry(SpliceEdit Edit, IReadOnlyList<Splice> Inverse, byte[] BeforeDigest, byte[] AfterDigest, byte[]? Snapshot);
