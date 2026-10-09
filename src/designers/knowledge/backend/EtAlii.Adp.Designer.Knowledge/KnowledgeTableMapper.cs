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
    public static TableStructureChanged Structure(KnowledgeTable table, string readOnlyReason, string viewId, int rowCount)
    {
        var view = table.ViewOrDefault(viewId);
        return new TableStructureChanged(
            table.Name,
            Columns(table, view),
            [.. table.Views.Select(candidate => new TableView(candidate.Id, candidate.Name))],
            Settings(view),
            rowCount,
            readOnlyReason);
    }

    /// <summary>
    /// The columns in the view's order: the properties the view lists, as it lists them, then
    /// those it does not mention, which are shown - a property added outside ADP appears.
    /// </summary>
    private static IReadOnlyList<TableColumn> Columns(KnowledgeTable table, KnowledgeView? view)
    {
        var listed = view?.Columns ?? [];
        var byId = table.Properties.ToDictionary(property => property.Id);
        var columns = new List<TableColumn>(table.Properties.Count);
        foreach (var column in listed)
        {
            // A view naming a property that is gone still opens, without that setting.
            if (byId.Remove(column.PropertyId, out var property))
            {
                columns.Add(Column(property, column));
            }
        }

        columns.AddRange(table.Properties.Where(property => byId.ContainsKey(property.Id)).Select(property => Column(property, null)));
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
    public static TableRow Row(KnowledgeRow row, IReadOnlySet<(string RowId, string ColumnId)> unwritten) => new(
        row.Id,
        Cells: [.. row.Cells.Where(cell => cell.Values.Count > 0).Select(cell => new TableCell(cell.PropertyId, cell.Values, Pending: unwritten.Contains((row.Id, cell.PropertyId))))]);

    /// <summary>What the body's reading reported, as the table's findings.</summary>
    public static IReadOnlyList<TableFinding> Findings(FblModel model) =>
    [
        .. model.Findings.Select(finding => new TableFinding(
            finding.Code,
            finding.Severity switch
            {
                FindingSeverity.Error => TableFindingSeverity.Error,
                FindingSeverity.Warning => TableFindingSeverity.Warning,
                _ => TableFindingSeverity.Info,
            },
            finding.Location is { } at ? $"{finding.Message} (line {at.Line})" : finding.Message)),
    ];

    private static TableColumn Column(KnowledgeProperty property, KnowledgeColumn? column)
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        if (property.TargetFile.Length > 0)
        {
            settings["targetFile"] = property.TargetFile;
        }

        if (property.ValueType == "relation")
        {
            settings["limit"] = property.Limit;
        }

        return new TableColumn(
            property.Id,
            property.Name,
            property.ValueType,
            [.. property.Options.Select(option => new TableOption(option.Id, option.Name, option.Colour))],
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
