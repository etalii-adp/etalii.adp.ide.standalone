using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// What a knowledge file's model will be once changes that are still on their way to the file
/// have been written: the same changes, made to the elements in memory.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is what lets an edit be shown at once and written behind</b> (knowledge-designer
/// design, <i>An edit is shown at once and written behind</i>). Planning a change through the
/// FBL runtime reads the whole body again, which on a file of ten thousand rows takes longer
/// than an author will wait for a cell to change. Here a change is a few list operations.
/// </para>
/// <para>
/// <b>It is never the truth.</b> The file is; a projection lives only until the write it
/// anticipates has landed, when the session reads the body's own model again. What it has to
/// get right is what the table shows in between - which element exists, under which parent, with
/// which attributes, in which order - and it derives an id exactly as the reading does, so a
/// later change in the queue can name what an earlier one made.
/// </para>
/// </remarks>
internal static class KnowledgeProjection
{
    /// <param name="model">The model to change.</param>
    /// <param name="changes">The changes, in order.</param>
    /// <param name="besideItsRule">Whether the file's format adds an entry after the last one its rule reads (<see cref="KnowledgeDefinition.AddsBesideItsRule"/>).</param>
    public static FblModel Apply(FblModel model, IReadOnlyList<ModelChange> changes, bool besideItsRule = false)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(changes);

        if (changes.Count == 0)
        {
            return model;
        }

        var elements = model.Elements.ToList();
        foreach (var change in changes)
        {
            switch (change)
            {
                case ModelChange.Add add:
                    Add(elements, add, besideItsRule);
                    break;
                case ModelChange.Set set:
                    Set(elements, set);
                    break;
                case ModelChange.Remove remove:
                    Remove(elements, remove.Id);
                    break;
                case ModelChange.Move move:
                    Move(elements, move);
                    break;
            }

            Readdress(elements);
        }

        return model with { Elements = elements };
    }

    /// <summary>The rule an added element is read by and the slot of its parent it is in, as the bindings have them.</summary>
    private static (string Rule, string? Slot) Placement(ModelChange.Add add, FblElement? parent) => add.Type switch
    {
        "Property" => ("property", null),
        "View" => ("view", null),
        "Row" => ("row", null),
        "Option" => ("option", "options"),
        "Column" => ("column", "columns"),
        "Sort" => ("sort", "sorts"),
        "Condition" when parent?.Type == "View" => ("condition1", "filter"),
        "Condition" when parent?.Rule == "filterGroup1" => ("condition2", "conditions"),
        "Condition" => ("condition3", "conditions"),
        "FilterGroup" when parent?.Type == "View" => ("filterGroup1", "filter"),
        "FilterGroup" => ("filterGroup2", "conditions"),
        "GroupSetting" when add.Slot == "hiddenGroups" => ("hiddenGroup", "hiddenGroups"),
        "GroupSetting" when add.Slot == "collapsed" => ("collapsedGroup", "collapsed"),
        "GroupSetting" => ("groupOrder", "groupOrder"),
        "Cell" => ("cell", "cells"),
        "CellItem" when add.Attributes.ContainsKey("option") => ("cellOption", "items"),
        "CellItem" => ("cellRow", "items"),
        _ => (add.Type, add.Slot),
    };

    /// <summary>The elements ordered among each other with one of this type, parent and slot.</summary>
    private static List<FblElement> Siblings(List<FblElement> elements, string type, string? parentId, string? slot) =>
        parentId is null
            ? elements.Where(element => element.ParentId is null && element.Type == type).ToList()
            : elements.Where(element => element.ParentId == parentId && element.ParentSlot == slot).ToList();

    private static void Add(List<FblElement> elements, ModelChange.Add add, bool besideItsRule)
    {
        var parent = add.ParentId is null ? null : elements.FirstOrDefault(element => element.Id == add.ParentId);
        (string rule, string? slot) = Placement(add, parent);
        var siblings = Siblings(elements, add.Type, add.ParentId, slot);
        var attributes = add.Attributes.Where(pair => !IsEmpty(pair.Value)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var id = add.Id
            ?? KnowledgeDefinition.DeriveId(new IdRequest(rule, add.Type, attributes, null, null, 0) { ParentId = add.ParentId, ParentSlot = slot, PositionInSlot = siblings.Count })
            ?? ShortGuid.NewShortGuid().ToString();
        var element = new FblElement(id, add.Id is not null, add.Type, rule, false, attributes, add.ParentId, slot, null, null, default, 0);

        // Last of its siblings - or, in a format that adds beside the entry's own rule, after the last
        // sibling that rule reads: a condition then goes before a group that follows the conditions.
        // Only the order among siblings is read, so last of all the elements is last of them.
        if (besideItsRule && siblings.LastOrDefault(sibling => sibling.Rule == rule) is { } last && !ReferenceEquals(last, siblings[^1]))
        {
            elements.Insert(elements.IndexOf(last) + 1, element);
        }
        else
        {
            elements.Add(element);
        }
    }

    private static void Set(List<FblElement> elements, ModelChange.Set set)
    {
        var index = elements.FindIndex(element => element.Id == set.Id);
        if (index < 0)
        {
            return;
        }

        var attributes = new Dictionary<string, object?>(elements[index].Attributes, StringComparer.Ordinal);
        foreach ((string name, object? value) in set.Attributes)
        {
            if (IsEmpty(value))
            {
                attributes.Remove(name);
            }
            else
            {
                attributes[name] = value;
            }
        }

        elements[index] = elements[index] with { Attributes = attributes };
    }

    /// <summary>
    /// Removes an element with everything it contains, and with what the bindings cascade to: the
    /// cells, columns, sorts and conditions of a removed property, and the cells, cell items and
    /// conditions that name a removed option.
    /// </summary>
    private static void Remove(List<FblElement> elements, string id)
    {
        var removed = elements.FirstOrDefault(element => element.Id == id);
        if (removed is null)
        {
            return;
        }

        var gone = new HashSet<string>(StringComparer.Ordinal) { id };
        var naming = removed.Type switch
        {
            "Property" => "property",
            "Option" => "option",
            _ => null,
        };
        if (naming is not null)
        {
            foreach (var element in elements)
            {
                if (element.Attributes.TryGetValue(naming, out var named) && KnowledgeValues.Text(named) == id)
                {
                    gone.Add(element.Id);
                }
            }
        }

        // Document order puts a parent before what it contains, so one pass reaches every depth.
        foreach (var element in elements)
        {
            if (element.ParentId is not null && gone.Contains(element.ParentId))
            {
                gone.Add(element.Id);
            }
        }

        elements.RemoveAll(element => gone.Contains(element.Id));
    }

    private static void Move(List<FblElement> elements, ModelChange.Move move)
    {
        var element = elements.FirstOrDefault(candidate => candidate.Id == move.Id);
        if (element is null)
        {
            return;
        }

        // The index counts the siblings as they are before the move, the element itself included.
        var siblings = Siblings(elements, element.Type, move.NewParentId, element.ParentSlot);
        var before = move.Index >= 0 && move.Index < siblings.Count ? siblings[move.Index] : null;
        if (ReferenceEquals(before, element))
        {
            return;
        }

        elements.Remove(element);
        var moved = element with { ParentId = move.NewParentId };
        if (before is null)
        {
            elements.Add(moved);
        }
        else
        {
            elements.Insert(elements.IndexOf(before), moved);
        }
    }

    /// <summary>
    /// Gives a filter's items the ids the reading would give them. A condition and a group are
    /// addressed by their place, so adding, moving or removing one changes the address of those
    /// after it - and a later change in the queue names them by the address they will have.
    /// </summary>
    private static void Readdress(List<FblElement> elements)
    {
        var items = elements.Where(element => element.Type is "Condition" or "FilterGroup" && element.ParentId is not null).ToList();
        if (items.Count == 0)
        {
            return;
        }

        var under = items.GroupBy(item => item.ParentId!).ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var addressed = new Dictionary<FblElement, FblElement>(ReferenceEqualityComparer.Instance);
        void Walk(string parentWas, string parentIs, string prefix)
        {
            if (!under.TryGetValue(parentWas, out var children))
            {
                return;
            }

            for (var index = 0; index < children.Count; index++)
            {
                var id = FormattableString.Invariant($"{prefix}/{index}");
                if (children[index].Id != id || children[index].ParentId != parentIs)
                {
                    addressed[children[index]] = children[index] with { Id = id, ParentId = parentIs };
                }

                Walk(children[index].Id, id, id);
            }
        }

        foreach (var view in elements.Where(element => element.Type == "View").ToList())
        {
            Walk(view.Id, view.Id, $"{view.Id}/filter");
        }

        if (addressed.Count == 0)
        {
            return;
        }

        for (var index = 0; index < elements.Count; index++)
        {
            if (addressed.TryGetValue(elements[index], out var readdressed))
            {
                elements[index] = readdressed;
            }
        }
    }

    /// <summary>What empties an attribute, as a set of it does in the body: nothing, an empty text, an empty list.</summary>
    private static bool IsEmpty(object? value) => value switch
    {
        null => true,
        string text => text.Length == 0,
        System.Collections.ICollection list => list.Count == 0,
        _ => false,
    };
}
