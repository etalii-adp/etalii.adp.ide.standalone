namespace EtAlii.Adp.Specification.Fbl.Bullets;

/// <summary>A heading of a bullets body (FBL 0.7 §4.8): its 0-based line, its level and its text.</summary>
internal sealed record Heading(int Line, int Level, string Text);
