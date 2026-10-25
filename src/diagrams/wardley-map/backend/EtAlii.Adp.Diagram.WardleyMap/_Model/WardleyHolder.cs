namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// One element of a map as its identity sees it: its kind, the key of its holder (with the
/// ordinal of a second or later holder of one key) and the line that declares it.
/// </summary>
/// <param name="Kind">One of <see cref="WardleyIdentityKind"/>.</param>
/// <param name="Key">The key of the holder, as <see cref="WardleyIdentityKeys.Numbered"/> builds it.</param>
/// <param name="Line">The 1-based line of the statement that declares the element.</param>
public sealed record WardleyHolder(string Kind, string Key, uint Line);
