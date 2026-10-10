using EtAlii.Adp.Designer.TableModel;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// A knowledge file onto the host's table model: what the table library draws. The table model
/// names no designer; this is where the Knowledge designer's own words - a property, a value
/// type, a colour, a target file - become columns, kinds and settings.
/// </summary>
internal static class KnowledgeTableMapper
{
    /// <summary>Everything about the table but its rows, for the view a connection shows.</summary>
    /// <param name="table">The table as shown.</param>
    /// <param name="readOnlyReason">Why it cannot be edited, or empty.</param>
    /// <param name="viewId">The view the connection shows.</param>
    /// <param name="rowCount">The lines of that view.</param>
    /// <param name="targetOf">What a relation property points at, or null where relations are not resolved.</param>
    public static TableStructureChanged Structure(KnowledgeTable table, string readOnlyReason, string viewId, int rowCount, Func<KnowledgeProperty, KnowledgeTarget?>? targetOf = null)
    {
        var view = table.ViewOrDefault(viewId);
        return new TableStructureChanged(
            table.Name,
            Columns(table, view, targetOf, Uses(table)),
            [.. table.Views.Select(candidate => new TableView(candidate.Id, candidate.Name))],
            Settings(view),
            rowCount,
            readOnlyReason);
    }

    /// <summary>
    /// The columns in the view's order: the properties the view lists, as it lists them, then
    /// those it does not mention, which are shown - a property added outside ADP appears.
    /// </summary>
    /// <summary>The setting a column says under how many rows have one of its options: what deleting that option would clear.</summary>
    private const string UsesPrefix = "uses:";

    /// <summary>How many rows have each option of each selection, counted once for the whole table.</summary>
    private static Dictionary<(string PropertyId, string OptionId), int> Uses(KnowledgeTable table)
    {
        var uses = new Dictionary<(string, string), int>();
        foreach (var cell in table.Rows.SelectMany(row => row.Cells).Where(cell => cell.Key is "option" or "options"))
        {
            foreach (var value in cell.Values.Distinct())
            {
                uses[(cell.PropertyId, value)] = uses.GetValueOrDefault((cell.PropertyId, value)) + 1;
            }
        }

        return uses;
    }

    private static IReadOnlyList<TableColumn> Columns(KnowledgeTable table, KnowledgeView? view, Func<KnowledgeProperty, KnowledgeTarget?>? targetOf, Dictionary<(string PropertyId, string OptionId), int> uses)
    {
        var listed = view?.Columns ?? [];
        var byId = table.Properties.ToDictionary(property => property.Id);
        var columns = new List<TableColumn>(table.Properties.Count);
        foreach (var column in listed)
        {
            // A view naming a property that is gone still opens, without that setting.
            if (byId.Remove(column.PropertyId, out var property))
            {
                columns.Add(Column(property, column, targetOf, uses));
            }
        }

        columns.AddRange(table.Properties.Where(property => byId.ContainsKey(property.Id)).Select(property => Column(property, null, targetOf, uses)));
        return columns;
    }

    private static TableViewSettings Settings(KnowledgeView? view) => view is null
        ? new TableViewSettings("")
        : new TableViewSettings(
            view.Id,
            [.. view.Sorts.Select(sort => new TableSort(sort.PropertyId, sort.Descending))],
            view.Filter.Items.Count == 0 ? null : Filter(view.Filter),
            view.GroupBy,
            view.HideEmptyGroups);

    /// <summary>
    /// One row as a line of the view, holding the cells that have a value. A cell among
    /// <paramref name="unwritten"/> shows an edit that is not in the file yet, and says so.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="unwritten">The cells whose value is an edit not yet written.</param>
    /// <param name="labelOf">What a related row is called, by the relation's property and the row's id; empty for one that is not found.</param>
    public static TableRow Row(KnowledgeRow row, IReadOnlySet<(string RowId, string ColumnId)> unwritten, Func<string, string, string>? labelOf = null) => new(
        row.Id,
        Cells:
        [
            .. row.Cells.Where(cell => cell.Values.Count > 0).Select(cell => new TableCell(
                cell.PropertyId,
                cell.Values,
                // A related row is stored by its id and shown by its title.
                cell.Key == "rows" && labelOf is not null ? [.. cell.Values.Select(value => labelOf(cell.PropertyId, value))] : null,
                unwritten.Contains((row.Id, cell.PropertyId)))),
        ]);

    /// <summary>What the body's reading reported, as the table's findings.</summary>
    public static IReadOnlyList<TableFinding> Findings(FblModel model) =>
    [
        .. model.Findings.Select(finding => new TableFinding(
            // A key the bindings do not read is the designer's own finding: kept, and said.
            finding.Code == FindingCodes.UnboundKey ? KnowledgeValidator.UnknownKey : finding.Code,
            finding.Severity switch
            {
                FindingSeverity.Error => TableFindingSeverity.Error,
                FindingSeverity.Warning => TableFindingSeverity.Warning,
                _ => TableFindingSeverity.Info,
            },
            finding.Location is { } at ? $"{finding.Message} (line {at.Line})" : finding.Message)),
    ];

    private static TableColumn Column(KnowledgeProperty property, KnowledgeColumn? column, Func<KnowledgeProperty, KnowledgeTarget?>? targetOf, Dictionary<(string PropertyId, string OptionId), int> uses)
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        if (property.TargetFile.Length > 0)
        {
            settings["targetFile"] = property.TargetFile;
        }

        IReadOnlyList<TableOption> options = [.. property.Options.Select(option => new TableOption(option.Id, option.Name, option.Colour))];

        // An option some rows have says how many: its author is told before deleting it clears them.
        foreach (var option in property.Options)
        {
            if (uses.GetValueOrDefault((property.Id, option.Id)) is > 0 and var count)
            {
                settings[UsesPrefix + option.Id] = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        if (property.ValueType == "relation")
        {
            settings["limit"] = property.Limit;
            if (property.IsComputed)
            {
                settings["computed"] = "true";
            }

            if (property.IsParent)
            {
                settings["parent"] = "true";
            }

            // What a relation offers to choose from is the rows of its target, by their titles.
            if (targetOf?.Invoke(property)?.Table is { } target)
            {
                options = [.. target.Rows.Select(row => new TableOption(row.Id, KnowledgeRelations.TitleOf(target, row)))];
            }
            else if (targetOf is not null)
            {
                settings["unresolved"] = "true";
            }
        }

        return new TableColumn(
            property.Id,
            property.Name,
            property.ValueType,
            options,
            column?.Width ?? 0,
            // The property that names a row is always shown, whatever a file says.
            property.IsTitle || (column?.Visible ?? true),
            property.IsTitle,
            column?.Wrap ?? false,
            settings);
    }

    private static TableFilterGroup Filter(KnowledgeFilterGroup group) => new(
        group.Any,
        [
            .. group.Items.Select(item => item switch
            {
                KnowledgeFilterGroup nested => (TableFilterItem)Filter(nested),
                // The two comparisons every type has are the table library's own, under its names for them.
                KnowledgeCondition condition => new TableCondition(
                    condition.PropertyId,
                    condition.Operator switch
                    {
                        "is-empty" => "isEmpty",
                        "is-not-empty" => "isNotEmpty",
                        _ => condition.Operator,
                    },
                    condition.Value.Length > 0 ? [condition.Value] : []),
                _ => throw new InvalidOperationException($"A filter item of type {item.GetType().Name} has no place in a table's filter."),
            }),
        ]);
}
