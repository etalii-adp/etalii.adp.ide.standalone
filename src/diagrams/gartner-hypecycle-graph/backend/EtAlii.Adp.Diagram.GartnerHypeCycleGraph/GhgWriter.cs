using System.Globalization;
using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>What an edit did, or why it did nothing.</summary>
/// <param name="Refusal">The sentence to show, or <c>null</c> when the edit was spliced.</param>
public readonly record struct GhgEdit(string? Refusal)
{
    /// <summary>The edit was spliced into the document.</summary>
    public static GhgEdit Applied { get; } = new(null);

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
    internal const string InfluencesSection = "influences:";

    /// <summary>The trend keys in the order a new entry writes them, which is where an added key goes.</summary>
    private static readonly string[] TrendKeyOrder =
        ["id", "name", "start", "stop", "row", "phases", "peak-end", "trough-end", "slope-end", "tags", "description"];

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

        if (!influence.FromEnd.IsReadable || !influence.ToEnd.IsReadable)
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
            $"{keyIndent}from-phase: {influence.FromEnd.Phase}",
            $"{keyIndent}from-edge: {influence.FromEnd.Edge}",
            $"{keyIndent}from-at: {GhgEnd.FormatAt(influence.FromEnd.At!.Value)}",
            $"{keyIndent}to: {LineSplice.Quote(influence.To)}",
            $"{keyIndent}to-phase: {influence.ToEnd.Phase}",
            $"{keyIndent}to-edge: {influence.ToEnd.Edge}",
            $"{keyIndent}to-at: {GhgEnd.FormatAt(influence.ToEnd.At!.Value)}",
        ];

        document.Insert(at, lines);
        return GhgEdit.Applied;
    }

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
