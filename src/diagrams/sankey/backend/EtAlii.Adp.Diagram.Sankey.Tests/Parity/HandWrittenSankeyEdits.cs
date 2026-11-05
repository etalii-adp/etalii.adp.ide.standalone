using System.Globalization;
using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Sankey.Tests.Parity;

/// <summary>
/// The Sankey module's line writer as it was before every command wrote through the binding (sankey-disl-fbl task 11):
/// <c>SankeyWriter</c>, the two of its edits the four damaged shapes of finding B16 are measured against, as it wrote them.
/// </summary>
/// <remarks>
/// <para>
/// It spliced the lines the hand-written reader recorded, finding a key by the start of a line and a section by a line
/// that starts with its name, which is exactly what B16 found it damages; <see cref="SankeyDamagedShapesTests"/> shows
/// each damage here and its absence in the module.
/// </para>
/// </remarks>
internal static class HandWrittenSankeyEdits
{
    private const string NodesSection = "nodes:";

    /// <summary>Writes a text key, removing it instead when <paramref name="removeWhenEmpty"/> and the value is blank.</summary>
    public static void SetText(LineDocument document, LineRange range, string key, string value, bool removeWhenEmpty)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (removeWhenEmpty && string.IsNullOrWhiteSpace(value))
        {
            LineSplice.RemoveKey(document, range, key);
        }
        else
        {
            LineSplice.SetKey(document, range, key, LineSplice.QuoteText(value));
        }
    }

    /// <summary>
    /// Adds a node entry after the last node, matching whatever indentation the document already uses.
    /// </summary>
    public static void AddNode(LineDocument document, SankeyModel model, SankeyNode node)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(node);

        List<(string Key, string Value)> keys = [("name", LineSplice.QuoteText(node.Name))];
        if (node.Color.Length > 0)
        {
            keys.Add(("color", LineSplice.QuoteText(node.Color)));
        }

        if (node.Column is { } column)
        {
            keys.Add(("column", column.ToString(CultureInfo.InvariantCulture)));
        }

        Append(document, model.Nodes.Select(existing => existing.Range).ToList(), NodesSection, node.Id, keys);
    }

    private static void Append(
        LineDocument document,
        IReadOnlyList<LineRange> ranges,
        string section,
        string id,
        IReadOnlyList<(string Key, string Value)> keys)
    {
        var at = LineSplice.InsertionPointFor(document, ranges, section);
        if (at < 0)
        {
            // A hand-written document may leave the section out; the entry brings its section with it.
            document.Insert(document.Lines.Count, [section]);
            at = document.Lines.Count;
        }

        (string itemIndent, string dashGap, string keyIndent) = LineSplice.IndentOf(document, ranges);
        document.Insert(at, Entry(itemIndent, dashGap, keyIndent, id, keys));
    }

    private static List<string> Entry(string itemIndent, string dashGap, string keyIndent, string id, IReadOnlyList<(string Key, string Value)> keys)
    {
        List<string> lines = [$"{itemIndent}-{dashGap}id: {LineSplice.QuoteText(id)}", .. keys.Select(entry => $"{keyIndent}{entry.Key}: {entry.Value}")];
        return lines;
    }
}
