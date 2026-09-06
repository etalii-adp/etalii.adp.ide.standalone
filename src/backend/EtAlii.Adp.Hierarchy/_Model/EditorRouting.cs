namespace EtAlii.Adp.Hierarchy;

/// <summary>What <see cref="EditorResolver"/> decided about a file.</summary>
/// <remarks>
/// A closed set: the two records declared beside this one. There is no "not resolvable" case,
/// because the fallback editor answers when nothing else claims a file - the last resort by
/// construction (modular-text-editors Requirement 3.2).
/// </remarks>
public abstract record EditorRouting;
