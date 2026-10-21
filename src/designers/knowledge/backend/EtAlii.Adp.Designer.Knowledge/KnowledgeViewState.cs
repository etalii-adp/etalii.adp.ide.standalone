using EtAlii.Adp.Designer.TableModel;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// What one connection looks at in a knowledge table: the view it has open, the rows added while
/// that view was open, what it searches the rows for, and the window of lines it has in sight.
/// None of it is in the file, so another connection to the same file looks at its own.
/// </summary>
/// <remarks>
/// <b>Not thread-safe.</b> The <see cref="KnowledgeSession"/> that owns it touches it only with its
/// own lock held, together with what is shown, so the lines it is asked for are of that table.
/// </remarks>
/// <param name="viewId">The view the connection opens with: the one the file was left in.</param>
internal sealed class KnowledgeViewState(string viewId)
{
    /// <summary>The rows added while this view was open: they stay in sight until it is next opened, whatever its filter says.</summary>
    private readonly HashSet<string> _kept = new(StringComparer.Ordinal);

    private (int First, int Count)? _window;

    /// <summary>The view this connection shows, by its id: another connection leaving the file in another view does not move it.</summary>
    public string ViewId { get; private set; } = viewId;

    /// <summary>What this connection searches the rows for; empty for all of them.</summary>
    public string Search { get; private set; } = "";

    /// <summary>Opens a view: the rows kept in sight by the one before are no longer kept.</summary>
    public void Open(string viewId)
    {
        ViewId = viewId;
        _kept.Clear();
    }

    /// <summary>Keeps a row that was just added in sight until the view is next opened.</summary>
    public void Keep(string rowId) => _kept.Add(rowId);

    /// <summary>Searches the rows for a text; empty, or only spaces, for all of them.</summary>
    public void SearchFor(string text) => Search = text.Trim();

    /// <summary>Which lines the connection has in sight: a first line and a count, neither below zero.</summary>
    public void SetWindow(int first, int count) => _window = (Math.Max(0, first), Math.Max(0, count));

    /// <summary>The lines of the open view of <paramref name="table"/>, in order: its rows filtered, searched, sorted and grouped or nested.</summary>
    /// <param name="table">The table as it is shown, the computed sides of its relations filled in.</param>
    /// <param name="unwritten">The cells whose value is an edit not yet written.</param>
    /// <param name="editable">Whether the lines a new row is added at are given.</param>
    /// <param name="labelOf">What a related row is called, by the relation's property and the row's id.</param>
    public List<TableRow> Lines(KnowledgeTable table, IReadOnlySet<(string RowId, string ColumnId)> unwritten, bool editable, Func<string, string, string> labelOf) =>
        KnowledgeViewEngine.Lines(table, table.ViewOrDefault(ViewId), _kept, unwritten, editable, labelOf, Search);

    /// <summary>The rows of the open view of <paramref name="table"/> that are in sight: its filter and the search let them through, or they are kept.</summary>
    public List<KnowledgeRow> InSight(KnowledgeTable table, Func<string, string, string> labelOf) =>
        KnowledgeViewEngine.InSight(table, table.ViewOrDefault(ViewId), _kept, labelOf, Search);

    /// <summary>The lines in the window, or null while the connection has not said which it has in sight.</summary>
    /// <param name="lines">All the lines; asked for only when there is a window, since making them reads the tables relations point at.</param>
    public TableRowsChanged? RowsInWindow(Func<List<TableRow>> lines)
    {
        if (_window is not { } window)
        {
            return null;
        }

        var all = lines();
        var first = Math.Min(window.First, all.Count);
        return new TableRowsChanged(first, all.GetRange(first, Math.Min(window.Count, all.Count - first)), all.Count);
    }
}
