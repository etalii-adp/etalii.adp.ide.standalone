using System.Globalization;
using EtAlii.Adp.Designer.TableModel;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// A view's lines: the rows its filter lets through, in the order its sorts give, under the
/// headings of its grouping or nested under their parents - computed here, on the backend, so a
/// client draws a window of them and never ten thousand rows (knowledge-designer design, <i>The
/// backend sorts, filters and groups</i>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A value is compared as what its property's type says it is</b>: a number as a number, a
/// date as a moment, a selection by its option's place among the options. A cell that still holds
/// a value of a type the property had before has no value of this type, and is compared as empty.
/// </para>
/// <para>
/// <b>A condition that is not finished filters nothing.</b> A comparison that needs a value and
/// has none yet lets every row through, so a filter being built does not empty the table.
/// </para>
/// </remarks>
internal static class KnowledgeViewEngine
{
    /// <summary>The key of the group of rows that have no value for the grouping property.</summary>
    public const string NoValue = "none";

    /// <param name="table">The table as it is shown.</param>
    /// <param name="view">The view, or null for a table without one: every row, in the file's order.</param>
    /// <param name="kept">Rows shown whatever the filter says: those added while this view was open.</param>
    /// <param name="unwritten">The cells whose value is an edit not yet written.</param>
    /// <param name="editable">Whether a line to add a row at is given, at the bottom and under every group.</param>
    /// <param name="labelOf">What a related row is called, by the relation's property and the row's id; null where relations are not resolved.</param>
    public static List<TableRow> Lines(KnowledgeTable table, KnowledgeView? view, IReadOnlySet<string> kept, IReadOnlySet<(string RowId, string ColumnId)> unwritten, bool editable, Func<string, string, string>? labelOf = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(kept);
        ArgumentNullException.ThrowIfNull(unwritten);

        var properties = table.Properties.ToDictionary(property => property.Id, StringComparer.Ordinal);
        IEnumerable<KnowledgeRow> rows = table.Rows;
        if (view is not null)
        {
            rows = Sorted([.. rows.Where(row => kept.Contains(row.Id) || Passes(view.Filter, row, properties))], view.Sorts, properties);
        }

        var shown = rows.ToList();

        List<TableRow> lines = [];
        if (view is null || !properties.TryGetValue(view.GroupBy, out var grouped))
        {
            lines.AddRange(shown.Select(row => Line(row)));
        }
        else if (grouped.IsParent)
        {
            Nest(shown, grouped, view, Line, lines);
        }
        else
        {
            Group(shown, grouped, view, Line, lines, editable, labelOf);
            return lines;
        }

        if (editable)
        {
            lines.Add(new TableRow("", IsNewRow: true));
        }

        return lines;

        TableRow Line(KnowledgeRow row, int depth = 0, bool hasChildren = false, bool collapsed = false) =>
            KnowledgeTableMapper.Row(row, unwritten, labelOf) with { Depth = depth, HasChildren = hasChildren, Collapsed = collapsed };
    }

    // ---- the filter ----

    private static bool Passes(KnowledgeFilterGroup group, KnowledgeRow row, Dictionary<string, KnowledgeProperty> properties)
    {
        if (group.Items.Count == 0)
        {
            return true;
        }

        return group.Any ? group.Items.Any(ItemHolds) : group.Items.All(ItemHolds);

        bool ItemHolds(KnowledgeFilterItem item) => item switch
        {
            KnowledgeFilterGroup nested => Passes(nested, row, properties),
            KnowledgeCondition condition => Holds(condition, row, properties),
            _ => true,
        };
    }

    /// <summary>The values a row has for a property, as values of the property's own type: none when the cell holds none, or holds one of another type.</summary>
    private static IReadOnlyList<string> ValuesOf(KnowledgeRow row, KnowledgeProperty property) =>
        row.Cells.FirstOrDefault(candidate => candidate.PropertyId == property.Id) is { } cell && cell.Key == KnowledgeEdits.KeyOf(property.ValueType) ? cell.Values : [];

    private static bool Holds(KnowledgeCondition condition, KnowledgeRow row, Dictionary<string, KnowledgeProperty> properties)
    {
        // A condition naming a property that is gone is a setting the view opens without.
        if (!properties.TryGetValue(condition.PropertyId, out var property))
        {
            return true;
        }

        // A comparison its property's type does not have - written by hand, or left from a type the
        // property had before - is a setting the view opens without, too.
        if (!KnowledgeVocabulary.Compares(property.ValueType, condition.Operator))
        {
            return true;
        }

        var values = ValuesOf(row, property);
        var value = values.Count > 0 ? values[0] : "";
        var given = condition.Value;
        switch (condition.Operator)
        {
            case "is-empty":
                return values.Count == 0;
            case "is-not-empty":
                return values.Count > 0;
            case "is-checked":
                return value == "true";
            case "is-not-checked":
                return value != "true";
        }

        // Every other comparison is with a value. Without one the condition is not finished, and filters nothing.
        if (given.Length == 0)
        {
            return true;
        }

        switch (property.ValueType)
        {
            case "number":
                if (!double.TryParse(given, NumberStyles.Float, CultureInfo.InvariantCulture, out var wanted))
                {
                    return true;
                }

                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    // A row without a number is not equal to one, and is neither more nor less.
                    return condition.Operator == "does-not-equal";
                }

                return condition.Operator switch
                {
                    "equals" => number.Equals(wanted),
                    "does-not-equal" => !number.Equals(wanted),
                    "greater-than" => number > wanted,
                    "less-than" => number < wanted,
                    "at-least" => number >= wanted,
                    "at-most" => number <= wanted,
                    _ => true,
                };
            case "date" or "dateTime" or "time":
                if (Moment(property.ValueType, given) is not { } asked)
                {
                    return true;
                }

                if (Moment(property.ValueType, value) is not { } moment)
                {
                    return false;
                }

                return condition.Operator switch
                {
                    "is" => moment == asked,
                    "is-before" => moment < asked,
                    "is-after" => moment > asked,
                    "is-on-or-before" => moment <= asked,
                    "is-on-or-after" => moment >= asked,
                    _ => true,
                };
            case "multipleSelection" or "relation":
                return condition.Operator switch
                {
                    "contains" => values.Contains(given),
                    "does-not-contain" => !values.Contains(given),
                    _ => true,
                };
            case "selection":
                return condition.Operator switch
                {
                    "is" => value == given,
                    "is-not" => value != given,
                    _ => true,
                };
            default:
                return condition.Operator switch
                {
                    "is" => string.Equals(value, given, StringComparison.OrdinalIgnoreCase),
                    "is-not" => !string.Equals(value, given, StringComparison.OrdinalIgnoreCase),
                    "contains" => value.Contains(given, StringComparison.OrdinalIgnoreCase),
                    "does-not-contain" => !value.Contains(given, StringComparison.OrdinalIgnoreCase),
                    "starts-with" => value.StartsWith(given, StringComparison.OrdinalIgnoreCase),
                    "ends-with" => value.EndsWith(given, StringComparison.OrdinalIgnoreCase),
                    _ => true,
                };
        }
    }

    /// <summary>
    /// A date, a date and time or a time as a point to compare: ticks of the day, of the instant in
    /// UTC, or of the time of day. Null for what is not one.
    /// </summary>
    private static long? Moment(string valueType, string value) => valueType switch
    {
        "date" when DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) => date.DayNumber,
        "dateTime" when DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var instant) => instant.UtcTicks,
        "time" when TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) => time.Ticks,
        _ => null,
    };

    // ---- the sorts ----

    /// <summary>
    /// The rows by the sorts in turn, the first deciding first, each in the way its property's type
    /// calls for. Stable: rows the sorts do not tell apart keep the file's order. A row without a
    /// value comes after those with one, whichever way the sort runs.
    /// </summary>
    private static IEnumerable<KnowledgeRow> Sorted(IReadOnlyList<KnowledgeRow> rows, IReadOnlyList<KnowledgeSort> sorts, Dictionary<string, KnowledgeProperty> properties)
    {
        IOrderedEnumerable<KnowledgeRow>? ordered = null;
        foreach (var sort in sorts)
        {
            if (!properties.TryGetValue(sort.PropertyId, out var property))
            {
                continue;
            }

            var comparer = Comparer<SortKey>.Create((left, right) => SortKey.Compare(left, right, sort.Descending));
            ordered = ordered is null
                ? rows.OrderBy(row => KeyOf(row, property), comparer)
                : ordered.ThenBy(row => KeyOf(row, property), comparer);
        }

        return ordered is null ? rows : ordered;
    }

    private static SortKey KeyOf(KnowledgeRow row, KnowledgeProperty property)
    {
        var values = ValuesOf(row, property);
        if (values.Count == 0)
        {
            return SortKey.Empty;
        }

        var value = values[0];
        switch (property.ValueType)
        {
            case "number":
                return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? new SortKey(number, "") : SortKey.Empty;
            case "checkbox":
                // Unticked first.
                return new SortKey(value == "true" ? 1 : 0, "");
            case "date" or "dateTime" or "time":
                return Moment(property.ValueType, value) is { } moment ? new SortKey(moment, "") : SortKey.Empty;
            case "selection" or "multipleSelection":
                // By the option's place among the options, not by its name.
                var place = -1;
                for (var index = 0; index < property.Options.Count; index++)
                {
                    if (property.Options[index].Id == value)
                    {
                        place = index;
                        break;
                    }
                }

                return place < 0 ? SortKey.Empty : new SortKey(place, "");
            default:
                return new SortKey(0, value);
        }
    }

    /// <summary>What a row is sorted by for one property: a number, or a text, or nothing.</summary>
    private readonly record struct SortKey(double Number, string Text, bool HasValue = true)
    {
        public static SortKey Empty { get; } = new(0, "", HasValue: false);

        public static int Compare(SortKey left, SortKey right, bool descending)
        {
            if (!left.HasValue || !right.HasValue)
            {
                // Without a value is last, in either direction.
                return left.HasValue == right.HasValue ? 0 : left.HasValue ? -1 : 1;
            }

            var order = left.Number.CompareTo(right.Number);
            if (order == 0)
            {
                order = string.Compare(left.Text, right.Text, StringComparison.OrdinalIgnoreCase);
            }

            return descending ? -order : order;
        }
    }

    // ---- groups ----

    private static void Group(List<KnowledgeRow> rows, KnowledgeProperty grouped, KnowledgeView view, Func<KnowledgeRow, int, bool, bool, TableRow> line, List<TableRow> lines, bool editable, Func<string, string, string>? labelOf)
    {
        // The groups there are, in their natural order: an option each, in the options' order; unticked
        // and ticked; a related row each, as they are first met. The rows without a value come last.
        List<(string Key, string Label)> groups = grouped.ValueType switch
        {
            "selection" or "multipleSelection" => [.. grouped.Options.Select(option => (option.Id, option.Name))],
            "checkbox" => [("false", "Not checked"), ("true", "Checked")],
            // A related row is a group under its title; one that is not found, under the id it is named by.
            _ => [.. rows.SelectMany(row => ValuesOf(row, grouped)).Distinct().Select(id => (id, labelOf?.Invoke(grouped.Id, id) is { Length: > 0 } title ? title : id))],
        };

        // A checkbox has no row without a value: unticked is one. Every other grouping has a group for them.
        List<KnowledgeRow> without = [];
        var members = groups.ToDictionary(group => group.Key, _ => new List<KnowledgeRow>(), StringComparer.Ordinal);
        if (grouped.ValueType != "checkbox")
        {
            groups.Add((NoValue, $"No {grouped.Name}"));
            members[NoValue] = without;
        }
        foreach (var row in rows)
        {
            var values = ValuesOf(row, grouped);
            if (grouped.ValueType == "checkbox")
            {
                members[values.FirstOrDefault() == "true" ? "true" : "false"].Add(row);
                continue;
            }

            // A row with several values is under each of them. One naming a group that is not there has no group.
            var placed = false;
            foreach (var value in values.Distinct())
            {
                if (value != NoValue && members.TryGetValue(value, out var group))
                {
                    group.Add(row);
                    placed = true;
                }
            }

            if (!placed)
            {
                without.Add(row);
            }
        }

        // The order the view gives its groups first, then every other group in its natural order.
        var order = view.GroupOrder.Where(members.ContainsKey).ToList();
        order.AddRange(groups.Select(group => group.Key).Where(key => !order.Contains(key)));
        var labels = groups.ToDictionary(group => group.Key, group => group.Label, StringComparer.Ordinal);

        foreach (var key in order)
        {
            Emit(key, labels[key], members[key]);
        }

        return;

        void Emit(string key, string label, List<KnowledgeRow> under)
        {
            if (view.HiddenGroups.Contains(key) || (view.HideEmptyGroups && under.Count == 0))
            {
                return;
            }

            var collapsed = view.Collapsed.Contains(key);
            lines.Add(new TableRow(key, IsGroup: true, Label: label, Count: under.Count, Collapsed: collapsed));
            if (collapsed)
            {
                return;
            }

            lines.AddRange(under.Select(row => line(row, 0, false, false)));
            if (editable)
            {
                lines.Add(new TableRow(key, IsNewRow: true));
            }
        }
    }

    // ---- nesting ----

    /// <summary>
    /// The rows nested under their parents, to any depth. A row whose parent is not among the rows
    /// shown - filtered away, or not in the table - stands at the top; so does one that is its own
    /// ancestor, which a file made outside ADP can hold, so that no row is lost and none is shown twice.
    /// </summary>
    private static void Nest(List<KnowledgeRow> rows, KnowledgeProperty parent, KnowledgeView view, Func<KnowledgeRow, int, bool, bool, TableRow> line, List<TableRow> lines)
    {
        var shown = rows.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
        var children = new Dictionary<string, List<KnowledgeRow>>(StringComparer.Ordinal);
        List<KnowledgeRow> top = [];
        foreach (var row in rows)
        {
            var under = ValuesOf(row, parent).FirstOrDefault() ?? "";
            if (under.Length > 0 && under != row.Id && shown.Contains(under))
            {
                if (!children.TryGetValue(under, out var list))
                {
                    list = [];
                    children[under] = list;
                }

                list.Add(row);
            }
            else
            {
                top.Add(row);
            }
        }

        var emitted = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in top)
        {
            Emit(row, 0);
        }

        // Rows that name each other as parent, round in a circle, are under nobody that was reached.
        foreach (var row in rows.Where(row => !emitted.Contains(row.Id)))
        {
            Emit(row, 0);
        }

        return;

        void Emit(KnowledgeRow row, int depth)
        {
            if (!emitted.Add(row.Id))
            {
                return;
            }

            var has = children.TryGetValue(row.Id, out var under) && under.Count > 0;
            var collapsed = has && view.Collapsed.Contains(row.Id);
            lines.Add(line(row, depth, has, collapsed));
            if (has && !collapsed)
            {
                foreach (var child in under!)
                {
                    Emit(child, depth + 1);
                }
            }
            else if (has)
            {
                // Folded away, and not to be taken for rows that were never reached.
                Mark(under!);
            }
        }

        void Mark(List<KnowledgeRow> under)
        {
            foreach (var child in under)
            {
                if (emitted.Add(child.Id) && children.TryGetValue(child.Id, out var deeper))
                {
                    Mark(deeper);
                }
            }
        }
    }
}
