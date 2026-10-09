using EtAlii.Adp.Designer.TableModel;
using Proto = EtAlii.Adp.Designer.Wire;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// The table model to and from its wire form. One place, so the model a session speaks and the
/// messages a client reads cannot drift apart field by field.
/// </summary>
internal static class TableWire
{
    public static Proto.TableBaseline ToProto(TableBaseline baseline)
    {
        var message = new Proto.TableBaseline
        {
            Title = baseline.Title,
            Settings = ToProto(baseline.Settings),
            RowCount = baseline.RowCount,
            ReadOnlyReason = baseline.ReadOnlyReason,
        };
        message.Columns.AddRange(baseline.Columns.Select(ToProto));
        message.Views.AddRange(baseline.Views.Select(ToProto));
        message.Findings.AddRange(baseline.Findings.Select(ToProto));
        return message;
    }

    public static Proto.TableChange ToProto(TableChange change) => change switch
    {
        TableRowsChanged rows => new Proto.TableChange { Rows = ToProto(rows) },
        TableStructureChanged structure => new Proto.TableChange { Structure = ToProto(structure) },
        TableFindingsChanged findings => new Proto.TableChange { Findings = ToProto(findings) },
        TableEditSettled settled => new Proto.TableChange { Outcome = ToProto(settled) },
        _ => throw new ArgumentOutOfRangeException(nameof(change), change.GetType().Name, "A table change this host cannot put on the wire."),
    };

    public static TableGesture FromProto(Proto.TableGesture gesture) => new(
        gesture.Kind,
        gesture.RowId,
        gesture.ColumnId,
        gesture.ViewId,
        [.. gesture.Values],
        gesture.Index,
        gesture.TargetId,
        new Dictionary<string, string>(gesture.Settings, StringComparer.Ordinal));

    private static Proto.TableRows ToProto(TableRowsChanged rows)
    {
        var message = new Proto.TableRows { First = rows.First, RowCount = rows.RowCount };
        message.Rows.AddRange(rows.Rows.Select(ToProto));
        return message;
    }

    private static Proto.TableRow ToProto(TableRow row)
    {
        var message = new Proto.TableRow
        {
            Id = row.Id,
            Depth = row.Depth,
            IsGroup = row.IsGroup,
            Label = row.Label,
            Count = row.Count,
            Collapsed = row.Collapsed,
            HasChildren = row.HasChildren,
        };
        message.Cells.AddRange(row.Cells.Select(ToProto));
        return message;
    }

    private static Proto.TableCell ToProto(TableCell cell)
    {
        var message = new Proto.TableCell { ColumnId = cell.ColumnId, Pending = cell.Pending };
        message.Values.AddRange(cell.Values);
        message.Labels.AddRange(cell.Labels);
        return message;
    }

    private static Proto.TableStructure ToProto(TableStructureChanged structure)
    {
        var message = new Proto.TableStructure
        {
            Title = structure.Title,
            Settings = ToProto(structure.Settings),
            RowCount = structure.RowCount,
            ReadOnlyReason = structure.ReadOnlyReason,
        };
        message.Columns.AddRange(structure.Columns.Select(ToProto));
        message.Views.AddRange(structure.Views.Select(ToProto));
        return message;
    }

    private static Proto.TableFindings ToProto(TableFindingsChanged findings)
    {
        var message = new Proto.TableFindings();
        message.Findings.AddRange(findings.Findings.Select(ToProto));
        return message;
    }

    private static Proto.TableEditOutcome ToProto(TableEditSettled settled)
    {
        var message = new Proto.TableEditOutcome { EditId = settled.EditId, Written = settled.Written, Error = settled.Error };
        message.TakenBack.AddRange(settled.TakenBack.Select(id => (Documents.Wire.ShortGuid)id));
        return message;
    }

    private static Proto.TableColumn ToProto(TableColumn column)
    {
        var message = new Proto.TableColumn
        {
            Id = column.Id,
            Name = column.Name,
            Kind = column.Kind,
            Width = column.Width,
            Visible = column.Visible,
            IsTitle = column.IsTitle,
            Wraps = column.Wraps,
        };
        message.Options.AddRange(column.Options.Select(option => new Proto.TableOption { Id = option.Id, Name = option.Name, Color = option.Color }));
        message.Settings.Add(column.Settings.ToDictionary());
        return message;
    }

    private static Proto.TableView ToProto(TableView view) => new() { Id = view.Id, Name = view.Name };

    private static Proto.TableViewSettings ToProto(TableViewSettings settings)
    {
        var message = new Proto.TableViewSettings
        {
            ViewId = settings.ViewId,
            GroupBy = settings.GroupBy,
            HidesEmptyGroups = settings.HidesEmptyGroups,
        };
        message.Sorts.AddRange(settings.Sorts.Select(sort => new Proto.TableSort { ColumnId = sort.ColumnId, Descending = sort.Descending }));
        if (settings.Filter is { } filter)
        {
            message.Filter = ToProto(filter);
        }

        message.Settings.Add(settings.Settings.ToDictionary());
        return message;
    }

    private static Proto.TableFilterGroup ToProto(TableFilterGroup group)
    {
        var message = new Proto.TableFilterGroup { Any = group.Any };
        message.Items.AddRange(group.Items.Select(item => item switch
        {
            TableCondition condition => new Proto.TableFilterItem { Condition = ToProto(condition) },
            TableFilterGroup nested => new Proto.TableFilterItem { Group = ToProto(nested) },
            _ => throw new ArgumentOutOfRangeException(nameof(group), item.GetType().Name, "A filter item this host cannot put on the wire."),
        }));
        return message;
    }

    private static Proto.TableCondition ToProto(TableCondition condition)
    {
        var message = new Proto.TableCondition { ColumnId = condition.ColumnId, Comparison = condition.Comparison };
        message.Values.AddRange(condition.Values);
        return message;
    }

    private static Proto.TableFinding ToProto(TableFinding finding) => new()
    {
        Code = finding.Code,
        Severity = finding.Severity switch
        {
            TableFindingSeverity.Error => "error",
            TableFindingSeverity.Warning => "warning",
            _ => "info",
        },
        Message = finding.Message,
        RowId = finding.RowId,
        ColumnId = finding.ColumnId,
    };
}
