using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// The operations that shape the hierarchy (FR-026): the adds, the moves and the delete of <c>hierarchical-requirement.des</c>,
/// and giving a duplicate a number of its own (DESL 0.2 sections 5.3.4, 5.3.5). What they write is what the conformance
/// fixtures state, byte for byte.
/// </summary>
/// <remarks>
/// <b>An operation is written in two steps of one edit.</b> The numbering every edit writes, each placeholder's number and
/// the marks, the new entry's number included (spec 029 R17), is made first; the structure is planned against the bytes
/// that leaves. Planned together against the bytes as read, the splices of a move or a delete would overlap those of a
/// placeholder inside the entries it carries. The numbering moves no entry, so an entry is found again by its place in
/// reading order, and the undo still takes back both, since the command keeps the bytes from before the first.
/// </remarks>
internal static class StructureOperations
{
    public const string AddTopItem = "addTopItem";
    public const string AddItem = "addItem";
    public const string AddItemBelow = "addItemBelow";
    public const string AddRequirement = "addRequirement";
    public const string AddRequirementBelow = "addRequirementBelow";
    public const string MoveItem = "moveItem";
    public const string MoveRequirement = "moveRequirement";
    public const string DeleteEntry = "deleteEntry";

    /// <summary>Not an operation of the specification but the user's action DESL 0.2 section 5.3.4 asks for: the duplicate it targets is given the next number.</summary>
    public const string RenumberDuplicate = "renumberDuplicate";

    /// <summary>What <c>addRequirementBelow</c> is unavailable with on a requirement at the top level, as the specification says it.</summary>
    public const string NoItem = "This requirement stands in no item. Indent it under one first.";

    private const string ItemType = "Item";
    private const string RequirementType = "Requirement";

    /// <summary>Whether <paramref name="operation"/> is one of these.</summary>
    public static bool Handles(string operation) =>
        operation is AddTopItem or AddItem or AddItemBelow or AddRequirement or AddRequirementBelow or MoveItem or MoveRequirement or DeleteEntry or RenumberDuplicate;

    /// <summary>
    /// The bytes of the body <paramref name="body"/>, named <paramref name="fileName"/>, after <paramref name="operation"/>
    /// on the entry the surface names <paramref name="targetId"/>, with <paramref name="parameters"/> naming entries by the
    /// surface's ids too; or the sentence saying why it cannot be, in which case nothing changed. An entry the file no
    /// longer has is refused as a change made against an older file.
    /// </summary>
    public static (byte[]? Bytes, string Refusal) Apply(byte[] body, string fileName, string operation, string targetId, IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(parameters);
        var read = HierarchicalRequirementBody.Read(body, fileName);
        if (read.Document.ReadOnlyReason.Length > 0) return (null, read.Document.ReadOnlyReason);

        var entries = read.Document.Entries;
        var ids = HierarchicalRequirementSurfaceMapper.NodeIds(entries);
        RequirementEntry? Find(string id) => entries.FirstOrDefault(entry => ids[entry] == id);

        RequirementEntry? target = null;
        if (operation != AddTopItem && (target = Find(targetId)) is null) return (null, HierarchicalRequirementGestures.Drifted);
        RequirementEntry? item = null;
        RequirementEntry? after = null;
        if (parameters.TryGetValue("item", out var itemId) && itemId.Length > 0 && (item = Find(itemId)) is null) return (null, HierarchicalRequirementGestures.Drifted);
        if (parameters.TryGetValue("after", out var afterId) && afterId.Length > 0 && (after = Find(afterId)) is null) return (null, HierarchicalRequirementGestures.Drifted);
        if (Refusal(read, operation, target, item, after) is { Length: > 0 } refusal) return (null, refusal);

        var numbering = new BodyEdit(read);
        string? given = operation switch
        {
            AddTopItem or AddItem or AddItemBelow => BodyEdit.Next(read.Document, item: true),
            AddRequirement or AddRequirementBelow => BodyEdit.Next(read.Document, item: false),
            RenumberDuplicate => BodyEdit.Next(read.Document, item: target is Item),
            _ => null,
        };
        if (given is not null) numbering.Give(given);
        if (operation == RenumberDuplicate)
        {
            numbering.Set(numbering.ElementOf(target!), "id", given);
            return numbering.Write();
        }

        (byte[]? numbered, string unwritten) = numbering.Write();
        if (numbered is null) return (null, unwritten);

        // The structure, against the numbered bytes, with each entry found again at its place in reading order.
        var again = HierarchicalRequirementBody.Read(numbered, fileName);
        RequirementEntry? Same(RequirementEntry? entry) => entry is null ? null : again.Document.Entries[IndexOf(entries, entry)];
        var edit = new BodyEdit(again);
        if (Change(again, edit, operation, given, Same(target), Same(item), Same(after)) is { } change) edit.Change(change);
        return edit.Write();
    }

    /// <summary>Why <paramref name="operation"/> cannot be done with these entries, or empty when it can.</summary>
    private static string Refusal(HierarchicalRequirementBody read, string operation, RequirementEntry? target, RequirementEntry? item, RequirementEntry? after)
    {
        if (operation is AddItem or AddItemBelow or AddRequirement or MoveItem && target is not Item) return $"'{operation}' is for an item.";
        if (operation is AddRequirementBelow or MoveRequirement && target is not Requirement) return $"'{operation}' is for a requirement.";
        if (operation == AddRequirementBelow && target!.ParentId is null) return NoItem;
        if (item is not null && item is not Item) return $"{item.Id} is a requirement, which holds nothing.";
        if (operation == MoveRequirement && item is null) return "A requirement is moved into an item.";
        if (after is not null && (after.GetType() != target?.GetType() || Parent(read, after) != (item is null ? null : Element(read, item)))) return HierarchicalRequirementGestures.Drifted;
        if (operation == MoveItem && item is not null)
        {
            var self = Element(read, target!);
            for (var enclosing = Element(read, item); enclosing is not null; enclosing = Parent(read, enclosing))
            {
                if (enclosing == self) return $"{target!.Id} cannot be moved into itself or into anything it holds.";
            }
        }
        if (operation == RenumberDuplicate)
        {
            var ids = HierarchicalRequirementSurfaceMapper.NodeIds(read.Document.Entries);
            if (target!.Placeholder || ids[target] == target.Id) return $"{target.Id} is the only entry with its number, which it keeps.";
        }
        return "";
    }

    /// <summary>
    /// The structural change <paramref name="operation"/> makes, as the specification's actions say it, against the reading
    /// <paramref name="read"/>; or null when it changes nothing, as a move to where an entry already is.
    /// </summary>
    private static ModelChange? Change(HierarchicalRequirementBody read, BodyEdit edit, string operation, string? given, RequirementEntry? target, RequirementEntry? item, RequirementEntry? after)
    {
        switch (operation)
        {
            case AddTopItem:
                return Add(ItemType, given!, null, null);
            case AddItem:
                return Add(ItemType, given!, edit.ElementOf(target!), null);
            case AddItemBelow:
            case AddRequirementBelow:
            {
                var self = edit.ElementOf(target!);
                var parent = Parent(read, self);
                return Add(target is Item ? ItemType : RequirementType, given!, parent, Siblings(read, parent, target!).IndexOf(self) + 1);
            }
            case AddRequirement:
            {
                var parent = edit.ElementOf(target!);
                var children = Siblings(read, parent, target!);
                var requirements = children.Where(child => TypeOf(read, child) == RequirementType).ToList();
                var items = children.Where(child => TypeOf(read, child) == ItemType).ToList();
                int? index = requirements.Count > 0 ? children.IndexOf(requirements[^1]) + 1 : items.Count > 0 ? children.IndexOf(items[0]) : null;
                return Add(RequirementType, given!, parent, index);
            }
            case MoveItem:
            case MoveRequirement:
                return Move(read, edit, target!, item, after);
            case DeleteEntry:
                return new ModelChange.Remove(edit.ElementOf(target!));
            default:
                return null;
        }
    }

    /// <summary>
    /// A move as <c>moveItem</c> and <c>moveRequirement</c> say it: after <paramref name="after"/> when it is given; else,
    /// within its own parent, to the front of its kind, and into another, where a new entry of its kind would go.
    /// </summary>
    private static ModelChange.Move? Move(HierarchicalRequirementBody read, BodyEdit edit, RequirementEntry target, RequirementEntry? item, RequirementEntry? after)
    {
        var self = edit.ElementOf(target);
        var into = item is null ? null : edit.ElementOf(item);
        var siblings = Siblings(read, into, target);
        if (after is not null) return new ModelChange.Move(self, into, siblings.IndexOf(edit.ElementOf(after)) + 1);

        var type = TypeOf(read, self);
        var ofItsKind = siblings.Where(sibling => sibling != self && TypeOf(read, sibling) == type).ToList();
        if (Parent(read, self) == into) return ofItsKind.Count > 0 ? new ModelChange.Move(self, into, siblings.IndexOf(ofItsKind[0])) : null;
        if (type == ItemType || ofItsKind.Count > 0) return new ModelChange.Move(self, into, ofItsKind.Count > 0 && type == RequirementType ? siblings.IndexOf(ofItsKind[^1]) + 1 : -1);
        var items = siblings.Where(sibling => TypeOf(read, sibling) == ItemType).ToList();
        return new ModelChange.Move(self, into, items.Count > 0 ? siblings.IndexOf(items[0]) : -1);
    }

    private static ModelChange.Add Add(string type, string id, string? parent, int? index) =>
        new(type, id, new Dictionary<string, object?>(StringComparer.Ordinal) { ["title"] = type == ItemType ? "New item" : "New requirement" }, parent, index);

    /// <summary>
    /// The library's elements an entry of <paramref name="entry"/>'s kind is placed among under <paramref name="parent"/>,
    /// in their order: an item's children of either type, and at the top level, where the library orders each type apart,
    /// those of the entry's type.
    /// </summary>
    private static List<string> Siblings(HierarchicalRequirementBody read, string? parent, RequirementEntry entry) =>
        [.. read.Body.Model.Elements
            .Where(element => element.Type is ItemType or RequirementType && element.ParentId == parent && (parent is not null || element.Type == (entry is Item ? ItemType : RequirementType)))
            .Select(element => element.Id)];

    private static string Element(HierarchicalRequirementBody read, RequirementEntry entry) => new BodyEdit(read).ElementOf(entry);

    private static string? Parent(HierarchicalRequirementBody read, RequirementEntry entry) => Parent(read, Element(read, entry));

    private static string? Parent(HierarchicalRequirementBody read, string element) => Of(read, element).ParentId;

    private static string TypeOf(HierarchicalRequirementBody read, string element) => Of(read, element).Type;

    private static FblElement Of(HierarchicalRequirementBody read, string element) => read.Body.Model.Elements.First(candidate => candidate.Id == element);

    private static int IndexOf(IReadOnlyList<RequirementEntry> entries, RequirementEntry entry)
    {
        for (var at = 0; at < entries.Count; at++)
        {
            if (ReferenceEquals(entries[at], entry)) return at;
        }

        throw new ArgumentException("The entry is not one of these.", nameof(entry));
    }
}
