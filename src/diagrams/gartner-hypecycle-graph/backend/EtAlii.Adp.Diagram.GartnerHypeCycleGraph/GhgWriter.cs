using System.Globalization;
using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>What an edit did, or why it did nothing.</summary>
/// <param name="Refusal">The sentence to show, or <c>null</c> when the edit was spliced.</param>
public readonly record struct GhgEdit(string? Refusal)
{
    /// <summary>The edit was spliced into the document.</summary>
    public static GhgEdit Applied { get; } = new((string?)null);

    /// <summary>The edit was refused, and this is why.</summary>
    public static GhgEdit Refused(string because) => new(because);

    /// <summary>Whether the document changed.</summary>
    public bool WasApplied => Refusal is null;
}

/// <summary>
/// Turns an edit into a splice of the lines that edit affects, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing serialises a model back to a file</b>, as in FDG: every write is a splice of a range
/// the parser recorded, so an unchanged document is byte-identical because there is no code path
/// that could rewrite it, and an unknown key survives because nobody splices its line
/// (Requirement 2.5).
/// </para>
/// <para>
/// <b>An edit that adds a key grows its entry, and the range is carried along.</b> Several keys are
/// often written in one edit - a span sets <c>start</c>, <c>stop</c> and up to three boundaries - and
/// a range recorded before the first insertion would miss the entry's last line by the second. So
/// each key is written through <see cref="Set"/>, which moves the range's end by however many lines
/// the splice added or removed.
/// </para>
/// <para>
/// <b>A new key goes where a reader expects it</b>: after the keys that precede it in the design's
/// order, rather than straight after <c>- id:</c>, so a dragged boundary sits beside <c>phases</c>.
/// </para>
/// </remarks>
public static class GhgWriter
{
    internal const string TrendsSection = "trends:";
    internal const string TriggersSection = "triggers:";
    internal const string NotesSection = "notes:";
    internal const string InfluencesSection = "influences:";

    /// <summary>The trend keys in the order a new entry writes them, which is where an added key goes.</summary>
    private static readonly string[] TrendKeyOrder =
        ["id", "name", "start", "stop", "row", "phases", "peak-end", "trough-end", "slope-end", "tags", "description"];

    /// <summary>The trigger keys in the order a new entry writes them.</summary>
    private static readonly string[] TriggerKeyOrder = ["id", "name", "date", "row", "tags", "description"];

    /// <summary>The note keys in the order a new entry writes them.</summary>
    private static readonly string[] NoteKeyOrder = ["id", "text", "at", "row", "width", "height"];

    /// <summary>The influence keys in the order a new entry writes them.</summary>
    private static readonly string[] InfluenceKeyOrder =
        ["id", "from", "from-phase", "from-edge", "from-at", "to", "to-phase", "to-edge", "to-at", "description"];

    /// <summary>Rewrites a trend's name.</summary>
    public static GhgEdit SetName(LineDocument document, GhgTrend trend, string name)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(trend);

        if (string.IsNullOrWhiteSpace(name))
        {
            return GhgEdit.Refused("A trend needs a name.");
        }

        var range = trend.Range;
        Set(document, ref range, TrendKeyOrder, "name", LineSplice.Quote(name.Trim()));
        return GhgEdit.Applied;
    }

    /// <summary>Rewrites a trend's Description. An empty one removes the key.</summary>
    public static GhgEdit SetDescription(LineDocument document, GhgTrend trend, string description)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(trend);

        var range = trend.Range;
        SetOrRemove(document, ref range, TrendKeyOrder, "description", description);
        return GhgEdit.Applied;
    }

    /// <summary>Rewrites an influence's Description. An empty one removes the key.</summary>
    public static GhgEdit SetDescription(LineDocument document, GhgInfluence influence, string description)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(influence);

        var range = influence.Range;
        SetOrRemove(document, ref range, InfluenceKeyOrder, "description", description);
        return GhgEdit.Applied;
    }

    /// <summary>
    /// Rewrites a trend's dates, row and stored boundaries: a move, a resize, or a date typed in the
    /// grid. The caller has already computed what the boundaries become (<see cref="GhgPhases"/>).
    /// </summary>
    public static GhgEdit SetSpan(LineDocument document, GhgTrend trend, int start, int stop, int row, IReadOnlyList<int?> dragged)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(trend);
        ArgumentNullException.ThrowIfNull(dragged);

        if (stop <= start)
        {
            return GhgEdit.Refused("A trend must be at least one month long.");
        }

        var range = trend.Range;
        Set(document, ref range, TrendKeyOrder, "start", GhgScale.FormatMonth(start));
        Set(document, ref range, TrendKeyOrder, "stop", GhgScale.FormatMonth(stop));
        if (row != trend.Row || LineSplice.FindKey(document, range, "row") >= 0)
        {
            Set(document, ref range, TrendKeyOrder, "row", row.ToString(CultureInfo.InvariantCulture));
        }

        WriteBoundaries(document, ref range, dragged);
        return GhgEdit.Applied;
    }

    /// <summary>Rewrites a trend's stored boundaries: a dragged one written, a cleared one removed.</summary>
    public static GhgEdit SetBoundaries(LineDocument document, GhgTrend trend, IReadOnlyList<int?> dragged)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(trend);
        ArgumentNullException.ThrowIfNull(dragged);

        var range = trend.Range;
        WriteBoundaries(document, ref range, dragged);
        return GhgEdit.Applied;
    }

    /// <summary>Rewrites how many phases a trend shows. Its stored boundaries are kept (Requirement 3.4).</summary>
    public static GhgEdit SetPhases(LineDocument document, GhgTrend trend, int phases)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(trend);

        if (phases is < 1 or > GhgPhases.Count)
        {
            return GhgEdit.Refused($"A trend shows 1 to {GhgPhases.Count} phases.");
        }

        var range = trend.Range;
        Set(document, ref range, TrendKeyOrder, "phases", phases.ToString(CultureInfo.InvariantCulture));
        return GhgEdit.Applied;
    }

    /// <summary>Rewrites a trend's tags as one flow sequence line, so a tag added rewrites one line.</summary>
    public static GhgEdit SetTags(LineDocument document, GhgTrend trend, IReadOnlyList<string> tags)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(trend);
        ArgumentNullException.ThrowIfNull(tags);

        var range = trend.Range;
        if (tags.Count == 0)
        {
            Remove(document, ref range, "tags");
        }
        else
        {
            Set(document, ref range, TrendKeyOrder, "tags", FlowSequence(tags));
        }

        return GhgEdit.Applied;
    }

    /// <summary>Rewrites one end of an influence: its phase, edge and <c>at</c>.</summary>
    /// <param name="document">The document.</param>
    /// <param name="influence">The influence.</param>
    /// <param name="side"><c>from</c> or <c>to</c>.</param>
    /// <param name="end">A readable end.</param>
    public static GhgEdit SetEnd(LineDocument document, GhgInfluence influence, string side, GhgEnd end)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(influence);
        ArgumentNullException.ThrowIfNull(end);

        if (side is not ("from" or "to"))
        {
            return GhgEdit.Refused("An influence has a from end and a to end.");
        }

        if (!end.IsReadable)
        {
            return GhgEdit.Refused("An influence attaches to a phase, on its top or bottom edge, at a fraction from 0 to 1.");
        }

        var range = influence.Range;
        Set(document, ref range, InfluenceKeyOrder, $"{side}-phase", end.Phase);
        Set(document, ref range, InfluenceKeyOrder, $"{side}-edge", end.Edge);
        Set(document, ref range, InfluenceKeyOrder, $"{side}-at", GhgEnd.FormatAt(end.At!.Value));
        return GhgEdit.Applied;
    }

    /// <summary>
    /// Removes a trend and every influence touching it, bottom-up, so one undo restores all of them
    /// (Requirement 11.2).
    /// </summary>
    /// <remarks>Descending by start line, because each removal moves every line after it.</remarks>
    public static GhgEdit RemoveTrend(LineDocument document, GhgModel model, GhgTrend trend)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(trend);

        var ranges = model.Influences
            .Where(influence => influence.From == trend.Id || influence.To == trend.Id)
            .Select(influence => influence.Range)
            .Append(trend.Range)
            .OrderByDescending(range => range.Start)
            .ToList();

        foreach (var range in ranges)
        {
            document.Remove(range);
        }

        return GhgEdit.Applied;
    }

    /// <summary>Removes one influence.</summary>
    public static GhgEdit RemoveInfluence(LineDocument document, GhgInfluence influence)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(influence);

        document.Remove(influence.Range);
        return GhgEdit.Applied;
    }

    /// <summary>Appends a trend entry, matching whatever indentation the document already uses.</summary>
    public static GhgEdit AddTrend(LineDocument document, GhgModel model, GhgTrend trend)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(trend);

        if (!trend.HasSpan)
        {
            return GhgEdit.Refused("A trend must be at least one month long.");
        }

        var ranges = model.Trends.Select(existing => existing.Range).ToList();
        var at = LineSplice.InsertionPointFor(document, ranges, TrendsSection);
        if (at < 0)
        {
            return GhgEdit.Refused($"The document has no `{TrendsSection}` section to add to.");
        }

        var (itemIndent, dashGap, keyIndent) = LineSplice.IndentOf(document, ranges);
        List<string> lines =
        [
            $"{itemIndent}-{dashGap}id: {LineSplice.Quote(trend.Id)}",
            $"{keyIndent}name: {LineSplice.Quote(trend.Name)}",
            $"{keyIndent}start: {GhgScale.FormatMonth(trend.Start!.Value)}",
            $"{keyIndent}stop: {GhgScale.FormatMonth(trend.Stop!.Value)}",
            $"{keyIndent}row: {trend.Row.ToString(CultureInfo.InvariantCulture)}",
            $"{keyIndent}phases: {trend.Phases.ToString(CultureInfo.InvariantCulture)}",
        ];

        if (trend.Tags.Count > 0)
        {
            lines.Add($"{keyIndent}tags: {FlowSequence(trend.Tags)}");
        }

        document.Insert(at, lines);
        return GhgEdit.Applied;
    }

    /// <summary>Appends an influence entry.</summary>
    public static GhgEdit AddInfluence(LineDocument document, GhgModel model, GhgInfluence influence)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(influence);

        // An influence from a trigger states no from end at all; every other end must read.
        if ((!influence.FromEnd.IsNone && !influence.FromEnd.IsReadable) || !influence.ToEnd.IsReadable)
        {
            return GhgEdit.Refused("An influence attaches to a phase, on its top or bottom edge, at a fraction from 0 to 1.");
        }

        var ranges = model.Influences.Select(existing => existing.Range).ToList();
        var at = LineSplice.InsertionPointFor(document, ranges, InfluencesSection);
        if (at < 0)
        {
            return GhgEdit.Refused($"The document has no `{InfluencesSection}` section to add to.");
        }

        // With no influence yet to copy, the trends' indentation is the document's style.
        var (itemIndent, dashGap, keyIndent) = ranges.Count > 0
            ? LineSplice.IndentOf(document, ranges)
            : LineSplice.IndentOf(document, model.Trends.Select(trend => trend.Range).Where(range => range.End < document.Lines.Count));
        List<string> lines =
        [
            $"{itemIndent}-{dashGap}id: {LineSplice.Quote(influence.Id)}",
            $"{keyIndent}from: {LineSplice.Quote(influence.From)}",
        ];
        if (!influence.FromEnd.IsNone)
        {
            lines.Add($"{keyIndent}from-phase: {influence.FromEnd.Phase}");
            lines.Add($"{keyIndent}from-edge: {influence.FromEnd.Edge}");
            lines.Add($"{keyIndent}from-at: {GhgEnd.FormatAt(influence.FromEnd.At!.Value)}");
        }

        lines.AddRange(
        [
            $"{keyIndent}to: {LineSplice.Quote(influence.To)}",
            $"{keyIndent}to-phase: {influence.ToEnd.Phase}",
            $"{keyIndent}to-edge: {influence.ToEnd.Edge}",
            $"{keyIndent}to-at: {GhgEnd.FormatAt(influence.ToEnd.At!.Value)}",
        ]);

        document.Insert(at, lines);
        return GhgEdit.Applied;
    }

    /// <summary>Rewrites a trigger's name.</summary>
    public static GhgEdit SetName(LineDocument document, GhgTrigger trigger, string name)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(trigger);

        if (string.IsNullOrWhiteSpace(name))
        {
            return GhgEdit.Refused("A trigger needs a name.");
        }

        var range = trigger.Range;
        Set(document, ref range, TriggerKeyOrder, "name", LineSplice.Quote(name.Trim()));
        return GhgEdit.Applied;
    }

    /// <summary>Rewrites a trigger's Description. An empty one removes the key.</summary>
    public static GhgEdit SetDescription(LineDocument document, GhgTrigger trigger, string description)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(trigger);

        var range = trigger.Range;
        SetOrRemove(document, ref range, TriggerKeyOrder, "description", description);
        return GhgEdit.Applied;
    }

    /// <summary>Rewrites a trigger's tags as one flow sequence line.</summary>
    public static GhgEdit SetTags(LineDocument document, GhgTrigger trigger, IReadOnlyList<string> tags)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(trigger);
        ArgumentNullException.ThrowIfNull(tags);

        var range = trigger.Range;
        if (tags.Count == 0)
        {
            Remove(document, ref range, "tags");
        }
        else
        {
            Set(document, ref range, TriggerKeyOrder, "tags", FlowSequence(tags));
        }

        return GhgEdit.Applied;
    }

    /// <summary>Rewrites a trigger's date and row: a move, or a date typed in the grid.</summary>
    public static GhgEdit SetPlacement(LineDocument document, GhgTrigger trigger, int date, int row)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(trigger);

        var range = trigger.Range;
        Set(document, ref range, TriggerKeyOrder, "date", GhgScale.FormatMonth(date));
        if (row != trigger.Row || LineSplice.FindKey(document, range, "row") >= 0)
        {
            Set(document, ref range, TriggerKeyOrder, "row", row.ToString(CultureInfo.InvariantCulture));
        }

        return GhgEdit.Applied;
    }

    /// <summary>Rewrites a note's text: one line as a plain or quoted scalar, several as a literal block.</summary>
    public static GhgEdit SetText(LineDocument document, GhgNote note, string text)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(note);
        ArgumentNullException.ThrowIfNull(text);

        var range = note.Range;
        var indent = LineSplice.KeyIndentWithin(document, range);
        var index = LineSplice.FindKey(document, range, "text");
        if (index >= 0)
        {
            var existing = document.Lines[index].Text;
            var prefix = existing.TrimStart().StartsWith("- ", StringComparison.Ordinal)
                ? existing[..(existing.Length - existing.TrimStart().Length)] + "- "
                : existing[..(existing.Length - existing.TrimStart().Length)];
            var end = BlockEnd(document, range, index);
            var replacement = TextLines(prefix, indent, text);
            document.Replace(new LineRange(index, end), replacement);
            return GhgEdit.Applied;
        }

        var after = LineSplice.FindKey(document, range, "id");
        document.Insert((after >= 0 ? after : range.Start) + 1, TextLines(indent, indent, text));
        return GhgEdit.Applied;
    }

    /// <summary>Rewrites where a note's top-left sits: the month of its left edge and the row of its top.</summary>
    public static GhgEdit SetPlacement(LineDocument document, GhgNote note, int at, int row)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(note);

        var range = note.Range;
        Set(document, ref range, NoteKeyOrder, "at", GhgScale.FormatMonth(at));
        Set(document, ref range, NoteKeyOrder, "row", row.ToString(CultureInfo.InvariantCulture));
        return GhgEdit.Applied;
    }

    /// <summary>Rewrites a note's size, and its left edge's month when the left border moved.</summary>
    public static GhgEdit SetSize(LineDocument document, GhgNote note, int at, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(note);

        if (width <= 0 || height <= 0)
        {
            return GhgEdit.Refused("A note needs a width and a height.");
        }

        var range = note.Range;
        if (note.At != at)
        {
            Set(document, ref range, NoteKeyOrder, "at", GhgScale.FormatMonth(at));
        }

        Set(document, ref range, NoteKeyOrder, "width", FormatSize(width));
        Set(document, ref range, NoteKeyOrder, "height", FormatSize(height));
        return GhgEdit.Applied;
    }

    /// <summary>
    /// Removes a trigger and every influence touching it, bottom-up, so one undo restores all of
    /// them (Requirement 3.5).
    /// </summary>
    public static GhgEdit RemoveTrigger(LineDocument document, GhgModel model, GhgTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(trigger);

        var ranges = model.Influences
            .Where(influence => influence.From == trigger.Id || influence.To == trigger.Id)
            .Select(influence => influence.Range)
            .Append(trigger.Range)
            .OrderByDescending(range => range.Start)
            .ToList();

        foreach (var range in ranges)
        {
            document.Remove(range);
        }

        return GhgEdit.Applied;
    }

    /// <summary>Removes one note. A note takes part in no relation, so nothing else goes with it.</summary>
    public static GhgEdit RemoveNote(LineDocument document, GhgNote note)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(note);

        document.Remove(note.Range);
        return GhgEdit.Applied;
    }

    /// <summary>
    /// Appends a trigger entry, opening a <c>triggers:</c> list before <c>influences:</c> when the
    /// document has none - so a document without triggers is written exactly as before until one is added.
    /// </summary>
    public static GhgEdit AddTrigger(LineDocument document, GhgModel model, GhgTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(trigger);

        if (trigger.Date is not { } date)
        {
            return GhgEdit.Refused("A trigger needs a date.");
        }

        var ranges = model.Triggers.Select(existing => existing.Range).ToList();
        var (at, itemIndent, dashGap, keyIndent) = EntryPoint(document, model, ranges, TriggersSection);
        List<string> lines =
        [
            $"{itemIndent}-{dashGap}id: {LineSplice.Quote(trigger.Id)}",
            $"{keyIndent}name: {LineSplice.Quote(trigger.Name)}",
            $"{keyIndent}date: {GhgScale.FormatMonth(date)}",
            $"{keyIndent}row: {trigger.Row.ToString(CultureInfo.InvariantCulture)}",
        ];

        if (trigger.Tags.Count > 0)
        {
            lines.Add($"{keyIndent}tags: {FlowSequence(trigger.Tags)}");
        }

        document.Insert(at, lines);
        return GhgEdit.Applied;
    }

    /// <summary>Appends a note entry, opening a <c>notes:</c> list before <c>influences:</c> when the document has none.</summary>
    public static GhgEdit AddNote(LineDocument document, GhgModel model, GhgNote note)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(note);

        if (!note.IsPlaceable)
        {
            return GhgEdit.Refused("A note needs a position, a width and a height.");
        }

        var ranges = model.Notes.Select(existing => existing.Range).ToList();
        var (at, itemIndent, dashGap, keyIndent) = EntryPoint(document, model, ranges, NotesSection);
        List<string> lines = [$"{itemIndent}-{dashGap}id: {LineSplice.Quote(note.Id)}"];
        lines.AddRange(TextLines(keyIndent, keyIndent, note.Text));
        lines.AddRange(
        [
            $"{keyIndent}at: {GhgScale.FormatMonth(note.At!.Value)}",
            $"{keyIndent}row: {note.Row.ToString(CultureInfo.InvariantCulture)}",
            $"{keyIndent}width: {FormatSize(note.Width!.Value)}",
            $"{keyIndent}height: {FormatSize(note.Height!.Value)}",
        ]);

        document.Insert(at, lines);
        return GhgEdit.Applied;
    }

    /// <summary>
    /// Where a new entry of a list goes, and the indentation to write it with: after the list's
    /// last entry; after its key when it has none; and, when the document has no such list at all,
    /// after a new key opened directly before <c>influences:</c> - or at the end, when there is no
    /// <c>influences:</c> either. The indentation is the list's own, else the trends'.
    /// </summary>
    private static (int At, string ItemIndent, string DashGap, string KeyIndent) EntryPoint(
        LineDocument document,
        GhgModel model,
        List<LineRange> ranges,
        string section)
    {
        var (itemIndent, dashGap, keyIndent) = ranges.Count > 0
            ? LineSplice.IndentOf(document, ranges)
            : LineSplice.IndentOf(document, model.Trends.Select(trend => trend.Range).Where(range => range.End < document.Lines.Count));

        var at = LineSplice.InsertionPointFor(document, ranges, section);
        if (at >= 0)
        {
            return (at, itemIndent, dashGap, keyIndent);
        }

        var influences = LineSplice.FindSection(document, InfluencesSection);
        var opening = influences >= 0 ? influences : document.Lines.Count;
        document.Insert(opening, [section]);
        return (opening + 1, itemIndent, dashGap, keyIndent);
    }

    /// <summary>
    /// A note's <c>text</c> key as lines: one plain or quoted scalar for text without a line break,
    /// otherwise a literal block (<c>|-</c>) indented two further than the key. A block whose first
    /// line starts with a space states its indentation, or YAML would read the space as indentation.
    /// </summary>
    private static List<string> TextLines(string keyPrefix, string keyIndent, string text)
    {
        var normalised = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n');
        if (!normalised.Contains('\n', StringComparison.Ordinal))
        {
            return [$"{keyPrefix}text: {LineSplice.Quote(normalised)}"];
        }

        var blockIndent = keyIndent + "  ";
        var indicator = normalised.StartsWith(' ') ? "|2-" : "|-";
        List<string> lines = [$"{keyPrefix}text: {indicator}"];
        lines.AddRange(normalised.Split('\n').Select(line => line.Length == 0 ? "" : blockIndent + line));
        return lines;
    }

    /// <summary>
    /// The last line of the key on <paramref name="keyLine"/>: its own line, or the last line of the
    /// block scalar that follows it - every line indented further than the key, blank lines inside it
    /// included.
    /// </summary>
    private static int BlockEnd(LineDocument document, LineRange range, int keyLine)
    {
        var keyText = document.Lines[keyLine].Text;
        var keyIndent = keyText.Length - keyText.TrimStart().TrimStart('-').TrimStart().Length;
        var end = keyLine;
        for (var index = keyLine + 1; index <= range.End && index < document.Lines.Count; index++)
        {
            var text = document.Lines[index].Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (text.Length - text.TrimStart().Length <= keyIndent)
            {
                break;
            }

            end = index;
        }

        return end;
    }

    /// <summary>A size as the document writes it: a whole number without decimals, otherwise at most two.</summary>
    private static string FormatSize(double size) => Math.Round(size, 2).ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Tags as a YAML flow sequence, quoting a tag only where a flow sequence needs it.</summary>
    internal static string FlowSequence(IReadOnlyList<string> tags) =>
        $"[{string.Join(", ", tags.Select(QuoteInFlow))}]";

    private static string QuoteInFlow(string tag)
    {
        var quoted = LineSplice.Quote(tag);
        if (quoted != tag || tag.IndexOfAny([',', '[', ']', '{', '}']) < 0)
        {
            return quoted;
        }

        return $"\"{tag.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static void WriteBoundaries(LineDocument document, ref LineRange range, IReadOnlyList<int?> dragged)
    {
        for (var index = 0; index < GhgPhases.BoundaryKeys.Count; index++)
        {
            var key = GhgPhases.BoundaryKeys[index];
            if (index < dragged.Count && dragged[index] is { } month)
            {
                Set(document, ref range, TrendKeyOrder, key, GhgScale.FormatMonth(month));
            }
            else
            {
                Remove(document, ref range, key);
            }
        }
    }

    private static void SetOrRemove(LineDocument document, ref LineRange range, string[] order, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Remove(document, ref range, key);
        }
        else
        {
            Set(document, ref range, order, key, LineSplice.Quote(value));
        }
    }

    /// <summary>
    /// Writes one key inside an entry: in place when it is there, otherwise after the last key that
    /// precedes it in <paramref name="order"/>. The range's end follows the lines added.
    /// </summary>
    private static void Set(LineDocument document, ref LineRange range, string[] order, string key, string value)
    {
        if (LineSplice.FindKey(document, range, key) >= 0)
        {
            LineSplice.SetKey(document, range, key, value);
            return;
        }

        var after = -1;
        foreach (var earlier in order.TakeWhile(candidate => candidate != key))
        {
            after = Math.Max(after, LineSplice.FindKey(document, range, earlier));
        }

        var indent = LineSplice.KeyIndentWithin(document, range);
        document.Insert((after >= 0 ? after : range.Start) + 1, [$"{indent}{key}: {value}"]);
        range = range with { End = range.End + 1 };
    }

    private static void Remove(LineDocument document, ref LineRange range, string key)
    {
        if (LineSplice.FindKey(document, range, key) >= 0)
        {
            LineSplice.RemoveKey(document, range, key);
            range = range with { End = range.End - 1 };
        }
    }
}
