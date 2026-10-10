namespace EtAlii.Adp.Designer.TableModel;

/// <summary>
/// One line of a view: a row of the table, the heading of a group of rows, or the place a new
/// row is added at. They share a type because they share an order - the client asks for lines by
/// index and draws what is there.
/// </summary>
/// <param name="Id">A row's id, or a group's key.</param>
/// <param name="Depth">How deep the row is nested under parent rows.</param>
/// <param name="Cells">A row's cells that hold a value; an empty cell is not sent.</param>
/// <param name="IsGroup">Whether this line is a group's heading.</param>
/// <param name="Label">A group heading's text.</param>
/// <param name="Count">A group heading's number of rows.</param>
/// <param name="Collapsed">Whether the group, or the rows under this row, are folded away.</param>
/// <param name="HasChildren">Whether rows are nested under this row.</param>
/// <param name="IsNewRow">
/// Whether this line is where a new row is added: the bottom of the table, or of the group whose
/// key is the line's id.
/// </param>
public sealed record TableRow(
    string Id,
    int Depth = 0,
    IReadOnlyList<TableCell>? Cells = null,
    bool IsGroup = false,
    string Label = "",
    int Count = 0,
    bool Collapsed = false,
    bool HasChildren = false,
    bool IsNewRow = false)
{
    public IReadOnlyList<TableCell> Cells { get; } = Cells ?? [];
}

/// <summary>A row's value for one column.</summary>
/// <param name="ColumnId">The column it is the value of.</param>
/// <param name="Values">The value in its written form; several for a kind that holds several.</param>
/// <param name="Labels">What to show per value where that is not the value itself, e.g. a related row's title.</param>
/// <param name="Pending">Whether the value shown is an edit that has not been written yet.</param>
public sealed record TableCell(string ColumnId, IReadOnlyList<string> Values, IReadOnlyList<string>? Labels = null, bool Pending = false)
{
    public IReadOnlyList<string> Labels { get; } = Labels ?? [];
}

/// <summary>Something that changed in an open table, pushed to the connection showing it.</summary>
public abstract record TableChange;

/// <summary>The lines of the client's window, replacing what it holds there.</summary>
/// <param name="First">The index of the first line given.</param>
/// <param name="Rows">The lines from there.</param>
/// <param name="RowCount">The view's total, which an edit or a filter may have changed.</param>
public sealed record TableRowsChanged(int First, IReadOnlyList<TableRow> Rows, int RowCount) : TableChange;

/// <summary>The columns, the views or the active view's settings changed: all of them, anew.</summary>
public sealed record TableStructureChanged(
    string Title,
    IReadOnlyList<TableColumn> Columns,
    IReadOnlyList<TableView> Views,
    TableViewSettings Settings,
    int RowCount,
    string ReadOnlyReason = "") : TableChange;

/// <summary>The findings changed: all of them, replacing what the client holds.</summary>
public sealed record TableFindingsChanged(IReadOnlyList<TableFinding> Findings) : TableChange;

/// <summary>
/// What became of an edit that was accepted: it was written, or it was refused - and then every
/// later edit that waited on it is taken back with it (knowledge-designer design, <i>An edit is
/// shown at once and written behind</i>).
/// </summary>
/// <param name="EditId">The edit, by the id its gesture arrived under.</param>
/// <param name="Written">Whether its write was confirmed.</param>
/// <param name="Error">Why it was not written, as a sentence for the author.</param>
/// <param name="TakenBack">The later edits taken back with it.</param>
public sealed record TableEditSettled(ShortGuid EditId, bool Written, string Error = "", IReadOnlyList<ShortGuid>? TakenBack = null) : TableChange
{
    public IReadOnlyList<ShortGuid> TakenBack { get; } = TakenBack ?? [];
}

/// <summary>
/// What the author did, in the table's own words. The kinds are a session's to define and the
/// client's table library's to raise; the host carries a gesture and knows none.
/// </summary>
/// <param name="Kind">What was done.</param>
/// <param name="RowId">The row it concerns, or empty.</param>
/// <param name="ColumnId">The column it concerns, or empty.</param>
/// <param name="ViewId">The view it concerns, or empty.</param>
/// <param name="Values">The value or values given, in their written form.</param>
/// <param name="Index">A position, for a gesture that places something.</param>
/// <param name="TargetId">A second thing it names: what something is placed beside.</param>
/// <param name="Settings">Named settings of the gesture.</param>
public sealed record TableGesture(
    string Kind,
    string RowId = "",
    string ColumnId = "",
    string ViewId = "",
    IReadOnlyList<string>? Values = null,
    int Index = 0,
    string TargetId = "",
    IReadOnlyDictionary<string, string>? Settings = null)
{
    public IReadOnlyList<string> Values { get; } = Values ?? [];

    public IReadOnlyDictionary<string, string> Settings { get; } = Settings ?? new Dictionary<string, string>();
}

/// <summary>Carries the changes of one push of an open table.</summary>
public sealed class TableChangedEventArgs(IReadOnlyList<TableChange> changes) : EventArgs
{
    public IReadOnlyList<TableChange> Changes { get; } = changes;
}
