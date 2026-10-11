using System.Globalization;
using System.Text.RegularExpressions;
using EtAlii.Adp.Designer.TableModel;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>What a gesture comes to: the changes to make to the file, in order, or why it is refused.</summary>
/// <param name="Changes">The changes, one step together. Empty for a gesture that changes nothing.</param>
/// <param name="Refusal">Why the gesture is refused, as a sentence for the author; empty when it is not.</param>
/// <param name="NewRowId">The id of the row the gesture adds, or empty.</param>
/// <param name="NewViewId">The id of the view the gesture adds, or empty.</param>
/// <param name="Others">What the same step changes in other files: the other side of a two-way relation.</param>
internal sealed record KnowledgeEdit(IReadOnlyList<ModelChange> Changes, string Refusal = "", string NewRowId = "", string NewViewId = "", IReadOnlyList<KnowledgeOtherFile>? Others = null)
{
    public IReadOnlyList<KnowledgeOtherFile> Others { get; } = Others ?? [];

    public bool IsRefused => Refusal.Length > 0;

    public static KnowledgeEdit Refused(string reason) => new([], reason);

    public static KnowledgeEdit Nothing { get; } = new([]);

    public static KnowledgeEdit Of(params ModelChange[] changes) => new(changes);
}

/// <summary>What an edit may know of the files around the one it edits: where that file is, and what its relations point at.</summary>
/// <param name="BodyPath">The file being edited.</param>
/// <param name="TargetOf">What a relation property of the file points at.</param>
/// <param name="TargetAt">What a target named from the file - a relative path, or <c>.</c> for the file itself - is.</param>
internal sealed record KnowledgeSurroundings(string BodyPath, Func<KnowledgeProperty, KnowledgeTarget?> TargetOf, Func<string, KnowledgeTarget> TargetAt);

/// <summary>
/// The table's gestures as changes of the file: one place that knows what each gesture means for
/// a knowledge file, what it refuses and in which words. It reads a table and writes nothing; the
/// changes it gives are made to the file through the FBL runtime and to what the table shows
/// through <see cref="KnowledgeProjection"/>, so the two cannot mean different things.
/// </summary>
internal static partial class KnowledgeEdits
{
    private const string RowGone = "That row is no longer in this table.";
    private const string PropertyGone = "That property is no longer in this table.";
    private const string ViewGone = "That view is no longer in this table.";
    public const string NameNeeded = "A property needs a name.";
    private const string ViewNameNeeded = "A view needs a name.";
    public const string TitleStays = "The title property is always there: it cannot be deleted or hidden.";
    public const string LastView = "A table keeps at least one view.";
    public const string NumberExpected = "A number is expected.";
    public const string DateExpected = "A date is expected, as year-month-day.";
    public const string DateTimeExpected = "A date and a time are expected.";
    public const string TimeExpected = "A time is expected, as hours:minutes.";
    private const string Computed = "This side of the relation is filled in from the other table.";
    public const string RelationNeedsTarget = "A relation is added by choosing the table it relates to.";
    public const string TitleIsText = "The title property is always text.";

    /// <summary>The id the root of a knowledge file has: its rule stores none, and the designer derives this one.</summary>
    internal const string TableId = "table";

    /// <summary>The narrowest a column is, in pixels, as the specification has it.</summary>
    private const int MinimumWidth = 32;

    /// <summary>How deep a filter's groups go: the bindings read a group inside a group and no further.</summary>
    private const int FilterDepth = 2;

    [GeneratedRegex(@"(Z|[+-]\d\d:\d\d)$")]
    private static partial Regex Offset();

    /// <param name="table">The table as it is shown now, edits not yet written included.</param>
    /// <param name="activeViewId">The view the connection shows, for a gesture that names none.</param>
    /// <param name="gesture">What the author did.</param>
    /// <param name="newId">Gives an id that was never used: called once for everything the gesture makes that stores an id.</param>
    /// <param name="besideItsRule">Whether the file's format adds an entry after the last one its rule reads (<see cref="KnowledgeDefinition.AddsBesideItsRule"/>).</param>
    /// <param name="files">The files around this one, for a gesture about a relation; null where there are none to know of.</param>
    public static KnowledgeEdit Plan(KnowledgeTable table, string activeViewId, TableGesture gesture, Func<string> newId, bool besideItsRule = false, KnowledgeSurroundings? files = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(gesture);
        ArgumentNullException.ThrowIfNull(newId);

        var view = table.Views.FirstOrDefault(candidate => candidate.Id == (gesture.ViewId.Length > 0 ? gesture.ViewId : activeViewId)) ?? table.ViewOrDefault(activeViewId);
        return gesture.Kind switch
        {
            "setCell" => SetCell(table, gesture, newId, files),
            "addRow" => AddRow(table, view, gesture, newId),
            "deleteRows" => DeleteRows(table, gesture),

            "addColumn" => AddColumn(table, view, gesture, newId),
            "renameColumn" => RenameColumn(table, gesture),
            "duplicateColumn" => DuplicateColumn(table, view, gesture, newId),
            "deleteColumn" => DeleteColumn(table, gesture, files),
            "setColumnType" => ChangeType(table, gesture, newId),

            "addRelation" => AddRelation(table, view, gesture, newId, files),
            "setColumnParent" => SetParent(table, gesture),

            "addOption" => AddOption(table, gesture, newId),
            "renameOption" => ForOption(table, gesture, (property, option) => RenameOption(property, option, gesture)),
            "recolourOption" => ForOption(table, gesture, (_, option) => RecolourOption(option, gesture)),
            "moveOption" => ForOption(table, gesture, (property, option) => MoveTo(option.Id, property.Id, [.. property.Options.Select(candidate => candidate.Id)], gesture.Index)),
            "deleteOption" => ForOption(table, gesture, (property, option) => DeleteOption(table, property, option)),

            "addView" => AddView(table, newId),
            "renameView" => ForView(table, gesture, target => RenameView(target, gesture)),
            "duplicateView" => ForView(table, gesture, target => DuplicateView(table, target, newId)),
            "deleteView" => ForView(table, gesture, target => DeleteView(table, target)),
            "moveView" => ForView(table, gesture, target => MoveTo(target.Id, null, table.Views.Select(candidate => candidate.Id).ToList(), gesture.Index)),

            _ when view is null => Refused(ViewGone),
            "moveColumn" => MoveColumn(table, view, gesture),
            "resizeColumn" => ResizeColumn(table, view, gesture),
            "hideColumn" => ShowColumn(table, view, gesture, visible: false),
            "showColumn" => ShowColumn(table, view, gesture, visible: true),
            "setColumnWrap" => WrapColumn(table, view, gesture),

            "addSort" => AddSort(table, view, gesture),
            "setSort" => SetSort(view, gesture),
            "removeSort" => RemoveSort(view, gesture),
            "moveSort" => view.Sorts.Any(sort => sort.PropertyId == gesture.ColumnId)
                ? MoveTo(SortId(view, gesture.ColumnId), view.Id, [.. view.Sorts.Select(sort => SortId(view, sort.PropertyId))], gesture.Index)
                : KnowledgeEdit.Nothing,

            "groupBy" => GroupBy(table, view, gesture),
            "setHideEmptyGroups" => KnowledgeEdit.Of(Set(view.Id, ("hideEmptyGroups", Flag(gesture, "hide") ? true : null))),
            "toggleGroup" => Collapse(view, gesture.TargetId, Flag(gesture, "collapsed")),
            "toggleRow" => Collapse(view, gesture.RowId, Flag(gesture, "collapsed")),

            "addFilter" => AddFilter(table, view, gesture, besideItsRule),
            "addFilterGroup" => AddFilterGroup(view, gesture, besideItsRule),
            "setFilter" => SetFilter(table, view, gesture),
            "setFilterMatch" => SetFilterMatch(view, gesture),
            "removeFilter" => FilterItem(view, gesture.TargetId) is { } item && gesture.TargetId.Length > 0 ? KnowledgeEdit.Of(new ModelChange.Remove(item.Id)) : KnowledgeEdit.Nothing,

            _ => Refused("This table does not know how to do that."),
        };
    }

    // ---- cells and rows ----

    private static KnowledgeEdit SetCell(KnowledgeTable table, TableGesture gesture, Func<string> newId, KnowledgeSurroundings? files)
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

        // An option made while filling in the cell: made first, and then chosen like any other.
        if (gesture.Settings.TryGetValue("newOption", out var optionName) && optionName.Trim().Length > 0)
        {
            if (property.ValueType is not ("selection" or "multipleSelection"))
            {
                return Refused("Only a selection has options.");
            }

            var existing = property.Options.FirstOrDefault(option => string.Equals(option.Name, optionName.Trim(), StringComparison.OrdinalIgnoreCase));
            var optionId = existing?.Id ?? newId();
            if (existing is null)
            {
                changes.Add(new ModelChange.Add("Option", optionId, Attributes(("name", optionName.Trim())), property.Id));
            }

            if (property.ValueType == "selection")
            {
                values = [optionId];
            }
            else if (!values.Contains(optionId))
            {
                values.Add(optionId);
            }
        }
        else if (property.ValueType is "selection" or "multipleSelection" && values.FirstOrDefault(value => property.Options.All(option => option.Id != value)) is not null)
        {
            return Refused("That option is no longer one of this property's.");
        }

        var cellId = CellId(row.Id, property.Id);
        var cell = row.Cells.FirstOrDefault(candidate => candidate.PropertyId == property.Id);

        if (property.ValueType is "multipleSelection" or "relation")
        {
            if (property is { ValueType: "relation", Limit: "one" } && values.Count > 1)
            {
                return Refused("This relation holds one row.");
            }

            if (property.ValueType == "relation")
            {
                // A related row is a row of the target: one that is not there is not related to.
                if (files?.TargetOf(property)?.Table is { } target && values.FirstOrDefault(value => target.Rows.All(candidate => candidate.Id != value)) is not null)
                {
                    return Refused($"That row is not in {(target.Name.Length > 0 ? target.Name : "the table this relates to")}.");
                }

                if (property.IsParent && values.Count == 1 && KnowledgeRelations.WouldCycle(table, property, row.Id, values[0]))
                {
                    return Refused(ParentCycle);
                }
            }

            var key = property.ValueType == "relation" ? "row" : "option";
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

        if (values.Count == 0)
        {
            return cell is null ? KnowledgeEdit.Nothing : new KnowledgeEdit([.. changes, new ModelChange.Remove(cellId)]);
        }

        if (Written(property.ValueType, values[0], out var name, out var written) is { Length: > 0 } refusal)
        {
            return Refused(refusal);
        }

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
    /// A value as its type writes it: the key it is stored under and what is stored, or why it is
    /// not a value of that type. Dates and times are ISO 8601, and a date and time carries its
    /// offset: one given without is taken to be this machine's local time.
    /// </summary>
    private static string Written(string valueType, string value, out string key, out object? written)
    {
        key = valueType;
        written = value;
        switch (valueType)
        {
            case "text":
                return "";
            case "number":
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
                {
                    written = whole;
                    return "";
                }

                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var real) && double.IsFinite(real))
                {
                    written = real;
                    return "";
                }

                return NumberExpected;
            case "checkbox":
                key = "checked";
                written = value == "true" ? true : null;
                return "";
            case "date":
                if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    return DateExpected;
                }

                written = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                return "";
            case "dateTime":
                if (Offset().IsMatch(value))
                {
                    return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _) ? "" : DateTimeExpected;
                }

                if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
                {
                    return DateTimeExpected;
                }

                written = new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeZoneInfo.Local.GetUtcOffset(local)).ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
                return "";
            case "time":
                if (!TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                {
                    return TimeExpected;
                }

                written = time.ToString(time.Second == 0 ? "HH:mm" : "HH:mm:ss", CultureInfo.InvariantCulture);
                return "";
            case "selection":
                key = "option";
                return "";
            default:
                return "This value cannot be set here.";
        }
    }

    private static KnowledgeEdit AddRow(KnowledgeTable table, KnowledgeView? view, TableGesture gesture, Func<string> newId)
    {
        var id = newId();
        List<ModelChange> changes = [];

        // After the row the key was pressed in, where there is one; at the end otherwise.
        var after = gesture.TargetId.Length > 0 ? IndexOf(table.Rows, row => row.Id == gesture.TargetId) : -1;
        changes.AddRange(AddAt("Row", id, Attributes(), null, after >= 0 ? after + 1 : null, table.Rows.Count, id));

        // A row added in a group is given the value that puts it there.
        if (gesture.Settings.TryGetValue("group", out var group) && group.Length > 0
            && view is not null && table.Properties.FirstOrDefault(property => property.Id == view.GroupBy) is { IsComputed: false } grouped)
        {
            var cellId = CellId(id, grouped.Id);
            switch (grouped.ValueType)
            {
                case "selection" when grouped.Options.Any(option => option.Id == group):
                    changes.Add(new ModelChange.Add("Cell", null, Attributes(("property", grouped.Id), ("option", group)), id));
                    break;
                case "multipleSelection" when grouped.Options.Any(option => option.Id == group):
                    changes.Add(new ModelChange.Add("Cell", null, Attributes(("property", grouped.Id)), id));
                    changes.Add(new ModelChange.Add("CellItem", null, Attributes(("option", group)), cellId));
                    break;
                case "relation":
                    changes.Add(new ModelChange.Add("Cell", null, Attributes(("property", grouped.Id)), id));
                    changes.Add(new ModelChange.Add("CellItem", null, Attributes(("row", group)), cellId));
                    break;
                case "checkbox" when group == "true":
                    changes.Add(new ModelChange.Add("Cell", null, Attributes(("property", grouped.Id), ("checked", true)), id));
                    break;
            }
        }

        return new KnowledgeEdit(changes, NewRowId: id);
    }

    private static KnowledgeEdit DeleteRows(KnowledgeTable table, TableGesture gesture)
    {
        var ids = (gesture.Values.Count > 0 ? gesture.Values : [gesture.RowId]).Where(id => table.Rows.Any(row => row.Id == id)).Distinct().ToList();
        return ids.Count == 0 ? Refused(RowGone) : new KnowledgeEdit([.. ids.Select(id => new ModelChange.Remove(id))]);
    }

    // ---- properties ----

    private static KnowledgeEdit AddColumn(KnowledgeTable table, KnowledgeView? view, TableGesture gesture, Func<string> newId)
    {
        var type = gesture.Settings.GetValueOrDefault("type") ?? "text";
        if (type == "relation")
        {
            return Refused(RelationNeedsTarget);
        }

        if (!KnowledgeVocabulary.ValueTypes.Contains(type) || KnowledgeDefinition.ValueTypeLabel(type) is not { } label)
        {
            return Refused($"'{type}' is not a type a property can have.");
        }

        var id = newId();
        var name = UniqueName(table.Properties.Select(property => property.Name), label);
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

    private static KnowledgeEdit RenameColumn(KnowledgeTable table, TableGesture gesture)
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

    private static KnowledgeEdit DuplicateColumn(KnowledgeTable table, KnowledgeView? view, TableGesture gesture, Func<string> newId)
    {
        if (table.Properties.FirstOrDefault(candidate => candidate.Id == gesture.ColumnId) is not { } property)
        {
            return Refused(PropertyGone);
        }

        // The copy is a property like the first: its type and its options, each with an id of its
        // own. It is never the title, and never the other side of the first one's relation.
        var id = newId();
        var attributes = Attributes(("name", UniqueName(table.Properties.Select(other => other.Name), property.Name)), ("valueType", property.ValueType));
        if (property is { ValueType: "relation", IsComputed: false })
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

    public const string OtherSideChoice = "This relation has another side: choose whether that is deleted too or kept as a one-way relation.";

    private static KnowledgeEdit DeleteColumn(KnowledgeTable table, TableGesture gesture, KnowledgeSurroundings? files)
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
            changes.AddRange(UngroupSettings(view));
            changes.Add(Set(view.Id, ("groupBy", null)));
        }

        changes.Add(new ModelChange.Remove(property.Id));

        // One side of a two-way relation: what becomes of the other side is its author's choice, never a guess.
        var self = property.TargetFile is KnowledgeRelations.Self or "";
        var target = self ? null : files?.TargetOf(property);
        var related = self ? table : target?.Table;
        if (property is not { ValueType: "relation", Counterpart.Length: > 0 } || related?.Properties.FirstOrDefault(candidate => candidate.Id == property.Counterpart) is not { } other)
        {
            return new KnowledgeEdit(changes);
        }

        List<ModelChange> theirs = [];
        switch (gesture.Settings.GetValueOrDefault("otherSide"))
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
                return Refused(OtherSideChoice);
        }

        return self ? new KnowledgeEdit([.. theirs, .. changes]) : new KnowledgeEdit(changes, Others: [new KnowledgeOtherFile(target!.Path, theirs)]);
    }

    // ---- relations ----

    public const string ParentCycle = "A row cannot be its own ancestor.";

    /// <summary>
    /// A relation to another knowledge file, or to this one. One-way, it is a property of this file.
    /// Two-way, the target gets the other side in the same step: a property of its own, computed and
    /// without cells, each naming the other. Neither file is written when either refuses.
    /// </summary>
    private static KnowledgeEdit AddRelation(KnowledgeTable table, KnowledgeView? view, TableGesture gesture, Func<string> newId, KnowledgeSurroundings? files)
    {
        var targetName = (gesture.Settings.GetValueOrDefault("target") ?? "").Trim();
        if (files is null || targetName.Length == 0)
        {
            return Refused(RelationNeedsTarget);
        }

        var target = files.TargetAt(targetName);
        if (target.Table is not { } related)
        {
            return Refused(target.Problem);
        }

        var self = string.Equals(target.Path, Path.GetFullPath(files.BodyPath), StringComparison.OrdinalIgnoreCase);
        var asked = (gesture.Values.FirstOrDefault() ?? "").Trim();
        var name = UniqueName(table.Properties.Select(property => property.Name), asked.Length > 0 ? asked : related.Name.Length > 0 && !self ? related.Name : "Relation");
        var id = newId();
        var attributes = Attributes(
            ("name", name),
            ("valueType", "relation"),
            ("targetFile", self ? KnowledgeRelations.Self : KnowledgeRelations.TargetName(files.BodyPath, target.Path)),
            ("limit", gesture.Settings.GetValueOrDefault("limit") == "one" ? "one" : null));

        var otherName = (gesture.Settings.GetValueOrDefault("counterpart") ?? "").Trim();
        if (otherName.Length == 0)
        {
            return new KnowledgeEdit(Place(table, view, id, attributes, gesture.TargetId, left: false));
        }

        // The other side: in the target, computed, naming this one - and this one naming it.
        var otherId = newId();
        attributes["counterpart"] = otherId;
        var taken = self ? table.Properties.Select(property => property.Name).Append(name) : related.Properties.Select(property => property.Name);
        var other = new ModelChange.Add(
            "Property",
            otherId,
            Attributes(
                ("name", UniqueName(taken, otherName)),
                ("valueType", "relation"),
                ("targetFile", self ? KnowledgeRelations.Self : KnowledgeRelations.TargetName(target.Path, files.BodyPath)),
                ("computed", true),
                ("counterpart", id)));
        var changes = Place(table, view, id, attributes, gesture.TargetId, left: false);
        if (self)
        {
            changes.Add(other);
            return new KnowledgeEdit(changes);
        }

        return new KnowledgeEdit(changes, Others: [new KnowledgeOtherFile(target.Path, [other])]);
    }

    /// <summary>
    /// Makes a relation the parent relation, or no longer that: the one a view nests its rows by.
    /// Only a relation to the table itself that holds one row can be it, and a table has one.
    /// </summary>
    private static KnowledgeEdit SetParent(KnowledgeTable table, TableGesture gesture)
    {
        if (table.Properties.FirstOrDefault(candidate => candidate.Id == gesture.ColumnId) is not { } property)
        {
            return Refused(PropertyGone);
        }

        var parent = Flag(gesture, "parent");
        if (parent == property.IsParent)
        {
            return KnowledgeEdit.Nothing;
        }

        if (!parent)
        {
            return KnowledgeEdit.Of(Set(property.Id, ("isParent", null)));
        }

        if (property is not { ValueType: "relation", TargetFile: KnowledgeRelations.Self or "", Limit: "one", IsComputed: false })
        {
            return Refused("Only a relation to this table that holds one row can be the parent relation.");
        }

        if (table.Properties.FirstOrDefault(other => other.IsParent) is { } already)
        {
            return Refused($"'{already.Name}' is the parent relation already.");
        }

        // What is there must not be a circle already: a parent relation is never one.
        return table.Rows.Any(row => row.Cells.FirstOrDefault(cell => cell.PropertyId == property.Id && cell.Key == "rows")?.Values.FirstOrDefault() is { } above
                                     && KnowledgeRelations.WouldCycle(table, property, row.Id, above))
            ? Refused("Some rows of this relation are each other's ancestors, so it cannot be the parent relation.")
            : KnowledgeEdit.Of(Set(property.Id, ("isParent", true)));
    }

    // ---- a selection's options ----

    private const string NotASelection = "Only a selection has options.";
    private const string OptionGone = "That option is no longer one of this property's.";
    public const string OptionNameNeeded = "An option needs a name.";

    /// <summary>The selection a gesture names, and the option of it the gesture names, handed to what the gesture does.</summary>
    private static KnowledgeEdit ForOption(KnowledgeTable table, TableGesture gesture, Func<KnowledgeProperty, KnowledgeOption, KnowledgeEdit> edit)
    {
        if (table.Properties.FirstOrDefault(candidate => candidate.Id == gesture.ColumnId) is not { } property)
        {
            return Refused(PropertyGone);
        }

        if (property.ValueType is not ("selection" or "multipleSelection"))
        {
            return Refused(NotASelection);
        }

        return property.Options.FirstOrDefault(candidate => candidate.Id == gesture.TargetId) is { } option ? edit(property, option) : Refused(OptionGone);
    }

    /// <summary>A name for an option of a property: given, and not the name of another of its options.</summary>
    private static string OptionName(KnowledgeProperty property, string? exceptId, TableGesture gesture, out string name)
    {
        name = (gesture.Values.FirstOrDefault() ?? "").Trim();
        var given = name;
        if (name.Length == 0)
        {
            return OptionNameNeeded;
        }

        return property.Options.Any(other => other.Id != exceptId && string.Equals(other.Name, given, StringComparison.OrdinalIgnoreCase))
            ? $"'{property.Name}' already has an option called '{given}'."
            : "";
    }

    private static KnowledgeEdit AddOption(KnowledgeTable table, TableGesture gesture, Func<string> newId)
    {
        if (table.Properties.FirstOrDefault(candidate => candidate.Id == gesture.ColumnId) is not { } property)
        {
            return Refused(PropertyGone);
        }

        if (property.ValueType is not ("selection" or "multipleSelection"))
        {
            return Refused(NotASelection);
        }

        return OptionName(property, null, gesture, out var name) is { Length: > 0 } refusal
            ? Refused(refusal)
            : KnowledgeEdit.Of(new ModelChange.Add("Option", newId(), Attributes(("name", name)), property.Id));
    }

    /// <summary>A rename is one place in the file: rows hold an option's id, never its name.</summary>
    private static KnowledgeEdit RenameOption(KnowledgeProperty property, KnowledgeOption option, TableGesture gesture)
    {
        if (OptionName(property, option.Id, gesture, out var name) is { Length: > 0 } refusal)
        {
            return Refused(refusal);
        }

        return name == option.Name ? KnowledgeEdit.Nothing : KnowledgeEdit.Of(Set(option.Id, ("name", name)));
    }

    private static KnowledgeEdit RecolourOption(KnowledgeOption option, TableGesture gesture)
    {
        var colour = gesture.Settings.GetValueOrDefault("colour") ?? "";
        if (!KnowledgeVocabulary.Colours.Contains(colour))
        {
            return Refused($"'{colour}' is not a colour an option can have.");
        }

        // The default colour is what an option has when the file says nothing.
        return colour == option.Colour ? KnowledgeEdit.Nothing : KnowledgeEdit.Of(Set(option.Id, ("colour", colour == "default" ? null : colour)));
    }

    /// <summary>
    /// An option deleted with every value that names it, in one step: the cells of a selection, the
    /// items of a multiple selection and the conditions comparing with it go with the option as the
    /// bindings cascade, and what a view grouped by this property remembered of the option's group
    /// goes here.
    /// </summary>
    private static KnowledgeEdit DeleteOption(KnowledgeTable table, KnowledgeProperty property, KnowledgeOption option)
    {
        List<ModelChange> changes = [];
        foreach (var view in table.Views.Where(view => view.GroupBy == property.Id))
        {
            changes.AddRange(new[] { ("groupOrder", view.GroupOrder), ("hiddenGroups", view.HiddenGroups), ("collapsed", view.Collapsed) }
                .Where(setting => setting.Item2.Contains(option.Id))
                .Select(setting => (ModelChange)new ModelChange.Remove($"{view.Id}/{setting.Item1}/{option.Id}")));
        }

        changes.Add(new ModelChange.Remove(option.Id));
        return new KnowledgeEdit(changes);
    }

    // ---- a property's type ----

    /// <summary>The key a cell holds a value of this type under: one of a cell's attributes, or the name of its list of several.</summary>
    internal static string KeyOf(string valueType) => valueType switch
    {
        "multipleSelection" => "options",
        "relation" => "rows",
        _ => KnowledgeVocabulary.StoredKey(valueType),
    };

    /// <summary>
    /// A change of type, with every value that has a meaning in the new type converted in the same
    /// step, as the conversion table of the designer's specification says. A value that has none
    /// is not touched: it stays under the key it has, to be reported on its cell and to be there
    /// again when the type is changed back. Nothing is discarded.
    /// </summary>
    private static KnowledgeEdit ChangeType(KnowledgeTable table, TableGesture gesture, Func<string> newId)
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

        if (to == "relation")
        {
            return Refused(RelationNeedsTarget);
        }

        if (!KnowledgeVocabulary.ValueTypes.Contains(to))
        {
            return Refused($"'{to}' is not a type a property can have.");
        }

        List<ModelChange> changes = [Set(property.Id, ("valueType", to))];
        var from = property.ValueType;
        var held = KeyOf(from);
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

            var cellId = CellId(row.Id, property.Id);
            var value = cell.Values[0];
            switch (from, to)
            {
                case ("text", "checkbox") when value.Trim().ToLowerInvariant() is "true" or "false":
                    changes.Add(Set(cellId, ("text", null), ("checked", value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))));
                    break;
                case ("text", "number" or "date" or "dateTime" or "time") when Written(to, value.Trim(), out var key, out var written).Length == 0:
                    changes.Add(Set(cellId, ("text", null), (key, written)));
                    break;
                case ("text", "selection") when value.Trim().Length > 0:
                    changes.Add(Set(cellId, ("text", null), ("option", Option(value.Trim()))));
                    break;
                case ("text", "multipleSelection") when value.Trim().Length > 0:
                    var made = Option(value.Trim());
                    changes.Add(Set(cellId, ("text", null)));
                    changes.Add(new ModelChange.Add("CellItem", null, Attributes(("option", made)), cellId));
                    break;
                case ("number" or "checkbox" or "date" or "dateTime" or "time", "text"):
                    changes.Add(Set(cellId, (held, null), ("text", value)));
                    break;
                case ("selection", "text") when names.TryGetValue(value, out var name):
                    changes.Add(Set(cellId, ("option", null), ("text", name)));
                    break;
                case ("selection", "multipleSelection"):
                    changes.Add(Set(cellId, ("option", null)));
                    changes.Add(new ModelChange.Add("CellItem", null, Attributes(("option", value)), cellId));
                    break;
                case ("multipleSelection", "text") when cell.Values.All(names.ContainsKey):
                    changes.Add(Set(cellId, ("text", string.Join(", ", cell.Values.Select(item => names[item])))));
                    changes.AddRange(cell.Values.Select(item => (ModelChange)new ModelChange.Remove($"{cellId}/{item}")));
                    break;
                case ("multipleSelection", "selection") when cell.Values.Count == 1:
                    changes.Add(new ModelChange.Remove($"{cellId}/{value}"));
                    changes.Add(Set(cellId, ("option", value)));
                    break;
            }
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

    // ---- a view's columns ----

    private static KnowledgeEdit MoveColumn(KnowledgeTable table, KnowledgeView view, TableGesture gesture)
    {
        // The order the view shows: what it lists, then every other property in the file's order.
        var order = view.Columns.Select(column => column.PropertyId).Where(id => table.Properties.Any(property => property.Id == id)).ToList();
        order.AddRange(table.Properties.Select(property => property.Id).Where(id => !order.Contains(id)));
        if (!order.Contains(gesture.ColumnId))
        {
            return Refused(PropertyGone);
        }

        var others = order.Where(id => id != gesture.ColumnId).ToList();
        var shown = others.Where(Shown).ToList();
        var to = gesture.Index >= 0 && gesture.Index < shown.Count ? others.IndexOf(shown[gesture.Index]) : others.Count;
        if (order.IndexOf(gesture.ColumnId) == to)
        {
            return KnowledgeEdit.Nothing;
        }

        // An order is the view's own, so the view has to list everything up to where the column lands.
        List<ModelChange> changes = [];
        var listed = view.Columns.Select(column => column.PropertyId).ToList();
        foreach (var id in others.Take(to).Append(gesture.ColumnId).Where(id => !listed.Contains(id)))
        {
            changes.Add(new ModelChange.Add("Column", null, Attributes(("property", id)), view.Id));
            listed.Add(id);
        }

        changes.AddRange(MoveTo(ColumnId(view, gesture.ColumnId), view.Id, [.. listed.Select(id => ColumnId(view, id))], Math.Min(to, listed.Count - 1)).Changes);
        return new KnowledgeEdit(changes);

        // The index is among the columns in sight; the one that will follow the moved column says where that is among all of them.
        bool Shown(string id) => table.Properties.First(property => property.Id == id).IsTitle || (view.Columns.FirstOrDefault(column => column.PropertyId == id)?.Visible ?? true);
    }

    private static KnowledgeEdit ResizeColumn(KnowledgeTable table, KnowledgeView view, TableGesture gesture) =>
        int.TryParse(gesture.Settings.GetValueOrDefault("width"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) && width > 0
            ? ColumnSetting(table, view, gesture.ColumnId, "width", (long)Math.Max(MinimumWidth, width))
            : Refused("A width is a number of pixels.");

    private static KnowledgeEdit ShowColumn(KnowledgeTable table, KnowledgeView view, TableGesture gesture, bool visible)
    {
        if (!visible && table.Properties.FirstOrDefault(property => property.Id == gesture.ColumnId) is { IsTitle: true })
        {
            return Refused(TitleStays);
        }

        // Shown is what a column is unless the view says otherwise, so only hidden is written.
        return ColumnSetting(table, view, gesture.ColumnId, "visible", visible ? null : false);
    }

    private static KnowledgeEdit WrapColumn(KnowledgeTable table, KnowledgeView view, TableGesture gesture) =>
        ColumnSetting(table, view, gesture.ColumnId, "wrap", Flag(gesture, "wrap") ? true : null);

    /// <summary>One setting of one column of one view: set on its entry, which is written only when there is something to say.</summary>
    private static KnowledgeEdit ColumnSetting(KnowledgeTable table, KnowledgeView view, string propertyId, string name, object? value)
    {
        if (table.Properties.All(property => property.Id != propertyId))
        {
            return Refused(PropertyGone);
        }

        if (view.Columns.Any(column => column.PropertyId == propertyId))
        {
            return KnowledgeEdit.Of(Set(ColumnId(view, propertyId), (name, value)));
        }

        return value is null ? KnowledgeEdit.Nothing : KnowledgeEdit.Of(new ModelChange.Add("Column", null, Attributes(("property", propertyId), (name, value)), view.Id));
    }

    // ---- views ----

    private static KnowledgeEdit ForView(KnowledgeTable table, TableGesture gesture, Func<KnowledgeView, KnowledgeEdit> edit) =>
        table.Views.FirstOrDefault(candidate => candidate.Id == gesture.ViewId) is { } view ? edit(view) : Refused(ViewGone);

    private static KnowledgeEdit AddView(KnowledgeTable table, Func<string> newId)
    {
        var id = newId();
        return new KnowledgeEdit([new ModelChange.Add("View", id, Attributes(("name", UniqueName(table.Views.Select(view => view.Name), "View"))))], NewViewId: id);
    }

    private static KnowledgeEdit RenameView(KnowledgeView view, TableGesture gesture)
    {
        var name = (gesture.Values.FirstOrDefault() ?? "").Trim();
        return name.Length == 0 ? Refused(ViewNameNeeded) : name == view.Name ? KnowledgeEdit.Nothing : KnowledgeEdit.Of(Set(view.Id, ("name", name)));
    }

    private static KnowledgeEdit DuplicateView(KnowledgeTable table, KnowledgeView view, Func<string> newId)
    {
        var id = newId();
        var at = IndexOf(table.Views, candidate => candidate.Id == view.Id) + 1;
        List<ModelChange> changes =
        [
            .. AddAt(
                "View",
                id,
                Attributes(
                    ("name", UniqueName(table.Views.Select(candidate => candidate.Name), view.Name)),
                    ("filterMatch", view.Filter.Any ? "any" : null),
                    ("groupBy", view.GroupBy.Length > 0 ? view.GroupBy : null),
                    ("hideEmptyGroups", view.HideEmptyGroups ? true : null)),
                null,
                at,
                table.Views.Count,
                id),
            .. view.Columns.Select(column => (ModelChange)new ModelChange.Add(
                "Column",
                null,
                Attributes(("property", column.PropertyId), ("visible", column.Visible ? null : false), ("width", column.Width > 0 ? (long)column.Width : null), ("wrap", column.Wrap ? true : null)),
                id)),
            .. view.Sorts.Select(sort => (ModelChange)new ModelChange.Add("Sort", null, Attributes(("property", sort.PropertyId), ("direction", sort.Descending ? "descending" : null)), id)),
        ];
        CopyFilter(table, view.Filter, id, $"{id}/filter", changes);
        changes.AddRange(view.GroupOrder.Select(key => (ModelChange)new ModelChange.Add("GroupSetting", null, Attributes(("key", key)), id, Slot: "groupOrder")));
        changes.AddRange(view.HiddenGroups.Select(key => (ModelChange)new ModelChange.Add("GroupSetting", null, Attributes(("key", key)), id, Slot: "hiddenGroups")));
        changes.AddRange(view.Collapsed.Select(key => (ModelChange)new ModelChange.Add("GroupSetting", null, Attributes(("key", key)), id, Slot: "collapsed")));
        return new KnowledgeEdit(changes, NewViewId: id);
    }

    /// <summary>A filter's items under a new parent, each addressed by its place as the reading will address it.</summary>
    private static void CopyFilter(KnowledgeTable table, KnowledgeFilterGroup group, string parentId, string prefix, List<ModelChange> changes)
    {
        for (var index = 0; index < group.Items.Count; index++)
        {
            var itemId = FormattableString.Invariant($"{prefix}/{index}");
            switch (group.Items[index])
            {
                case KnowledgeFilterGroup nested:
                    changes.Add(new ModelChange.Add("FilterGroup", null, Attributes(("match", nested.Any ? "any" : "all")), parentId));
                    CopyFilter(table, nested, itemId, itemId, changes);
                    break;
                case KnowledgeCondition condition:
                    var attributes = Attributes(("property", condition.PropertyId), ("operator", condition.Operator));
                    if (condition.Value.Length > 0 && ConditionValue(table, condition.PropertyId, condition.Value, out var key, out var written).Length == 0 && written is not null)
                    {
                        attributes[key] = written;
                    }

                    changes.Add(new ModelChange.Add("Condition", null, attributes, parentId));
                    break;
            }
        }
    }

    private static KnowledgeEdit DeleteView(KnowledgeTable table, KnowledgeView view)
    {
        if (table.Views.Count <= 1)
        {
            return Refused(LastView);
        }

        // The file no longer says it was left in a view that is gone.
        return table.ActiveViewId == view.Id
            ? KnowledgeEdit.Of(Set(TableId, ("activeView", null)), new ModelChange.Remove(view.Id))
            : KnowledgeEdit.Of(new ModelChange.Remove(view.Id));
    }

    // ---- sorts and groups ----

    private static KnowledgeEdit AddSort(KnowledgeTable table, KnowledgeView view, TableGesture gesture)
    {
        if (table.Properties.All(property => property.Id != gesture.ColumnId))
        {
            return Refused(PropertyGone);
        }

        var direction = gesture.Settings.GetValueOrDefault("direction") == "descending" ? "descending" : null;
        return view.Sorts.Any(sort => sort.PropertyId == gesture.ColumnId)
            ? KnowledgeEdit.Of(Set(SortId(view, gesture.ColumnId), ("direction", direction)))
            : KnowledgeEdit.Of(new ModelChange.Add("Sort", null, Attributes(("property", gesture.ColumnId), ("direction", direction)), view.Id));
    }

    private static KnowledgeEdit SetSort(KnowledgeView view, TableGesture gesture) =>
        view.Sorts.Any(sort => sort.PropertyId == gesture.ColumnId)
            ? KnowledgeEdit.Of(Set(SortId(view, gesture.ColumnId), ("direction", gesture.Settings.GetValueOrDefault("direction") == "descending" ? "descending" : null)))
            : KnowledgeEdit.Nothing;

    private static KnowledgeEdit RemoveSort(KnowledgeView view, TableGesture gesture) =>
        view.Sorts.Any(sort => sort.PropertyId == gesture.ColumnId) ? KnowledgeEdit.Of(new ModelChange.Remove(SortId(view, gesture.ColumnId))) : KnowledgeEdit.Nothing;

    private static KnowledgeEdit GroupBy(KnowledgeTable table, KnowledgeView view, TableGesture gesture)
    {
        if (gesture.ColumnId == view.GroupBy)
        {
            return KnowledgeEdit.Nothing;
        }

        if (gesture.ColumnId.Length > 0)
        {
            if (table.Properties.FirstOrDefault(candidate => candidate.Id == gesture.ColumnId) is not { } property)
            {
                return Refused(PropertyGone);
            }

            if (property.ValueType is not ("selection" or "multipleSelection" or "checkbox" or "relation"))
            {
                return Refused("A view is grouped by a selection, a checkbox or a relation.");
            }
        }

        // What the view remembered of its groups was about the old grouping's groups.
        return new KnowledgeEdit([.. UngroupSettings(view), Set(view.Id, ("groupBy", gesture.ColumnId.Length > 0 ? gesture.ColumnId : null))]);
    }

    private static IEnumerable<ModelChange> UngroupSettings(KnowledgeView view) =>
        view.GroupOrder.Select(key => $"{view.Id}/groupOrder/{key}")
            .Concat(view.HiddenGroups.Select(key => $"{view.Id}/hiddenGroups/{key}"))
            .Concat(view.Collapsed.Select(key => $"{view.Id}/collapsed/{key}"))
            .Select(id => (ModelChange)new ModelChange.Remove(id));

    private static KnowledgeEdit Collapse(KnowledgeView view, string key, bool collapsed)
    {
        if (key.Length == 0 || view.Collapsed.Contains(key) == collapsed)
        {
            return KnowledgeEdit.Nothing;
        }

        return collapsed
            ? KnowledgeEdit.Of(new ModelChange.Add("GroupSetting", null, Attributes(("key", key)), view.Id, Slot: "collapsed"))
            : KnowledgeEdit.Of(new ModelChange.Remove($"{view.Id}/collapsed/{key}"));
    }

    // ---- the filter ----

    /// <summary>A filter's item and its id, by its path: empty for the view's own group, else places joined by a slash.</summary>
    private static (string Id, KnowledgeFilterItem Item, int Depth)? FilterItem(KnowledgeView view, string path)
    {
        KnowledgeFilterItem item = view.Filter;
        var id = $"{view.Id}/filter";
        var depth = 0;
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (item is not KnowledgeFilterGroup group || !int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index >= group.Items.Count)
            {
                return null;
            }

            item = group.Items[index];
            id = FormattableString.Invariant($"{id}/{index}");
            depth++;
        }

        return (depth == 0 ? view.Id : id, item, depth);
    }

    /// <summary>
    /// A condition or a group added as the last item of a group, in every format. A format that adds
    /// an entry after the last one its rule reads would put a condition before the groups that follow
    /// the conditions; there the item is moved to the end in the same step, so a filter reads the same
    /// whichever format the file has.
    /// </summary>
    private static KnowledgeEdit AddToFilter(string type, Dictionary<string, object?> attributes, KnowledgeView view, (string Id, KnowledgeFilterItem Item, int Depth) group, bool besideItsRule)
    {
        var items = ((KnowledgeFilterGroup)group.Item).Items;
        var add = new ModelChange.Add(type, null, attributes, group.Id);
        if (!besideItsRule)
        {
            return KnowledgeEdit.Of(add);
        }

        var last = -1;
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is KnowledgeCondition == (type == "Condition"))
            {
                last = index;
            }
        }

        if (last < 0 || last == items.Count - 1)
        {
            return KnowledgeEdit.Of(add);
        }

        var prefix = group.Depth == 0 ? $"{view.Id}/filter" : group.Id;
        return KnowledgeEdit.Of(add, new ModelChange.Move(FormattableString.Invariant($"{prefix}/{last + 1}"), group.Id, items.Count + 1));
    }

    private static KnowledgeEdit AddFilter(KnowledgeTable table, KnowledgeView view, TableGesture gesture, bool besideItsRule)
    {
        if (table.Properties.FirstOrDefault(candidate => candidate.Id == gesture.ColumnId) is not { } property)
        {
            return Refused(PropertyGone);
        }

        if (FilterItem(view, gesture.TargetId) is not { Item: KnowledgeFilterGroup } group)
        {
            return Refused("That group is no longer in this filter.");
        }

        var comparison = gesture.Settings.GetValueOrDefault("comparison") is { Length: > 0 } asked ? Comparison(asked) : KnowledgeVocabulary.Comparisons.GetValueOrDefault(property.ValueType)?.FirstOrDefault() ?? "is-empty";
        return AddToFilter("Condition", Attributes(("property", property.Id), ("operator", comparison)), view, group, besideItsRule);
    }

    private static KnowledgeEdit AddFilterGroup(KnowledgeView view, TableGesture gesture, bool besideItsRule)
    {
        if (FilterItem(view, gesture.TargetId) is not { Item: KnowledgeFilterGroup } group)
        {
            return Refused("That group is no longer in this filter.");
        }

        return group.Depth >= FilterDepth
            ? Refused("A filter's groups go two deep.")
            : AddToFilter("FilterGroup", Attributes(("match", "all")), view, group, besideItsRule);
    }

    private static KnowledgeEdit SetFilter(KnowledgeTable table, KnowledgeView view, TableGesture gesture)
    {
        if (FilterItem(view, gesture.TargetId) is not { Item: KnowledgeCondition condition } item)
        {
            return Refused("That condition is no longer in this filter.");
        }

        var propertyId = gesture.ColumnId.Length > 0 ? gesture.ColumnId : condition.PropertyId;
        if (table.Properties.All(property => property.Id != propertyId))
        {
            return Refused(PropertyGone);
        }

        // One value under the key its property's type writes it under, and no other value left beside it.
        var attributes = Attributes(
            ("property", propertyId),
            ("operator", gesture.Settings.GetValueOrDefault("comparison") is { Length: > 0 } comparison ? Comparison(comparison) : condition.Operator),
            ("text", null), ("number", null), ("checked", null), ("date", null), ("dateTime", null), ("time", null), ("option", null));
        var value = gesture.Values.FirstOrDefault() ?? "";
        if (value.Length > 0)
        {
            if (ConditionValue(table, propertyId, value, out var key, out var written) is { Length: > 0 } refusal)
            {
                return Refused(refusal);
            }

            attributes[key] = written;
        }

        return KnowledgeEdit.Of(new ModelChange.Set(item.Id, attributes));
    }

    /// <summary>What a condition is compared with, as its property's type writes it. Several values are compared one at a time.</summary>
    private static string ConditionValue(KnowledgeTable table, string propertyId, string value, out string key, out object? written)
    {
        var type = table.Properties.FirstOrDefault(property => property.Id == propertyId)?.ValueType ?? "text";
        switch (type)
        {
            case "multipleSelection":
                key = "option";
                written = value;
                return "";
            case "relation":
                key = "text";
                written = value;
                return "";
            default:
                return Written(type, value, out key, out written);
        }
    }

    private static KnowledgeEdit SetFilterMatch(KnowledgeView view, TableGesture gesture)
    {
        if (FilterItem(view, gesture.TargetId) is not { Item: KnowledgeFilterGroup } group)
        {
            return Refused("That group is no longer in this filter.");
        }

        var any = gesture.Settings.GetValueOrDefault("match") == "any";
        return group.Depth == 0
            ? KnowledgeEdit.Of(Set(view.Id, ("filterMatch", any ? "any" : null)))
            : KnowledgeEdit.Of(Set(group.Id, ("match", any ? "any" : "all")));
    }

    // ---- what they share ----

    /// <summary>
    /// Something added at a place among its siblings. A declared binding adds at the end and nowhere
    /// else, so a place is two changes of the one step: added last, then moved to where it belongs.
    /// </summary>
    /// <param name="index">Where it is to stand, or null for the end.</param>
    /// <param name="count">How many siblings there are before it is added.</param>
    /// <param name="addedId">The id the added element is read under, stored or derived.</param>
    private static IEnumerable<ModelChange> AddAt(string type, string? id, Dictionary<string, object?> attributes, string? parentId, int? index, int count, string addedId)
    {
        yield return new ModelChange.Add(type, id, attributes, parentId);
        if (index is { } at && at < count)
        {
            yield return new ModelChange.Move(addedId, parentId, at);
        }
    }

    /// <summary>
    /// A comparison by the name the file knows it by. The two every type has - is empty, is not
    /// empty - are the table library's own and arrive under its names for them.
    /// </summary>
    private static string Comparison(string asked) => asked switch
    {
        "isEmpty" => "is-empty",
        "isNotEmpty" => "is-not-empty",
        _ => asked,
    };

    private static string CellId(string rowId, string propertyId) => $"{rowId}/{propertyId}";

    private static string ColumnId(KnowledgeView view, string propertyId) => $"{view.Id}/columns/{propertyId}";

    private static string SortId(KnowledgeView view, string propertyId) => $"{view.Id}/sorts/{propertyId}";

    private static KnowledgeEdit Refused(string reason) => KnowledgeEdit.Refused(reason);

    private static bool Flag(TableGesture gesture, string name) => gesture.Settings.GetValueOrDefault(name) == "true";

    private static ModelChange.Set Set(string id, params (string Name, object? Value)[] attributes) => new(id, Attributes(attributes));

    private static Dictionary<string, object?> Attributes(params (string Name, object? Value)[] attributes)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string name, object? value) in attributes)
        {
            result[name] = value;
        }

        return result;
    }

    private static int IndexOf<T>(IReadOnlyList<T> items, Func<T, bool> matches)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (matches(items[index]))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Moves one of <paramref name="siblings"/> so that it ends at <paramref name="to"/> among them.
    /// The runtime's index counts the siblings as they are before the move, the moved one included,
    /// so a place further on is one more than where it ends.
    /// </summary>
    private static KnowledgeEdit MoveTo(string id, string? parentId, IReadOnlyList<string> siblings, int to)
    {
        var from = IndexOf(siblings, sibling => sibling == id);
        var target = Math.Clamp(to, 0, siblings.Count - 1);
        return from < 0 || from == target ? KnowledgeEdit.Nothing : KnowledgeEdit.Of(new ModelChange.Move(id, parentId, target > from ? target + 1 : target));
    }

    /// <summary><paramref name="name"/>, or it with the lowest number that no other of <paramref name="taken"/> has.</summary>
    private static string UniqueName(IEnumerable<string> taken, string name)
    {
        var names = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(name))
        {
            return name;
        }

        for (var number = 2; ; number++)
        {
            var candidate = FormattableString.Invariant($"{name} {number}");
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
