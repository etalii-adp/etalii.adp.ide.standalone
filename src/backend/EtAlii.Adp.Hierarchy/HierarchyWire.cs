using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy.Wire;

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// Maps the hierarchy's entries and changes to the contract's proto - the one place that mapping
/// lives, shared by <see cref="HierarchyService.ListEntries"/> and <see cref="HierarchyWatch"/>.
/// </summary>
internal static class HierarchyWire
{
    public static Entry ToProto(EntryNode node)
    {
        var entry = new Entry
        {
            Id = node.Id,
            Name = node.Name,
            Kind = node.IsFolder ? EntryKind.Folder : EntryKind.File,
            Available = node.Available,
            HasChildren = node.HasChildren,
            DiagramState = (Wire.EntryDiagramState)node.DiagramState,
        };
        if (node.ParentId is { } parentId)
        {
            entry.ParentId = parentId;
        }

        return entry;
    }

    public static HierarchyChange ToProto(HierarchyEntryChange change) => change switch
    {
        HierarchyEntryCreated c => new HierarchyChange { Created = new EntryCreated { Entry = ToProto(c.Entry) } },
        HierarchyEntryRemoved r => new HierarchyChange { Removed = new EntryRemoved { EntryId = r.EntryId } },
        HierarchyEntryRenamed rn => new HierarchyChange { Renamed = new EntryRenamed { EntryId = rn.EntryId, NewName = rn.NewName } },
        // A re-parent sets parent_id; an empty id is "now at the root" (unset would read as
        // "parent unchanged", which a root re-parent is not).
        HierarchyEntryUpdated up => new HierarchyChange
        {
            Updated = up.ParentChanged
                ? new EntryUpdated { EntryId = up.EntryId, HasChildren = up.HasChildren, ParentId = up.ParentId ?? default, DiagramState = (Wire.EntryDiagramState)up.DiagramState }
                : new EntryUpdated { EntryId = up.EntryId, HasChildren = up.HasChildren, DiagramState = (Wire.EntryDiagramState)up.DiagramState },
        },
        HierarchyRootUnavailable u => new HierarchyChange { RootUnavailable = new RootUnavailable { Message = u.Message } },
        _ => throw new ArgumentOutOfRangeException(nameof(change)),
    };
}
