namespace EtAlii.Adp.Designer.TableModel;

/// <summary>
/// A table as a connection first sees it: its columns, its views, the active view's settings
/// and how many lines that view has - everything but the rows, which follow for the window the
/// client has in sight.
/// </summary>
/// <remarks>
/// The table model names no designer type. It is what crosses the wire and what the client's
/// table library draws; a designer module maps its own document onto it, as a diagram module
/// maps onto the canvas library's diagram model (knowledge-designer design, <i>The host</i>).
/// </remarks>
/// <param name="Title">The table's name.</param>
/// <param name="Columns">Every column, in the active view's order.</param>
/// <param name="Views">Every view, in their order.</param>
/// <param name="Settings">The active view's settings.</param>
/// <param name="RowCount">The lines of the active view: rows and group headings.</param>
/// <param name="Findings">What the document's reading reported.</param>
/// <param name="ReadOnlyReason">Why the table cannot be edited, or empty when it can.</param>
public sealed record TableBaseline(
    string Title,
    IReadOnlyList<TableColumn> Columns,
    IReadOnlyList<TableView> Views,
    TableViewSettings Settings,
    int RowCount,
    IReadOnlyList<TableFinding> Findings,
    string ReadOnlyReason = "");

/// <summary>One column of a table.</summary>
/// <param name="Id">The column's id, stable under renaming and reordering.</param>
/// <param name="Name">The name its header shows.</param>
/// <param name="Kind">The kind of value its cells hold, in the designer's own vocabulary.</param>
/// <param name="Options">The options of a selection, in their order.</param>
/// <param name="Width">Its width in pixels in the active view, or 0 for the table's default.</param>
/// <param name="Visible">Whether the active view shows it.</param>
/// <param name="IsTitle">Whether it is the column that names a row.</param>
/// <param name="Wraps">Whether its cells wrap in the active view.</param>
/// <param name="Settings">What else its kind has, by name.</param>
public sealed record TableColumn(
    string Id,
    string Name,
    string Kind,
    IReadOnlyList<TableOption>? Options = null,
    int Width = 0,
    bool Visible = true,
    bool IsTitle = false,
    bool Wraps = false,
    IReadOnlyDictionary<string, string>? Settings = null)
{
    public IReadOnlyList<TableOption> Options { get; } = Options ?? [];

    public IReadOnlyDictionary<string, string> Settings { get; } = Settings ?? new Dictionary<string, string>();
}

/// <summary>One option of a selection.</summary>
/// <param name="Id">The option's id.</param>
/// <param name="Name">The name shown.</param>
/// <param name="Color">A colour name of the theme, never a colour value.</param>
public sealed record TableOption(string Id, string Name, string Color = "");

/// <summary>One view of a table, as its tab shows it.</summary>
public sealed record TableView(string Id, string Name);

/// <summary>What a view does to the table it shows.</summary>
/// <param name="ViewId">The view these are the settings of.</param>
/// <param name="Sorts">The sorts, the first deciding first.</param>
/// <param name="Filter">The filter, or null when the view filters nothing.</param>
/// <param name="GroupBy">The column the rows are grouped by, or empty.</param>
/// <param name="HidesEmptyGroups">Whether a group without rows is left out.</param>
/// <param name="Settings">What else the view stores, by name.</param>
public sealed record TableViewSettings(
    string ViewId,
    IReadOnlyList<TableSort>? Sorts = null,
    TableFilterGroup? Filter = null,
    string GroupBy = "",
    bool HidesEmptyGroups = false,
    IReadOnlyDictionary<string, string>? Settings = null)
{
    public IReadOnlyList<TableSort> Sorts { get; } = Sorts ?? [];

    public IReadOnlyDictionary<string, string> Settings { get; } = Settings ?? new Dictionary<string, string>();
}

/// <summary>A sort by one column.</summary>
public sealed record TableSort(string ColumnId, bool Descending = false);

/// <summary>A filter's item: a condition on a column, or a group of items.</summary>
public abstract record TableFilterItem;

/// <summary>Items a row passes when all of them hold, or when any does.</summary>
/// <param name="Any">True when one holding item is enough.</param>
/// <param name="Items">The conditions and groups, in their order.</param>
public sealed record TableFilterGroup(bool Any, IReadOnlyList<TableFilterItem> Items) : TableFilterItem;

/// <summary>A comparison of a column's value.</summary>
/// <param name="ColumnId">The column compared.</param>
/// <param name="Comparison">The comparison, in the designer's own vocabulary.</param>
/// <param name="Values">What it is compared with; none for a comparison that needs none.</param>
public sealed record TableCondition(string ColumnId, string Comparison, IReadOnlyList<string>? Values = null) : TableFilterItem
{
    public IReadOnlyList<string> Values { get; } = Values ?? [];
}

/// <summary>Something the document's reading or the designer's rules reported.</summary>
/// <param name="Code">The finding's code.</param>
/// <param name="Severity">How serious it is.</param>
/// <param name="Message">The sentence shown.</param>
/// <param name="RowId">The row of the cell it is about, or empty.</param>
/// <param name="ColumnId">The column of the cell it is about, or empty.</param>
public sealed record TableFinding(string Code, TableFindingSeverity Severity, string Message, string RowId = "", string ColumnId = "");

public enum TableFindingSeverity
{
    Info,
    Warning,
    Error,
}
