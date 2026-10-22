namespace EtAlii.Adp.Specification.Fbl.Planning;

/// <summary>
/// Where an entry of a <c>kept</c> block goes (FBL 0.7 §8.4, §15.3): at <paramref name="Index"/> among the block's entries
/// as they are without it, or directly after or before the entry with the key <paramref name="After"/> or <paramref name="Before"/>.
/// </summary>
public sealed record SettingPosition(int? Index = null, string? After = null, string? Before = null);
