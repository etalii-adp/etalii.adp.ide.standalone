using EtAlii.Adp.Specification.Fbl.Planning;

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
/// Turns an edit into one FBL model change, which the library plans as splices of the body.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing serialises a model back to a file, and nothing here splices either.</b> Each method
/// states the attributes it changes, adds or removes, in the binding's attribute names, and
/// <see cref="GhgBody"/> hands that to FBL (FBL §6.4): where a key goes, how a value is quoted, what
/// indentation and line ending a new entry gets, all follow the binding and the body's own
/// conventions (FBL §6.3) rather than code in this module.
/// </para>
/// <para>
/// <b>What stays here is what FBL does not decide</b>: the module's own refusals (a trend needs a
/// name, a span at least a month), and turning months and ends into the values the file holds.
/// </para>
/// </remarks>
public static class GhgWriter
{
    private const string Trend = "Trend";
    private const string Trigger = "Trigger";
    private const string Note = "Note";
    private const string Influence = "Influence";

    /// <summary>The trend attributes that hold the stored boundaries, in boundary order.</summary>
    private static readonly string[] BoundaryAttributes = ["peakEnd", "troughEnd", "slopeEnd"];

    /// <summary>Rewrites a trend's name.</summary>
    public static GhgEdit SetName(GhgBody body, GhgTrend trend, string name)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(trend);

        if (string.IsNullOrWhiteSpace(name))
        {
            return GhgEdit.Refused("A trend needs a name.");
        }

        return body.Set(Trend, trend.Id, trend.Range, Values(("name", name.Trim())));
    }

    /// <summary>Rewrites a trend's Description. An empty one removes the key.</summary>
    public static GhgEdit SetDescription(GhgBody body, GhgTrend trend, string description)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(trend);

        return body.Set(Trend, trend.Id, trend.Range, Values(("description", Optional(description))));
    }

    /// <summary>Rewrites an influence's Description. An empty one removes the key.</summary>
    public static GhgEdit SetDescription(GhgBody body, GhgInfluence influence, string description)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(influence);

        return body.Set(Influence, influence.Id, influence.Range, Values(("description", Optional(description))));
    }

    /// <summary>
    /// Rewrites a trend's dates, row and stored boundaries: a move, a resize, or a date typed in the
    /// grid. The caller has already computed what the boundaries become (<see cref="GhgPhases"/>).
    /// </summary>
    public static GhgEdit SetSpan(GhgBody body, GhgTrend trend, int start, int stop, int row, IReadOnlyList<int?> dragged)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(trend);
        ArgumentNullException.ThrowIfNull(dragged);

        if (stop <= start)
        {
            return GhgEdit.Refused("A trend must be at least one month long.");
        }

        var values = Values(("start", GhgScale.FormatMonth(start)), ("stop", GhgScale.FormatMonth(stop)));
        if (row != trend.Row)
        {
            values["row"] = row;
        }

        AddBoundaries(values, dragged);
        return body.Set(Trend, trend.Id, trend.Range, values);
    }

    /// <summary>Rewrites a trend's stored boundaries: a dragged one written, a cleared one removed.</summary>
    public static GhgEdit SetBoundaries(GhgBody body, GhgTrend trend, IReadOnlyList<int?> dragged)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(trend);
        ArgumentNullException.ThrowIfNull(dragged);

        var values = Values();
        AddBoundaries(values, dragged);
        return body.Set(Trend, trend.Id, trend.Range, values);
    }

    /// <summary>Rewrites how many phases a trend shows. Its stored boundaries are kept (Requirement 3.4).</summary>
    public static GhgEdit SetPhases(GhgBody body, GhgTrend trend, int phases)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(trend);

        if (phases is < 1 or > GhgPhases.Count)
        {
            return GhgEdit.Refused($"A trend shows 1 to {GhgPhases.Count} phases.");
        }

        return body.Set(Trend, trend.Id, trend.Range, Values(("phases", phases)));
    }

    /// <summary>Rewrites a trend's tags as one flow sequence, so a tag added rewrites one line.</summary>
    public static GhgEdit SetTags(GhgBody body, GhgTrend trend, IReadOnlyList<string> tags)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(trend);
        ArgumentNullException.ThrowIfNull(tags);

        return body.Set(Trend, trend.Id, trend.Range, Values(("tags", TagList(tags))));
    }

    /// <summary>Rewrites one end of an influence: its phase, edge and <c>at</c>.</summary>
    /// <param name="body">The body.</param>
    /// <param name="influence">The influence.</param>
    /// <param name="side"><c>from</c> or <c>to</c>.</param>
    /// <param name="end">A readable end.</param>
    public static GhgEdit SetEnd(GhgBody body, GhgInfluence influence, string side, GhgEnd end)
    {
        ArgumentNullException.ThrowIfNull(body);
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

        return body.Set(Influence, influence.Id, influence.Range, Values(
            ($"{side}Phase", end.Phase),
            ($"{side}Edge", end.Edge),
            ($"{side}At", Math.Round(end.At!.Value, 2))));
    }

    /// <summary>Removes one influence.</summary>
    public static GhgEdit RemoveInfluence(GhgBody body, GhgInfluence influence)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(influence);

        return body.Remove(Influence, influence.Id, influence.Range);
    }

    /// <summary>Appends an influence entry.</summary>
    public static GhgEdit AddInfluence(GhgBody body, GhgModel model, GhgInfluence influence)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(influence);

        // An influence from a trigger states no from end at all; every other end must read.
        if (influence.FromEnd is { IsNone: false, IsReadable: false } || !influence.ToEnd.IsReadable)
        {
            return GhgEdit.Refused("An influence attaches to a phase, on its top or bottom edge, at a fraction from 0 to 1.");
        }

        var values = Values(("from", influence.From));
        if (!influence.FromEnd.IsNone)
        {
            values["fromPhase"] = influence.FromEnd.Phase;
            values["fromEdge"] = influence.FromEnd.Edge;
            values["fromAt"] = Math.Round(influence.FromEnd.At!.Value, 2);
        }

        values["to"] = influence.To;
        values["toPhase"] = influence.ToEnd.Phase;
        values["toEdge"] = influence.ToEnd.Edge;
        values["toAt"] = Math.Round(influence.ToEnd.At!.Value, 2);
        var edit = body.Change(new ModelChange.Add(Influence, influence.Id, values));

        // The binding opens no influences list: a graph without one is refused in the module's own words.
        return edit.Refusal != "The file has no influences to add to."
            ? edit
            : GhgEdit.Refused("The document has no `influences:` section to add to.");
    }

    /// <summary>Rewrites a trigger's name.</summary>
    public static GhgEdit SetName(GhgBody body, GhgTrigger trigger, string name)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(trigger);

        if (string.IsNullOrWhiteSpace(name))
        {
            return GhgEdit.Refused("A trigger needs a name.");
        }

        return body.Set(Trigger, trigger.Id, trigger.Range, Values(("name", name.Trim())));
    }

    /// <summary>Rewrites a trigger's Description. An empty one removes the key.</summary>
    public static GhgEdit SetDescription(GhgBody body, GhgTrigger trigger, string description)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(trigger);

        return body.Set(Trigger, trigger.Id, trigger.Range, Values(("description", Optional(description))));
    }

    /// <summary>Rewrites a trigger's tags as one flow sequence.</summary>
    public static GhgEdit SetTags(GhgBody body, GhgTrigger trigger, IReadOnlyList<string> tags)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(trigger);
        ArgumentNullException.ThrowIfNull(tags);

        return body.Set(Trigger, trigger.Id, trigger.Range, Values(("tags", TagList(tags))));
    }

    /// <summary>Rewrites a trend's row alone - an arrangement, which never touches a date.</summary>
    public static GhgEdit SetRow(GhgBody body, GhgTrend trend, int row)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(trend);

        return body.Set(Trend, trend.Id, trend.Range, Values(("row", row)));
    }

    /// <summary>Rewrites a trigger's row alone.</summary>
    public static GhgEdit SetRow(GhgBody body, GhgTrigger trigger, int row)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(trigger);

        return body.Set(Trigger, trigger.Id, trigger.Range, Values(("row", row)));
    }

    /// <summary>Rewrites a note's row alone.</summary>
    public static GhgEdit SetRow(GhgBody body, GhgNote note, int row)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(note);

        return body.Set(Note, note.Id, note.Range, Values(("row", row)));
    }

    /// <summary>Rewrites a trigger's date and row: a move, or a date typed in the grid.</summary>
    public static GhgEdit SetPlacement(GhgBody body, GhgTrigger trigger, int date, int row)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(trigger);

        var values = Values(("date", GhgScale.FormatMonth(date)));
        if (row != trigger.Row)
        {
            values["row"] = row;
        }

        return body.Set(Trigger, trigger.Id, trigger.Range, values);
    }

    /// <summary>Rewrites a note's text: one line as a plain or quoted scalar, several as a literal block.</summary>
    public static GhgEdit SetText(GhgBody body, GhgNote note, string text)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(note);
        ArgumentNullException.ThrowIfNull(text);

        return body.Set(Note, note.Id, note.Range, Values(("text", Normalised(text))));
    }

    /// <summary>Rewrites where a note's top-left sits: the month of its left edge and the row of its top.</summary>
    public static GhgEdit SetPlacement(GhgBody body, GhgNote note, int at, int row)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(note);

        return body.Set(Note, note.Id, note.Range, Values(("at", GhgScale.FormatMonth(at)), ("row", row)));
    }

    /// <summary>Rewrites a note's size, and its left edge's month and top's row when the left or top border moved.</summary>
    public static GhgEdit SetSize(GhgBody body, GhgNote note, int at, int row, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(note);

        if (width <= 0 || height <= 0)
        {
            return GhgEdit.Refused("A note needs a width and a height.");
        }

        var values = Values();
        if (note.At != at)
        {
            values["at"] = GhgScale.FormatMonth(at);
        }

        if (note.Row != row)
        {
            values["row"] = row;
        }

        values["width"] = width;
        values["height"] = height;
        return body.Set(Note, note.Id, note.Range, values);
    }

    private static Dictionary<string, object?> Values(params (string Attribute, object? Value)[] values) =>
        values.ToDictionary(value => value.Attribute, value => value.Value, StringComparer.Ordinal);

    /// <summary>A text that empties its attribute when it is blank, so the binding's <c>empty: remove</c> drops the key.</summary>
    private static string Optional(string text) => string.IsNullOrWhiteSpace(text) ? "" : text;

    /// <summary>Line breaks as LF, trailing ones dropped: FBL writes a multi-line value as a literal block in the body's own ending.</summary>
    private static string Normalised(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n');

    private static List<object?> TagList(IReadOnlyList<string> tags) => [.. tags];

    private static void AddBoundaries(Dictionary<string, object?> values, IReadOnlyList<int?> dragged)
    {
        for (var index = 0; index < BoundaryAttributes.Length; index++)
        {
            values[BoundaryAttributes[index]] = index < dragged.Count && dragged[index] is { } month ? GhgScale.FormatMonth(month) : null;
        }
    }
}
