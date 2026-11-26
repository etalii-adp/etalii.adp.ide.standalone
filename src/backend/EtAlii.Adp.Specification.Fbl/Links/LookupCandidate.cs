namespace EtAlii.Adp.Specification.Fbl.Links;

/// <summary>
/// An element the lookup offers (DISL 0.12 §9.4): the document it is in, by path, by folder relative to the workspace's root and
/// by name, its id and name, and the link that names it, with that name cached, as the element created for it holds.
/// </summary>
public sealed record LookupCandidate(string Path, string Folder, string DocumentName, string ElementId, string Name, string Link);
