using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// A knowledge file as the designer understands it: its properties, its views with every setting
/// they have, and its rows - read from the body's FBL model and from nowhere else. Nothing here
/// is kept outside the file (knowledge-designer Requirement 2.3).
/// </summary>
/// <param name="Name">The table's name.</param>
/// <param name="ActiveViewId">The view the table was last left in, or empty for the first.</param>
/// <param name="Properties">The properties, in the file's order.</param>
/// <param name="Views">The views, in the file's order.</param>
/// <param name="Rows">The rows, in the file's order - their order where no sort applies.</param>
internal sealed record KnowledgeTable(
    string Name,
    string ActiveViewId,
    IReadOnlyList<KnowledgeProperty> Properties,
    IReadOnlyList<KnowledgeView> Views,
    IReadOnlyList<KnowledgeRow> Rows)
{
    public static KnowledgeTable Empty { get; } = new("", "", [], [], []);

    /// <summary>The view a connection shows when it names none: the one the file was left in, else the first.</summary>
    public KnowledgeView? ViewOrDefault(string viewId) =>
        Views.FirstOrDefault(view => view.Id == viewId)
        ?? Views.FirstOrDefault(view => view.Id == ActiveViewId)
        ?? Views.FirstOrDefault();
}

/// <summary>One property: a column of the table, with its type and what its type has.</summary>
internal sealed record KnowledgeProperty(
    string Id,
    string Name,
    string ValueType,
    bool IsTitle,
    IReadOnlyList<KnowledgeOption> Options,
    string TargetFile,
    string Limit,
    string Counterpart,
    bool IsComputed,
    bool IsParent);

/// <summary>One option of a selection.</summary>
internal sealed record KnowledgeOption(string Id, string Name, string Colour);

/// <summary>One view, with everything it stores.</summary>
internal sealed record KnowledgeView(
    string Id,
    string Name,
    IReadOnlyList<KnowledgeColumn> Columns,
    IReadOnlyList<KnowledgeSort> Sorts,
    KnowledgeFilterGroup Filter,
    string GroupBy,
    bool HideEmptyGroups,
    IReadOnlyList<string> GroupOrder,
    IReadOnlyList<string> HiddenGroups,
    IReadOnlyList<string> Collapsed);

/// <summary>What a view says about one property: whether it shows, how wide, whether it wraps.</summary>
internal sealed record KnowledgeColumn(string PropertyId, bool Visible, int Width, bool Wrap);

internal sealed record KnowledgeSort(string PropertyId, bool Descending);

/// <summary>A filter's item: a condition, or a group of items.</summary>
internal abstract record KnowledgeFilterItem;

/// <summary>Items joined by <i>all</i> or by <i>any</i>. A view's own filter is the outermost group.</summary>
internal sealed record KnowledgeFilterGroup(bool Any, IReadOnlyList<KnowledgeFilterItem> Items) : KnowledgeFilterItem;

/// <summary>One comparison of a property's value. <see cref="Value"/> is in its written form, empty for a comparison that takes none.</summary>
internal sealed record KnowledgeCondition(string PropertyId, string Operator, string Value) : KnowledgeFilterItem;

/// <summary>One row and the cells it holds. A property without a cell is empty in this row.</summary>
internal sealed record KnowledgeRow(string Id, IReadOnlyList<KnowledgeCell> Cells);

/// <summary>A row's value for one property, in written form: one value, or several for a kind that holds several.</summary>
/// <param name="PropertyId">The property it is the value of.</param>
/// <param name="Values">The value or values.</param>
/// <param name="Key">
/// The key the file holds the value under: <c>text</c>, <c>number</c>, <c>checked</c>, <c>date</c>,
/// <c>dateTime</c>, <c>time</c> or <c>option</c> for one value, <c>options</c> or <c>rows</c> for
/// several, and empty for a cell without a value. It is the key of the type the value was written
/// as, which after a change of type need not be the property's.
/// </param>
internal sealed record KnowledgeCell(string PropertyId, IReadOnlyList<string> Values, string Key = "");

/// <summary>Reads a <see cref="KnowledgeTable"/> out of a body's FBL model.</summary>
internal static class KnowledgeModelReader
{
    public static KnowledgeTable Read(FblModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        // One pass to group every element under its parent and slot, so reading stays linear in
        // the size of the file: ten thousand rows are ten thousand lookups, not ten thousand scans.
        var children = model.Elements
            .Where(element => element.ParentId is not null)
            .GroupBy(element => (element.ParentId!, element.ParentSlot ?? ""))
            .ToDictionary(group => group.Key, group => (IReadOnlyList<FblElement>)group.ToList());

        var table = model.Elements.FirstOrDefault(element => element.Type == "Table");
        var properties = model.Elements.Where(element => element.Type == "Property").Select(property => new KnowledgeProperty(
            property.Id,
            Text(property, "name"),
            Text(property, "valueType"),
            KnowledgeValues.IsTrue(property.Attributes.GetValueOrDefault("title")),
            // An option that names no colour has the default one, and a relation that names no limit
            // has none: the bindings' defaults, restated for a model that was not read through them.
            [.. ChildrenOf(property, "options").Select(option => new KnowledgeOption(option.Id, Text(option, "name"), Text(option, "colour") is { Length: > 0 } colour ? colour : "default"))],
            Text(property, "targetFile"),
            Text(property, "limit") is { Length: > 0 } limit ? limit : "none",
            Text(property, "counterpart"),
            KnowledgeValues.IsTrue(property.Attributes.GetValueOrDefault("computed")),
            KnowledgeValues.IsTrue(property.Attributes.GetValueOrDefault("isParent")))).ToList();

        var views = model.Elements.Where(element => element.Type == "View").Select(view => new KnowledgeView(
            view.Id,
            Text(view, "name"),
            [.. ChildrenOf(view, "columns").Select(column => new KnowledgeColumn(
                Text(column, "property"),
                // A column that does not say is shown: the binding's default, restated for a family that gives none.
                !column.Attributes.TryGetValue("visible", out var visible) || visible is null || KnowledgeValues.IsTrue(visible),
                KnowledgeValues.Whole(column.Attributes.GetValueOrDefault("width")) ?? 0,
                KnowledgeValues.IsTrue(column.Attributes.GetValueOrDefault("wrap"))))],
            [.. ChildrenOf(view, "sorts").Select(sort => new KnowledgeSort(Text(sort, "property"), Text(sort, "direction") == "descending"))],
            new KnowledgeFilterGroup(Text(view, "filterMatch") == "any", [.. ChildrenOf(view, "filter").Select(FilterItemOf)]),
            Text(view, "groupBy"),
            KnowledgeValues.IsTrue(view.Attributes.GetValueOrDefault("hideEmptyGroups")),
            [.. ChildrenOf(view, "groupOrder").Select(setting => Text(setting, "key"))],
            [.. ChildrenOf(view, "hiddenGroups").Select(setting => Text(setting, "key"))],
            [.. ChildrenOf(view, "collapsed").Select(setting => Text(setting, "key"))])).ToList();

        var rows = model.Elements.Where(element => element.Type == "Row").Select(row => new KnowledgeRow(
            row.Id,
            [.. ChildrenOf(row, "cells").Select(cell =>
            {
                var items = ChildrenOf(cell, "items");
                // Several values are several items: an option each, or a related row each.
                var several = items.Select(item => item.Attributes.ContainsKey("option") ? Text(item, "option") : Text(item, "row")).Where(value => value.Length > 0).ToList();
                if (items.Count > 0)
                {
                    return new KnowledgeCell(Text(cell, "property"), several, items[0].Attributes.ContainsKey("option") ? "options" : "rows");
                }

                var one = ValueOf(cell, out var key);
                return one.Length > 0 ? new KnowledgeCell(Text(cell, "property"), [one], key) : new KnowledgeCell(Text(cell, "property"), []);
            })])).ToList();

        return new KnowledgeTable(table is null ? "" : Text(table, "name"), table is null ? "" : Text(table, "activeView"), properties, views, rows);

        KnowledgeFilterItem FilterItemOf(FblElement element) => element.Type == "FilterGroup"
            ? new KnowledgeFilterGroup(Text(element, "match") == "any", [.. ChildrenOf(element, "conditions").Select(FilterItemOf)])
            : new KnowledgeCondition(Text(element, "property"), Text(element, "operator"), ValueOf(element, out _));

        IReadOnlyList<FblElement> ChildrenOf(FblElement parent, string slot) => children.GetValueOrDefault((parent.Id, slot)) ?? [];
    }

    private static string Text(FblElement element, string attribute) => KnowledgeValues.Text(element.Attributes.GetValueOrDefault(attribute));

    /// <summary>The one value a cell or a condition holds, whichever key its kind writes it under.</summary>
    private static string ValueOf(FblElement element, out string key)
    {
        foreach (var candidate in KnowledgeVocabulary.ValueKeys)
        {
            if (element.Attributes.TryGetValue(candidate, out var value) && value is not null)
            {
                key = candidate;
                return KnowledgeValues.Text(value);
            }
        }

        key = "";
        return "";
    }
}
