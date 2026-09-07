using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Turns an edit into a splice of the lines that edit affects, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Every write in this module goes through here, and every one of them is a splice of a range the
/// parser recorded. Nothing serialises a model back to a file, which is what makes Requirement
/// 2.2 - only the affected lines change - true by construction rather than by care, and
/// Requirement 2.3 - unmodelled keys survive - free, since a line nobody splices is a line nobody
/// can damage.
/// </para>
/// <para>
/// A write invalidates the model that located it: line numbers after a splice have moved. Callers
/// re-parse rather than adjusting ranges by hand, which is cheap and removes a whole class of
/// off-by-one bug.
/// </para>
/// </remarks>
public static class TimelineWriter
{
    /// <summary>Rewrites an element's label, leaving its quoting style alone elsewhere.</summary>
    public static void SetLabel(LineDocument document, TimelineElement element, string label) =>
        LineSplice.SetKey(document, element.Range, "label", LineSplice.Quote(label));

    /// <summary>Rewrites an element's begin.</summary>
    public static void SetBegin(LineDocument document, TimelineElement element, string begin) =>
        LineSplice.SetKey(document, element.Range, "begin", begin);

    /// <summary>Rewrites an element's row.</summary>
    public static void SetRow(LineDocument document, TimelineElement element, int row) =>
        LineSplice.SetKey(document, element.Range, "row", row.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// Rewrites an element's end, or removes the key entirely when <paramref name="end"/> is null -
    /// which is how a period becomes a moment (Requirement 11.3).
    /// </summary>
    public static void SetEnd(LineDocument document, TimelineElement element, string? end)
    {
        if (end is null)
        {
            LineSplice.RemoveKey(document, element.Range, "end");
            return;
        }

        LineSplice.SetKey(document, element.Range, "end", end);
    }

    /// <summary>Rewrites a connection's label, or removes the key when the label is cleared.</summary>
    public static void SetConnectionLabel(LineDocument document, TimelineConnection connection, string label)
    {
        if (label.Length == 0)
        {
            LineSplice.RemoveKey(document, connection.Range, "label");
            return;
        }

        LineSplice.SetKey(document, connection.Range, "label", LineSplice.Quote(label));
    }

    /// <summary>
    /// Appends an element to the document, after the last one already there.
    /// </summary>
    /// <remarks>
    /// The indentation is copied from whatever is already in the file rather than imposed, so a
    /// document written with four spaces stays written with four spaces (Requirement 2.2). Only an
    /// empty document falls back to this module's own default.
    /// </remarks>
    public static void InsertElement(
        LineDocument document,
        TimelineModel model,
        string id,
        string label,
        string begin,
        string? end,
        int row)
    {
        var (itemIndent, dashGap, keyIndent) = LineSplice.IndentOf(document, model.Elements.Select(element => element.Range));
        var lines = new List<string>
        {
            $"{itemIndent}-{dashGap}id: {id}",
            $"{keyIndent}label: {LineSplice.Quote(label)}",
            $"{keyIndent}begin: {begin}",
        };

        if (end is not null)
        {
            lines.Add($"{keyIndent}end: {end}");
        }

        lines.Add($"{keyIndent}row: {row.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

        document.Insert(LineSplice.InsertionPointFor(document, model.Elements.Select(element => element.Range), "elements:"), lines);
    }

    /// <summary>Appends a connection to the document.</summary>
    public static void InsertConnection(
        LineDocument document,
        TimelineModel model,
        string id,
        string from,
        string to,
        string label)
    {
        var (itemIndent, dashGap, keyIndent) = LineSplice.IndentOf(document, model.Connections.Select(connection => connection.Range));
        var lines = new List<string>
        {
            $"{itemIndent}-{dashGap}id: {id}",
            $"{keyIndent}from: {from}",
            $"{keyIndent}to: {to}",
        };

        if (label.Length > 0)
        {
            lines.Add($"{keyIndent}label: {LineSplice.Quote(label)}");
        }

        var at = LineSplice.InsertionPointFor(document, model.Connections.Select(connection => connection.Range), "connections:");
        if (at < 0)
        {
            // No `connections:` key yet, so the section is created at the end of the document
            // along with its first entry.
            document.Insert(document.Lines.Count, ["connections:", .. lines]);
            return;
        }

        document.Insert(at, lines);
    }

    /// <summary>
    /// Removes an element and every connection that referenced it, in one splice each, from the
    /// bottom of the document upwards.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Descending order matters: removing a range shifts every line after it, so removing top-down
    /// would leave every later range pointing at the wrong lines. Sorting once here is cheaper
    /// than recomputing ranges between removals, and considerably harder to get wrong.
    /// </para>
    /// <para>
    /// The connections go with the element because a connection to something that no longer exists
    /// is not a diagram anybody wants; Requirement 2.5 asks that the count be known beforehand,
    /// which <see cref="ConnectionsTouching"/> answers without performing the removal.
    /// </para>
    /// </remarks>
    public static void RemoveElement(LineDocument document, TimelineModel model, TimelineElement element)
    {
        var ranges = ConnectionsTouching(model, element.Id)
            .Select(connection => connection.Range)
            .Append(element.Range)
            .OrderByDescending(range => range.Start)
            .ToList();

        foreach (var range in ranges)
        {
            document.Remove(range);
        }
    }

    /// <summary>Removes one connection.</summary>
    public static void RemoveConnection(LineDocument document, TimelineConnection connection) =>
        document.Remove(connection.Range);

    /// <summary>Whether the document currently carries a <c>connections:</c> section key.</summary>
    /// <remarks>
    /// An insert creates the section on demand, so a command whose inverse must restore the file
    /// byte for byte asks this first and has its undo remove what the insert created - a stray
    /// <c>connections:</c> header after an undo was the one line that broke identity.
    /// </remarks>
    public static bool HasConnectionsSection(LineDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return LineSplice.FindSection(document, "connections:") >= 0;
    }

    /// <summary>
    /// Removes a <c>connections:</c> section key left with no entries - the undo of the insert
    /// that created it. A no-op while any relation remains.
    /// </summary>
    public static void RemoveConnectionsSectionIfEmpty(LineDocument document, TimelineModel model)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);

        if (model.Connections.Count > 0)
        {
            return;
        }

        var index = LineSplice.FindSection(document, "connections:");
        if (index >= 0)
        {
            document.Remove(new LineRange(index, index));
        }
    }

    /// <summary>
    /// The connections that would go with an element, so an action can say how many before it runs
    /// (Requirement 2.5).
    /// </summary>
    public static IReadOnlyList<TimelineConnection> ConnectionsTouching(TimelineModel model, string elementId)
    {
        ArgumentNullException.ThrowIfNull(model);

        return model.Connections
            .Where(connection =>
                string.Equals(connection.From, elementId, StringComparison.Ordinal) ||
                string.Equals(connection.To, elementId, StringComparison.Ordinal))
            .ToList();
    }
}
