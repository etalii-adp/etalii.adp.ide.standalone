using EtAlii.Adp.Designer.TableModel;
using EtAlii.Adp.Specification.Fbl.Planning;
using static EtAlii.Adp.Designer.Knowledge.KnowledgeEdits;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// The gestures that change a property, the column of every view: whether it is there and what it
/// is called, its type, its relation (<c>KnowledgeColumnEdits.Relations.cs</c>) and its options
/// (<c>KnowledgeColumnEdits.Options.cs</c>). How a view shows a column is a view's edit.
/// </summary>
internal static partial class KnowledgeColumnEdits
{
    public const string NameNeeded = "A property needs a name.";
    public const string RelationNeedsTarget = "A relation is added by choosing the table it relates to.";
    public const string TitleIsText = "The title property is always text.";
    public const string OtherSideChoice = "This relation has another side: choose whether that is deleted too or kept as a one-way relation.";

    internal static KnowledgeEdit AddColumn(KnowledgeTable table, KnowledgeView? view, TableGesture gesture, Func<string> newId)
    {
        var type = gesture.Settings.GetValueOrDefault("type") ?? KnowledgeValueTypes.Text.Name;
        var valueType = KnowledgeValueTypes.Of(type);
        if (valueType is KnowledgeRelationType)
        {
            return Refused(RelationNeedsTarget);
        }

        if (!valueType.IsKnown || KnowledgeDefinition.ValueTypeLabel(type) is not { } label)
        {
            return Refused($"'{type}' is not a type a property can have.");
        }

        // A name given with the property is its name, refused like a rename's when another property has it; without one it takes its type's.
        var given = (gesture.Values.FirstOrDefault() ?? "").Trim();
        if (given.Length > 0 && table.Properties.Any(other => string.Equals(other.Name, given, StringComparison.OrdinalIgnoreCase)))
        {
            return Refused($"Another property is already called '{given}'.");
        }

        var id = newId();
        var name = given.Length > 0 ? given : UniqueName(table.Properties.Select(property => property.Name), label);
        return new KnowledgeEdit(Place(table, view, id, Attributes(("name", name), ("valueType", type)), gesture.TargetId, gesture.Settings.GetValueOrDefault("side") == "left"));
    }

    /// <summary>
    /// A new property and where it stands: at the end, or beside another - in the file, so every
    /// view that has no order of its own shows it there, and in this view's own order where the
    /// view has one that reaches that far.
    /// </summary>
    private static List<ModelChange> Place(KnowledgeTable table, KnowledgeView? view, string id, Dictionary<string, object?> attributes, string besideId, bool left)
    {
        var beside = IndexOf(table.Properties, property => property.Id == besideId);
        if (beside < 0)
        {
            return [new ModelChange.Add("Property", id, attributes)];
        }

        List<ModelChange> changes = [.. AddAt("Property", id, attributes, null, left ? beside : beside + 1, table.Properties.Count, id)];
        if (view is not null && IndexOf(view.Columns, column => column.PropertyId == besideId) is >= 0 and var listed)
        {
            changes.AddRange(AddAt("Column", null, Attributes(("property", id)), view.Id, left ? listed : listed + 1, view.Columns.Count, $"{view.Id}/columns/{id}"));
        }

        return changes;
    }

    internal static KnowledgeEdit RenameColumn(KnowledgeTable table, TableGesture gesture)
    {
        if (table.Properties.FirstOrDefault(candidate => candidate.Id == gesture.ColumnId) is not { } property)
        {
            return Refused(PropertyGone);
        }

        var name = (gesture.Values.FirstOrDefault() ?? "").Trim();
        if (name.Length == 0)
        {
            return Refused(NameNeeded);
        }

        if (name == property.Name)
        {
            return KnowledgeEdit.Nothing;
        }

        return table.Properties.Any(other => other.Id != property.Id && string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase))
            ? Refused($"Another property is already called '{name}'.")
            : KnowledgeEdit.Of(Set(property.Id, ("name", name)));
    }

    internal static KnowledgeEdit DuplicateColumn(KnowledgeTable table, KnowledgeView? view, TableGesture gesture, Func<string> newId)
    {
        if (table.Properties.FirstOrDefault(candidate => candidate.Id == gesture.ColumnId) is not { } property)
        {
            return Refused(PropertyGone);
        }

        // The copy is a property like the first: its type and its options, each with an id of its
        // own. It is never the title, and never the other side of the first one's relation.
        var id = newId();
        var attributes = Attributes(("name", UniqueName(table.Properties.Select(other => other.Name), property.Name)), ("valueType", property.ValueType));
        if (property is { Type: KnowledgeRelationType, IsComputed: false })
        {
            attributes["targetFile"] = property.TargetFile;
            attributes["limit"] = property.Limit is "one" ? "one" : null;
        }

        var changes = Place(table, view, id, attributes, property.Id, left: false);
        changes.AddRange(property.Options.Select(option => (ModelChange)new ModelChange.Add(
            "Option",
            newId(),
            Attributes(("name", option.Name), ("colour", option.Colour is "" or "default" ? null : option.Colour)),
            id)));
        return new KnowledgeEdit(changes);
    }

    internal static KnowledgeEdit DeleteColumn(KnowledgeTable table, TableGesture gesture, KnowledgeSurroundings? files)
    {
        if (table.Properties.FirstOrDefault(candidate => candidate.Id == gesture.ColumnId) is not { } property)
        {
            return Refused(PropertyGone);
        }

        if (property.IsTitle)
        {
            return Refused(TitleStays);
        }

        // A view grouped by the property is no longer grouped: the grouping, and what it remembered
        // of its groups, goes in the same step. Its cells, columns, sorts and conditions go with the
        // property itself, as the bindings cascade.
        List<ModelChange> changes = [];
        foreach (var view in table.Views.Where(view => view.GroupBy == property.Id))
        {
            changes.AddRange(KnowledgeViewEdits.UngroupSettings(view));
            changes.Add(Set(view.Id, ("groupBy", null)));
        }

        // A view frozen up to the property is no longer frozen: it has no column to be frozen up to.
        changes.AddRange(table.Views.Where(view => view.Freeze == property.Id).Select(view => (ModelChange)Set(view.Id, ("freeze", null))));

        changes.Add(new ModelChange.Remove(property.Id));

        // One side of a two-way relation: what becomes of the other side is its author's choice, never a guess.
        var self = property.TargetFile is KnowledgeRelations.Self or "";
        var target = self ? null : files?.TargetOf(property);
        var related = self ? table : target?.Table;
        if (property is not { Type: KnowledgeRelationType, Counterpart.Length: > 0 } || related?.Properties.FirstOrDefault(candidate => candidate.Id == property.Counterpart) is not { } other)
        {
            return new KnowledgeEdit(changes);
        }

        if (OtherSideOfDeleted(table, property, related, other, gesture.Settings.GetValueOrDefault("otherSide")) is not { } theirs)
        {
            return Refused(OtherSideChoice);
        }

        return self ? new KnowledgeEdit([.. theirs, .. changes]) : new KnowledgeEdit(changes, Others: [new KnowledgeOtherFile(target!.Path, theirs)]);
    }

    /// <summary>What becomes of the other side of a relation one side of which is deleted, as its author chose; null when no choice was made.</summary>
    private static List<ModelChange>? OtherSideOfDeleted(KnowledgeTable table, KnowledgeProperty property, KnowledgeTable related, KnowledgeProperty other, string? choice)
    {
        List<ModelChange> theirs = [];
        switch (choice)
        {
            case "delete":
                theirs.Add(new ModelChange.Remove(other.Id));
                break;
            case "keep" when !other.IsComputed:
                // It holds the values already: it only stops naming the side that goes.
                theirs.Add(Set(other.Id, ("counterpart", null)));
                break;
            case "keep":
                // It showed the values of the side that goes: they become its own, row by row.
                theirs.Add(Set(other.Id, ("counterpart", null), ("computed", null)));
                foreach (var row in related.Rows)
                {
                    var naming = table.Rows
                        .Where(candidate => candidate.Cells.Any(cell => cell.PropertyId == property.Id && cell.Key == "rows" && cell.Values.Contains(row.Id)))
                        .Select(candidate => candidate.Id)
                        .ToList();
                    if (naming.Count > 0)
                    {
                        theirs.Add(new ModelChange.Add("Cell", null, Attributes(("property", other.Id)), row.Id));
                        theirs.AddRange(naming.Select(value => (ModelChange)new ModelChange.Add("CellItem", null, Attributes(("row", value)), CellId(row.Id, other.Id))));
                    }
                }

                break;
            default:
                return null;
        }

        return theirs;
    }

    /// <summary>
    /// A change of type, with every value that has a meaning in the new type converted in the same
    /// step, as the conversion table of the designer's specification says. A value that has none
    /// is not touched: it stays under the key it has, to be reported on its cell and to be there
    /// again when the type is changed back. Nothing is discarded.
    /// </summary>
    internal static KnowledgeEdit ChangeType(KnowledgeTable table, TableGesture gesture, Func<string> newId)
    {
        if (table.Properties.FirstOrDefault(candidate => candidate.Id == gesture.ColumnId) is not { } property)
        {
            return Refused(PropertyGone);
        }

        var to = gesture.Settings.GetValueOrDefault("type") ?? "";
        if (to == property.ValueType)
        {
            return KnowledgeEdit.Nothing;
        }

        if (property.IsTitle)
        {
            return Refused(TitleIsText);
        }

        var target = KnowledgeValueTypes.Of(to);
        if (target is KnowledgeRelationType)
        {
            return Refused(RelationNeedsTarget);
        }

        return target.IsKnown ? Converted(table, property, to, target, newId) : Refused($"'{to}' is not a type a property can have.");
    }

    /// <summary>The property given its new type, and each value written as the type it had converted to the new one.</summary>
    private static KnowledgeEdit Converted(KnowledgeTable table, KnowledgeProperty property, string to, KnowledgeValueType target, Func<string> newId)
    {
        List<ModelChange> changes = [Set(property.Id, ("valueType", to))];
        var from = property.Type;
        var held = from.CellKey;
        var names = property.Options.ToDictionary(option => option.Id, option => option.Name, StringComparer.Ordinal);
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in property.Options)
        {
            ids.TryAdd(option.Name, option.Id);
        }

        foreach (var row in table.Rows)
        {
            // Only a value written as the type the property had is converted. One kept from an
            // earlier change of type stays as it is, under the key it has.
            if (row.Cells.FirstOrDefault(candidate => candidate.PropertyId == property.Id) is not { Values.Count: > 0 } cell || cell.Key != held)
            {
                continue;
            }

            from.ConvertTo(target, new KnowledgeConversion(CellId(row.Id, property.Id), cell, held, names, Option, changes));
        }

        return new KnowledgeEdit(changes);

        // The option of a name, made in this step when the property has none of it.
        string Option(string name)
        {
            if (!ids.TryGetValue(name, out var id))
            {
                id = newId();
                ids[name] = id;
                changes.Add(new ModelChange.Add("Option", id, Attributes(("name", name)), property.Id));
            }

            return id;
        }
    }
}
