namespace EtAlii.Adp.Specification.Fbl.Planning;

/// <summary>One part of an emit template: a placeholder or a literal, and the optional segment it is in (-1 for none).</summary>
internal sealed record EmitPart(string? Placeholder, string? Literal, int Segment);
