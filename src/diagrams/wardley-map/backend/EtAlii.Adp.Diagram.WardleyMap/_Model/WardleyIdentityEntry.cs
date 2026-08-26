namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// One element's identity, and the handle the `.owm` gives for finding it again.
/// </summary>
/// <param name="Id">
/// The ADP-assigned <c>ShortGuid</c>, as its base36 string. The `.owm` format has no identifier
/// of any kind, and name-as-identity would make every rename a delete-plus-add (Requirement 4.1).
/// </param>
/// <param name="Kind">
/// Which sort of thing the key names - <c>component</c>, <c>link</c>, <c>pipeline</c>,
/// <c>note</c>, <c>annotation</c>. Two elements of different kinds may legitimately share a key.
/// </param>
/// <param name="Key">
/// The handle the document offers: a component's name, a link's endpoints and kind, a
/// pipeline's parent name, a note's text, an annotation's number. Rewritten in the same command
/// as the statement it names, so a rename keeps the identity (Requirement 4.4).
/// </param>
public sealed record WardleyIdentityEntry(string Id, string Kind, string Key);
