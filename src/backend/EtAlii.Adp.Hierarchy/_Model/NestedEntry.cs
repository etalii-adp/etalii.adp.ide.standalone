namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// Where one registration sits in the tree (adp-file-nesting Requirement 3): under
/// <paramref name="SubjectName"/> when its subject is a sibling file, or at the level it sits
/// on disk when it has none - visibly so when the subject is *missing* rather than merely
/// not derivable (<paramref name="IsOrphan"/>, Requirements 8.1 and 8.2).
/// </summary>
/// <param name="Name">The registration's own file name.</param>
/// <param name="SubjectName">The sibling file it nests under, or null to stay where it sits.</param>
/// <param name="IsOrphan">
/// True when a subject was resolved but is not present among the siblings - the broken pair a
/// user can see and fix. False both for a nested registration and for one that never resolves
/// a subject at all (an unknown type keeps its current, un-nested behaviour per Requirement 8.4).
/// </param>
public sealed record NestedEntry(string Name, string? SubjectName, bool IsOrphan);
