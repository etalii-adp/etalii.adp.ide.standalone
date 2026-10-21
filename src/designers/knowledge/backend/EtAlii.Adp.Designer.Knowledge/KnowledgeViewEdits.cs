using System.Globalization;
using EtAlii.Adp.Designer.TableModel;
using EtAlii.Adp.Specification.Fbl.Planning;
using static EtAlii.Adp.Designer.Knowledge.KnowledgeEdits;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// The gestures that change a view: the views themselves, and how the view shown shows the rows -
/// its columns, its sorts and its groups (<c>KnowledgeViewEdits.Groups.cs</c>). Its filter is a
/// filter's edit.
/// </summary>
internal static partial class KnowledgeViewEdits
{
    private const string ViewNameNeeded = "A view needs a name.";
    public const string LastView = "A table keeps at least one view.";

    /// <summary>The narrowest a column is, in pixels, as the specification has it.</summary>
    private const int MinimumWidth = 32;

    // ---- views ----

    /// <summary>The view a gesture names itself, handed to what the gesture does: not the view shown.</summary>
    private static KnowledgeEdit ForView(KnowledgeTable table, TableGesture gesture, Func<KnowledgeView, KnowledgeEdit> edit) =>
        table.Views.FirstOrDefault(candidate => candidate.Id == gesture.ViewId) is { } view ? edit(view) : Refused(ViewGone);

    internal static KnowledgeEdit AddView(KnowledgeTable table, Func<string> newId)
    {
        var id = newId();
        return new KnowledgeEdit([new ModelChange.Add("View", id, Attributes(("name", UniqueName(table.Views.Select(view => view.Name), "View"))))], NewViewId: id);
    }

    internal static KnowledgeEdit RenameView(KnowledgeTable table, TableGesture gesture) =>
        ForView(table, gesture, target => RenameView(target, gesture));

    internal static KnowledgeEdit DuplicateView(KnowledgeTable table, TableGesture gesture, Func<string> newId) =>
        ForView(table, gesture, target => DuplicateView(table, target, newId));

    internal static KnowledgeEdit DeleteView(KnowledgeTable table, TableGesture gesture) =>
        ForView(table, gesture, target => DeleteView(table, target));

    internal static KnowledgeEdit MoveView(KnowledgeTable table, TableGesture gesture) =>
        ForView(table, gesture, target => MoveTo(target.Id, null, table.Views.Select(candidate => candidate.Id).ToList(), gesture.Index));

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
                    ("hideEmptyGroups", view.HideEmptyGroups ? true : null),
                    ("freeze", view.Freeze.Length > 0 ? view.Freeze : null)),
                null,
                at,
                table.Views.Count,
                id),
            .. view.Columns.Select(column => (ModelChange)new ModelChange.Add(
                "Column",
                null,
                Attributes(("property", column.PropertyId), ("visible", column.Visible ? null : false), ("width", column.Width > 0 ? (long)column.Width : null), ("wrap", column.Wrap ? true : null), ("calculate", column.Calculate.Length > 0 ? column.Calculate : null)),
                id)),
            .. view.Sorts.Select(sort => (ModelChange)new ModelChange.Add("Sort", null, Attributes(("property", sort.PropertyId), ("direction", sort.Descending ? "descending" : null)), id)),
        ];
        KnowledgeFilterEdits.CopyFilter(table, view.Filter, id, $"{id}/filter", changes);
        changes.AddRange(view.GroupOrder.Select(key => (ModelChange)new ModelChange.Add("GroupSetting", null, Attributes(("key", key)), id, Slot: "groupOrder")));
        changes.AddRange(view.HiddenGroups.Select(key => (ModelChange)new ModelChange.Add("GroupSetting", null, Attributes(("key", key)), id, Slot: "hiddenGroups")));
        changes.AddRange(view.Collapsed.Select(key => (ModelChange)new ModelChange.Add("GroupSetting", null, Attributes(("key", key)), id, Slot: "collapsed")));
        return new KnowledgeEdit(changes, NewViewId: id);
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

    // ---- a view's columns ----

    internal static KnowledgeEdit MoveColumn(KnowledgeTable table, KnowledgeView view, TableGesture gesture)
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

    internal static KnowledgeEdit ResizeColumn(KnowledgeTable table, KnowledgeView view, TableGesture gesture) =>
        int.TryParse(gesture.Settings.GetValueOrDefault("width"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) && width > 0
            ? ColumnSetting(table, view, gesture.ColumnId, "width", (long)Math.Max(MinimumWidth, width))
            : Refused("A width is a number of pixels.");

    internal static KnowledgeEdit ShowColumn(KnowledgeTable table, KnowledgeView view, TableGesture gesture, bool visible)
    {
        if (!visible && table.Properties.FirstOrDefault(property => property.Id == gesture.ColumnId) is { IsTitle: true })
        {
            return Refused(TitleStays);
        }

        // Shown is what a column is unless the view says otherwise, so only hidden is written.
        return ColumnSetting(table, view, gesture.ColumnId, "visible", visible ? null : false);
    }

    internal static KnowledgeEdit WrapColumn(KnowledgeTable table, KnowledgeView view, TableGesture gesture) =>
        ColumnSetting(table, view, gesture.ColumnId, "wrap", Flag(gesture, "wrap") ? true : null);

    /// <summary>What the line under a column shows: one of the calculations its property's type has, or none.</summary>
    internal static KnowledgeEdit Calculate(KnowledgeTable table, KnowledgeView view, TableGesture gesture)
    {
        var calculation = gesture.Settings.GetValueOrDefault("calculate") ?? "";
        if (table.Properties.FirstOrDefault(candidate => candidate.Id == gesture.ColumnId) is not { } property)
        {
            return Refused(PropertyGone);
        }

        if (calculation.Length > 0 && !KnowledgeCalculations.For(property.Type).Contains(calculation))
        {
            return Refused(KnowledgeCalculations.IsKnown(calculation) ? $"'{property.Name}' is not of a type that calculation is for." : "That is not a calculation.");
        }

        var now = view.Columns.FirstOrDefault(column => column.PropertyId == property.Id)?.Calculate ?? "";
        return now == calculation ? KnowledgeEdit.Nothing : ColumnSetting(table, view, property.Id, "calculate", calculation.Length > 0 ? calculation : null);
    }

    /// <summary>The columns up to and including one stay in sight when the table scrolls sideways; with no column named, none does.</summary>
    internal static KnowledgeEdit Freeze(KnowledgeTable table, KnowledgeView view, TableGesture gesture)
    {
        if (gesture.ColumnId.Length > 0 && table.Properties.All(property => property.Id != gesture.ColumnId))
        {
            return Refused(PropertyGone);
        }

        return gesture.ColumnId == view.Freeze ? KnowledgeEdit.Nothing : KnowledgeEdit.Of(Set(view.Id, ("freeze", gesture.ColumnId.Length > 0 ? gesture.ColumnId : null)));
    }

    /// <summary>
    /// One setting of one column of one view: set on its entry, which is written only when there is
    /// something to say. A view shows what it lists first and every other property after, in the
    /// file's order; so a column it does not list yet is listed together with the unlisted ones
    /// shown before it, each by its property alone, and stays where it was shown. Listed on its own,
    /// it moved in front of them: a calculation or a width given to a column moved that column.
    /// </summary>
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

        if (value is null)
        {
            return KnowledgeEdit.Nothing;
        }

        var listed = view.Columns.Select(column => column.PropertyId).ToHashSet(StringComparer.Ordinal);
        List<ModelChange> changes =
        [
            .. table.Properties
                .Select(property => property.Id)
                .TakeWhile(id => id != propertyId)
                .Where(id => !listed.Contains(id))
                .Select(id => (ModelChange)new ModelChange.Add("Column", null, Attributes(("property", id)), view.Id)),
            new ModelChange.Add("Column", null, Attributes(("property", propertyId), (name, value)), view.Id),
        ];
        return new KnowledgeEdit(changes);
    }

    private static string ColumnId(KnowledgeView view, string propertyId) => $"{view.Id}/columns/{propertyId}";

    // ---- sorts ----

    internal static KnowledgeEdit AddSort(KnowledgeTable table, KnowledgeView view, TableGesture gesture)
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

    internal static KnowledgeEdit SetSort(KnowledgeView view, TableGesture gesture) =>
        view.Sorts.Any(sort => sort.PropertyId == gesture.ColumnId)
            ? KnowledgeEdit.Of(Set(SortId(view, gesture.ColumnId), ("direction", gesture.Settings.GetValueOrDefault("direction") == "descending" ? "descending" : null)))
            : KnowledgeEdit.Nothing;

    internal static KnowledgeEdit RemoveSort(KnowledgeView view, TableGesture gesture) =>
        view.Sorts.Any(sort => sort.PropertyId == gesture.ColumnId) ? KnowledgeEdit.Of(new ModelChange.Remove(SortId(view, gesture.ColumnId))) : KnowledgeEdit.Nothing;

    internal static KnowledgeEdit MoveSort(KnowledgeView view, TableGesture gesture) =>
        view.Sorts.Any(sort => sort.PropertyId == gesture.ColumnId)
            ? MoveTo(SortId(view, gesture.ColumnId), view.Id, [.. view.Sorts.Select(sort => SortId(view, sort.PropertyId))], gesture.Index)
            : KnowledgeEdit.Nothing;

    private static string SortId(KnowledgeView view, string propertyId) => $"{view.Id}/sorts/{propertyId}";
}
