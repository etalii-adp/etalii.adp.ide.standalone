namespace EtAlii.Adp.Hierarchy;

public sealed record EntryNode(ShortGuid Id, ShortGuid? ParentId, string Name, bool IsFolder, bool Available, bool HasChildren, EntryDiagramState DiagramState = EntryDiagramState.Unspecified);
