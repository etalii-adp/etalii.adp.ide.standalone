namespace EtAlii.Adp.Specification.Fbl.Registration;

/// <summary>
/// An entry of a legacy identities file (FBL 0.12 §8.7): an element's id, the <c>sidecar.kind</c> of its rule and the key
/// section 8.6 makes for it, its ordinal included; <paramref name="Kind"/> is null for a rule that declares none.
/// </summary>
public sealed record LegacyIdentity(string Id, string? Kind, string Key);
