namespace EtAlii.Adp.Backend.Hierarchy;

public sealed record EntryNode(ShortGuid Id, ShortGuid? ParentId, string Name, bool IsFolder, bool Available);
