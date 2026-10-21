using EtAlii.Adp.Designer.TableModel;
using EtAlii.Adp.Specification.Fbl.Planning;
using static EtAlii.Adp.Designer.Knowledge.KnowledgeEdits;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>The gestures that change a row's cells, and the rows themselves: their values, which rows there are, and their order.</summary>
internal static class KnowledgeCellEdits
{
    private const string RowGone = "That row is no longer in this table.";
    private const string Computed = "This side of the relation is filled in from the other table.";
    public const string OptionFirst = "Make the option in one row first; it can then be given to several.";
    public const string ParentCycle = "A row cannot be its own ancestor.";
    public const string SortedRowsStay = "Rows are in the order of this view's sort, so they cannot be moved here.";

    /// <summary>What an answer starts with when it is a question to confirm and not a refusal: the gesture is made again, confirmed.</summary>
    private const string ConfirmPrefix = "confirm:";

    /// <summary>The question put before rows are deleted that rows of other tables relate to.</summary>
    public static string AskFirst(int relating, int deleted) =>
        $"{ConfirmPrefix}{(relating == 1 ? "1 row in another table relates" : $"{relating} rows in other tables relate")} to {(deleted == 1 ? "this row" : "these rows")}. "
        + $"Deleting {(deleted == 1 ? "it" : "them")} leaves {(relating == 1 ? "that relation" : "those relations")} pointing at nothing.";

    /// <summary>
    /// One property set to the same value in several rows, as one step: what Notion does when the
    /// property of several selected rows is edited. Any row that refuses the value refuses it for all.
    /// </summary>
    internal static KnowledgeEdit SetCells(KnowledgeTable table, TableGesture gesture, Func<string> newId, KnowledgeSurroundings? files)
    {
        if (gesture.Settings.ContainsKey("newOption"))
        {
            return Refused(OptionFirst);
        }

        List<string> rows = [.. (gesture.Settings.GetValueOrDefault("rows") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct()];
        if (rows.Count == 0)
        {
            return Refused(RowGone);
        }

        List<ModelChange> changes = [];
        foreach (var rowId in rows)
        {
            var one = SetCell(table, new TableGesture("setCell", RowId: rowId, ColumnId: gesture.ColumnId, Values: gesture.Values), newId, files);
            if (one.IsRefused)
            {
                return one;
            }

            changes.AddRange(one.Changes);
        }

        return new KnowledgeEdit(changes);
    }

    internal static KnowledgeEdit SetCell(KnowledgeTable table, TableGesture gesture, Func<string> newId, KnowledgeSurroundings? files)
    {
        var row = table.Rows.FirstOrDefault(candidate => candidate.Id == gesture.RowId);
        if (row is null)
        {
            return Refused(RowGone);
        }

        var property = table.Properties.FirstOrDefault(candidate => candidate.Id == gesture.ColumnId);
        if (property is null)
        {
            return Refused(PropertyGone);
        }

        if (property.IsComputed)
        {
            return Refused(Computed);
        }

        List<ModelChange> changes = [];
        var values = gesture.Values.Where(value => value.Length > 0).ToList();
        if (ChooseOptions(property, gesture, newId, values, changes) is { Length: > 0 } refusal)
        {
            return Refused(refusal);
        }

        var cell = row.Cells.FirstOrDefault(candidate => candidate.PropertyId == property.Id);
        return property.Type.HoldsSeveral
            ? SetSeveral(table, row, property, cell, values, changes, files)
            : SetOne(row, property, cell, values, changes);
    }

    /// <summary>
    /// The options a cell's values name: an option made while filling in the cell is made first, and
    /// then chosen like any other. Refused when a value names an option the property does not have.
    /// </summary>
    private static string ChooseOptions(KnowledgeProperty property, TableGesture gesture, Func<string> newId, List<string> values, List<ModelChange> changes)
    {
        var type = property.Type;
        if (!gesture.Settings.TryGetValue("newOption", out var optionName) || optionName.Trim().Length == 0)
        {
            return type.HoldsOptions && values.FirstOrDefault(value => property.Options.All(option => option.Id != value)) is not null
                ? "That option is no longer one of this property's."
                : "";
        }

        if (!type.HoldsOptions)
        {
            return "Only a selection has options.";
        }

        var existing = property.Options.FirstOrDefault(option => string.Equals(option.Name, optionName.Trim(), StringComparison.OrdinalIgnoreCase));
        var optionId = existing?.Id ?? newId();
        if (existing is null)
        {
            changes.Add(new ModelChange.Add("Option", optionId, Attributes(("name", optionName.Trim()), ("colour", ColourFor(property, optionName.Trim()))), property.Id));
        }

        if (!type.HoldsSeveral)
        {
            values.Clear();
            values.Add(optionId);
        }
        else if (!values.Contains(optionId))
        {
            values.Add(optionId);
        }

        return "";
    }

    /// <summary>A cell of a type that holds several values: options or related rows, an item each.</summary>
    private static KnowledgeEdit SetSeveral(KnowledgeTable table, KnowledgeRow row, KnowledgeProperty property, KnowledgeCell? cell, List<string> values, List<ModelChange> changes, KnowledgeSurroundings? files)
    {
        if (RelationRefusal(table, row, property, values, files) is { Length: > 0 } refusal)
        {
            return Refused(refusal);
        }

        if (KnowledgeRelations.IsMutual(property))
        {
            return SetMutual(table, row, property, values, changes);
        }

        var cellId = CellId(row.Id, property.Id);
        var key = property.Type.StoredKey;
        var held = cell?.Values ?? [];
        if (values.Count == 0)
        {
            return cell is null ? KnowledgeEdit.Nothing : new KnowledgeEdit([.. changes, new ModelChange.Remove(cellId)]);
        }

        if (cell is null)
        {
            changes.Add(new ModelChange.Add("Cell", null, Attributes(("property", property.Id)), row.Id));
        }

        changes.AddRange(held.Where(value => !values.Contains(value)).Select(value => (ModelChange)new ModelChange.Remove($"{cellId}/{value}")));
        changes.AddRange(values.Where(value => !held.Contains(value)).Select(value => (ModelChange)new ModelChange.Add("CellItem", null, Attributes((key, value)), cellId)));
        return new KnowledgeEdit(changes);
    }

    /// <summary>Why a relation's cell cannot hold these rows, or empty when it can - and when the property is no relation.</summary>
    private static string RelationRefusal(KnowledgeTable table, KnowledgeRow row, KnowledgeProperty property, List<string> values, KnowledgeSurroundings? files)
    {
        if (property is { Type: KnowledgeRelationType, Limit: "one" } && values.Count > 1)
        {
            return "This relation holds one row.";
        }

        if (property.Type is not KnowledgeRelationType)
        {
            return "";
        }

        // A related row is a row of the target: one that is not there is not related to.
        if (files?.TargetOf(property)?.Table is { } target && values.FirstOrDefault(value => target.Rows.All(candidate => candidate.Id != value)) is not null)
        {
            return $"That row is not in {(target.Name.Length > 0 ? target.Name : "the table this relates to")}.";
        }

        return property.IsParent && values.Count == 1 && KnowledgeRelations.WouldCycle(table, property, row.Id, values[0]) ? ParentCycle : "";
    }

    /// <summary>A cell of a type that holds one value, written as that type writes it.</summary>
    private static KnowledgeEdit SetOne(KnowledgeRow row, KnowledgeProperty property, KnowledgeCell? cell, List<string> values, List<ModelChange> changes)
    {
        var cellId = CellId(row.Id, property.Id);
        if (values.Count == 0)
        {
            return cell is null ? KnowledgeEdit.Nothing : new KnowledgeEdit([.. changes, new ModelChange.Remove(cellId)]);
        }

        var type = property.Type;
        if (type.Write(values[0], out var written) is { Length: > 0 } refusal)
        {
            return Refused(refusal);
        }

        var name = type.StoredKey;

        if (written is null)
        {
            // An unticked checkbox is a cell that is not there.
            return cell is null ? KnowledgeEdit.Nothing : new KnowledgeEdit([.. changes, new ModelChange.Remove(cellId)]);
        }

        // A value kept from a type the property had before is replaced, not joined: a cell holds one value.
        changes.Add(cell is null
            ? new ModelChange.Add("Cell", null, Attributes(("property", property.Id), (name, written)), row.Id)
            : cell.Key.Length > 0 && cell.Key != name && cell.Key is not ("options" or "rows")
                ? Set(cellId, (cell.Key, null), (name, written))
                : Set(cellId, (name, written)));
        return new KnowledgeEdit(changes);
    }

    /// <summary>
    /// Sets what a row is related to in a relation that shows both directions in one property.
    /// A pair of rows is related when either names the other, so relating two rows writes one of
    /// them - the one being edited - and unrelating them takes the pair out of whichever holds it.
    /// </summary>
    private static KnowledgeEdit SetMutual(KnowledgeTable table, KnowledgeRow row, KnowledgeProperty property, List<string> values, List<ModelChange> changes)
    {
        var own = Held(row);
        List<string> stays = [.. own.Where(values.Contains)];
        List<string> added = [];
        foreach (var other in table.Rows.Where(other => other.Id != row.Id))
        {
            var names = Held(other).Contains(row.Id);
            if (values.Contains(other.Id))
            {
                if (!names && !own.Contains(other.Id))
                {
                    added.Add(other.Id);
                }
            }
            else if (names)
            {
                // The other end holds the pair: it goes there, with the cell when it was all the cell held.
                changes.Add(new ModelChange.Remove(Held(other).Count == 1 ? CellId(other.Id, property.Id) : $"{CellId(other.Id, property.Id)}/{row.Id}"));
            }
        }

        var cellId = CellId(row.Id, property.Id);
        if (stays.Count + added.Count == 0)
        {
            if (own.Count > 0)
            {
                changes.Add(new ModelChange.Remove(cellId));
            }

            return new KnowledgeEdit(changes);
        }

        if (own.Count == 0)
        {
            changes.Add(new ModelChange.Add("Cell", null, Attributes(("property", property.Id)), row.Id));
        }

        changes.AddRange(own.Where(value => !values.Contains(value)).Select(value => (ModelChange)new ModelChange.Remove($"{cellId}/{value}")));
        changes.AddRange(added.Select(value => (ModelChange)new ModelChange.Add("CellItem", null, Attributes(("row", value)), cellId)));
        return new KnowledgeEdit(changes);

        IReadOnlyList<string> Held(KnowledgeRow of) => of.Cells.FirstOrDefault(candidate => candidate.PropertyId == property.Id && candidate.Key == KnowledgeValueTypes.Relation.CellKey)?.Values ?? [];
    }

    internal static KnowledgeEdit AddRow(KnowledgeTable table, KnowledgeView? view, TableGesture gesture, Func<string> newId)
    {
        var id = newId();
        List<ModelChange> changes = [];

        // After the row the key was pressed in, where there is one; at the end otherwise.
        var after = gesture.TargetId.Length > 0 ? IndexOf(table.Rows, row => row.Id == gesture.TargetId) : -1;
        changes.AddRange(AddAt("Row", id, Attributes(), null, after >= 0 ? after + 1 : null, table.Rows.Count, id));

        // A row added in a group is given the value that puts it there.
        if (gesture.Settings.TryGetValue("group", out var group) && group.Length > 0
            && view is not null && table.Properties.FirstOrDefault(property => property.Id == view.GroupBy) is { IsComputed: false } grouped
            && grouped.Type.TakesGroup(grouped, group))
        {
            var type = grouped.Type;
            if (type.HoldsSeveral)
            {
                changes.Add(new ModelChange.Add("Cell", null, Attributes(("property", grouped.Id)), id));
                changes.Add(new ModelChange.Add("CellItem", null, Attributes((type.StoredKey, group)), CellId(id, grouped.Id)));
            }
            else if (type.Write(group, out var written).Length == 0)
            {
                changes.Add(new ModelChange.Add("Cell", null, Attributes(("property", grouped.Id), (type.StoredKey, written)), id));
            }
        }

        return new KnowledgeEdit(changes, NewRowId: id);
    }

    internal static KnowledgeEdit DeleteRows(KnowledgeTable table, TableGesture gesture)
    {
        var ids = (gesture.Values.Count > 0 ? gesture.Values : [gesture.RowId]).Where(id => table.Rows.Any(row => row.Id == id)).Distinct().ToList();
        return ids.Count == 0 ? Refused(RowGone) : new KnowledgeEdit([.. ids.Select(id => new ModelChange.Remove(id))]);
    }

    /// <summary>
    /// Moves rows to stand before or after another row, together and in the order they were in.
    /// The order is the file's, which a view without a sort shows; a sorted view has an order of
    /// its own, so nothing is moved from one.
    /// </summary>
    internal static KnowledgeEdit MoveRows(KnowledgeTable table, KnowledgeView view, TableGesture gesture)
    {
        if (view.Sorts.Count > 0)
        {
            return Refused(SortedRowsStay);
        }

        List<string> order = [.. table.Rows.Select(row => row.Id)];
        List<string> moved = [.. order.Where((gesture.Values.Count > 0 ? gesture.Values : [gesture.RowId]).Contains)];
        if (moved.Count == 0 || !order.Contains(gesture.TargetId))
        {
            return Refused(RowGone);
        }

        if (moved.Contains(gesture.TargetId))
        {
            return KnowledgeEdit.Nothing;
        }

        // One row at a time: the first to its place beside the target, each next one right after the one before it.
        List<ModelChange> changes = [];
        var anchor = gesture.TargetId;
        var after = gesture.Settings.GetValueOrDefault("place") == "after";
        foreach (var id in moved)
        {
            List<string> without = [.. order.Where(candidate => candidate != id)];
            var to = without.IndexOf(anchor) + (after ? 1 : 0);
            changes.AddRange(MoveTo(id, null, order, to).Changes);
            without.Insert(to, id);
            order = without;
            (anchor, after) = (id, true);
        }

        return new KnowledgeEdit(changes);
    }
}
