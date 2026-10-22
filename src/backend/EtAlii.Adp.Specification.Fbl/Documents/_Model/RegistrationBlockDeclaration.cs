namespace EtAlii.Adp.Specification.Fbl.Documents;

/// <summary>
/// A block of the registration that a binding declares (FBL 0.7 §8.1, <c>registration.blocks</c>): its name, whether its
/// entries stand where the edit put them (<c>kept</c>) or in the ordinal order of their keys, whether its keys are element
/// ids, which are pruned when stale (FBL §8.5), and whether its writes are steps of the history (FBL §7.1).
/// </summary>
internal sealed record RegistrationBlockDeclaration(string Name, bool Kept, bool KeyedByIds, bool InHistory);
