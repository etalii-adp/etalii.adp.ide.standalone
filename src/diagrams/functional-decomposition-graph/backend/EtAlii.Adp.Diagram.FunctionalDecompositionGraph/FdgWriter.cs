using System.Globalization;
using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>What an edit did, or why it did nothing.</summary>
/// <param name="Refusal">The sentence to show, or <c>null</c> when the edit was spliced.</param>
/// <remarks>
/// A refusal is a sentence rather than a code because it is shown to the person who asked for the
/// edit, and because the three checks that can refuse - type, cardinality, cycle - each have
/// something specific to say that "invalid" does not.
/// </remarks>
public readonly record struct FdgEdit(string? Refusal)
{
    /// <summary>The edit was spliced into the document.</summary>
    public static FdgEdit Applied { get; } = new((string?)null);

    /// <summary>The edit was refused, and this is why.</summary>
    public static FdgEdit Refused(string because) => new(because);

    /// <summary>Whether the document changed.</summary>
    public bool WasApplied => Refusal is null;
}

/// <summary>
/// Turns an edit into a splice of the lines that edit affects, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Every write in this module goes through here, and every one is a splice of a range the parser
/// recorded. <b>Nothing serialises a model back to a file</b>, which is what makes "only the
/// affected lines change" true by construction rather than by care - and what makes an unknown
/// key survive for free, because a line nobody splices is a line nobody can damage. That is
/// Requirement 2.3's byte-identical round trip: an unchanged document is not rewritten because
/// there is no code path that could rewrite it.
/// </para>
/// <para>
/// <b>A write invalidates the model that located it.</b> Line numbers after a splice have moved,
/// so callers re-parse rather than adjusting ranges by hand - cheap, and it removes a class of
/// off-by-one bug that is otherwise only found by an edit near the end of a file.
/// </para>
/// </remarks>
public static class FdgWriter
{
    internal const string ElementsSection = "elements:";
    internal const string ConnectionsSection = "connections:";

    /// <summary>Rewrites an element's name. A Comment has none, and says so.</summary>
    public static FdgEdit SetName(LineDocument document, FdgElement element, string name)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(element);

        if (element.IsComment)
        {
            return FdgEdit.Refused("A Comment has no name; its text is what it says.");
        }

        LineSplice.SetKey(document, element.Range, "name", LineSplice.Quote(name));
        return FdgEdit.Applied;
    }

    /// <summary>Rewrites an element's description - prose the client is never sent.</summary>
    public static FdgEdit SetDescription(LineDocument document, FdgElement element, string description)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(element);

        LineSplice.SetKey(document, element.Range, "description", LineSplice.Quote(description));
        return FdgEdit.Applied;
    }

    /// <summary>
    /// Rewrites a Comment's text as a block scalar, so its newlines survive the round trip.
    /// </summary>
    /// <remarks>
    /// <c>|-</c> rather than <c>|</c>: the clipped form keeps the author's last line without
    /// adding a trailing newline the author did not write, which is what a byte comparison
    /// notices first.
    /// </remarks>
    public static FdgEdit SetText(LineDocument document, FdgElement element, string text)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(element);

        if (!element.IsComment)
        {
            return FdgEdit.Refused("Only a Comment carries text.");
        }

        var indent = LineSplice.KeyIndentWithin(document, element.Range);
        List<string> lines = [$"{indent}text: |-"];
        lines.AddRange((text ?? "").Split('\n').Select(line => $"{indent}  {line.TrimEnd('\r')}"));

        ReplaceKeyBlock(document, element.Range, "text", lines);
        return FdgEdit.Applied;
    }

    /// <summary>Moves an element by rewriting its top-left corner.</summary>
    public static FdgEdit MoveTo(LineDocument document, FdgElement element, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(element);

        LineSplice.SetKey(document, element.Range, "x", Number(x));
        LineSplice.SetKey(document, element.Range, "y", Number(y));
        return FdgEdit.Applied;
    }

    /// <summary>
    /// Resizes an element. Width is clamped to the minimum rather than refused, and a height is
    /// written only for a Comment - the other four share <see cref="FdgGeometry.SharedHeight"/>
    /// and storing it per element is what the shared constant exists to avoid.
    /// </summary>
    public static FdgEdit Resize(LineDocument document, FdgElement element, double width, double? height)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(element);

        LineSplice.SetKey(document, element.Range, "width", Number(Math.Max(width, FdgGeometry.MinimumWidth)));

        if (height is null)
        {
            return FdgEdit.Applied;
        }

        if (!element.IsComment)
        {
            return FdgEdit.Refused("Only a Comment has its own height; the others share one.");
        }

        LineSplice.SetKey(document, element.Range, "height", Number(Math.Max(height.Value, FdgGeometry.MinimumCommentHeight)));
        return FdgEdit.Applied;
    }

    /// <summary>Rewrites a connection's name.</summary>
    public static FdgEdit SetConnectionName(LineDocument document, FdgConnection connection, string name)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(connection);

        LineSplice.SetKey(document, connection.Range, "name", LineSplice.Quote(name));
        return FdgEdit.Applied;
    }

    /// <summary>
    /// Removes an element and every connection touching it, bottom-up.
    /// </summary>
    /// <remarks>
    /// <b>Descending by start line, because each removal moves every line after it.</b> Removing
    /// top-down would leave the second range pointing at lines that have already shifted - the
    /// same reason the counterpart module orders its removals this way.
    /// </remarks>
    public static FdgEdit RemoveElement(LineDocument document, FdgModel model, FdgElement element)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(element);

        var ranges = model.Connections
            .Where(connection => connection.From == element.Id || connection.To == element.Id)
            .Select(connection => connection.Range)
            .Append(element.Range)
            .OrderByDescending(range => range.Start)
            .ToList();

        foreach (var range in ranges)
        {
            document.Remove(range);
        }

        return FdgEdit.Applied;
    }

    /// <summary>Removes one connection.</summary>
    public static FdgEdit Disconnect(LineDocument document, FdgConnection connection)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(connection);

        document.Remove(connection.Range);
        return FdgEdit.Applied;
    }

    /// <summary>Appends an element entry, matching whatever indentation the document already uses.</summary>
    public static FdgEdit AddElement(LineDocument document, FdgModel model, FdgElement element)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(element);

        var ranges = model.Elements.Select(existing => existing.Range).ToList();
        var at = LineSplice.InsertionPointFor(document, ranges, ElementsSection);
        if (at < 0)
        {
            return FdgEdit.Refused($"The document has no `{ElementsSection}` section to add to.");
        }

        var (itemIndent, dashGap, keyIndent) = LineSplice.IndentOf(document, ranges);
        List<string> lines =
        [
            $"{itemIndent}-{dashGap}id: {LineSplice.Quote(element.Id)}",
            $"{keyIndent}type: {element.Type}",
        ];

        if (element.IsComment)
        {
            lines.Add($"{keyIndent}text: |-");
            lines.AddRange((element.Text ?? "").Split('\n').Select(line => $"{keyIndent}  {line.TrimEnd('\r')}"));
        }
        else
        {
            lines.Add($"{keyIndent}name: {LineSplice.Quote(element.Name)}");
        }

        lines.Add($"{keyIndent}x: {Number(element.X)}");
        lines.Add($"{keyIndent}y: {Number(element.Y)}");
        lines.Add($"{keyIndent}width: {Number(Math.Max(element.Width, FdgGeometry.MinimumWidth))}");
        if (element.IsComment)
        {
            lines.Add($"{keyIndent}height: {Number(Math.Max(element.DrawnHeight, FdgGeometry.MinimumCommentHeight))}");
        }

        document.Insert(at, lines);
        return FdgEdit.Applied;
    }

    /// <summary>Appends a connection entry.</summary>
    public static FdgEdit Connect(LineDocument document, FdgModel model, FdgConnection connection)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(connection);

        var ranges = model.Connections.Select(existing => existing.Range).ToList();
        var at = LineSplice.InsertionPointFor(document, ranges, ConnectionsSection);
        if (at < 0)
        {
            return FdgEdit.Refused($"The document has no `{ConnectionsSection}` section to add to.");
        }

        var (itemIndent, dashGap, keyIndent) = LineSplice.IndentOf(document, ranges);
        List<string> lines =
        [
            $"{itemIndent}-{dashGap}id: {LineSplice.Quote(connection.Id)}",
            $"{keyIndent}type: {connection.Type}",
            $"{keyIndent}from: {LineSplice.Quote(connection.From)}",
            $"{keyIndent}to: {LineSplice.Quote(connection.To)}",
        ];

        if (!string.IsNullOrEmpty(connection.Name))
        {
            lines.Add($"{keyIndent}name: {LineSplice.Quote(connection.Name)}");
        }

        document.Insert(at, lines);
        return FdgEdit.Applied;
    }

    /// <summary>
    /// Replaces a multi-line key's block, or writes it when the entry has none.
    /// </summary>
    /// <remarks>
    /// <see cref="LineSplice.SetKey"/> rewrites one line, which is right for a scalar and wrong
    /// for a block scalar whose value spans several. This finds the key, walks to the end of its
    /// indented block, and replaces the whole of it.
    /// </remarks>
    private static void ReplaceKeyBlock(LineDocument document, LineRange range, string key, IReadOnlyList<string> lines)
    {
        var at = LineSplice.FindKey(document, range, key);
        if (at < 0)
        {
            document.Insert(range.End + 1, lines);
            return;
        }

        var keyIndent = Indent(document.Lines[at].Text);
        var end = at;
        while (end + 1 <= range.End && Indent(document.Lines[end + 1].Text).Length > keyIndent.Length)
        {
            end++;
        }

        document.Replace(new LineRange(at, end), lines);
    }

    private static string Indent(string text) => text[..(text.Length - text.TrimStart().Length)];

    /// <summary>
    /// A canvas number, written the way the document reads it.
    /// </summary>
    /// <remarks>
    /// Invariant culture and no trailing zeros: a coordinate written on a machine whose decimal
    /// separator is a comma would otherwise be unreadable everywhere else, and <c>120</c> reads
    /// as the author wrote it where <c>120.00</c> reads as a tool's.
    /// </remarks>
    private static string Number(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);
}
