using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests.Parity;

/// <summary>
/// The adds, the removals, Even phases, the span's rescale and the removal's confirmation as the
/// module wrote them by hand before they were run from the DISL definition (runtime plan step S13):
/// the handlers' bodies and the <c>GhgWriter</c> methods they called, kept verbatim as the oracle the
/// interpreter's edits are compared with byte for byte (decision D6).
/// </summary>
internal static class HandWrittenGhgEdits
{
    private const string Trend = "Trend";
    private const string Trigger = "Trigger";
    private const string Note = "Note";

    /// <summary><c>AddGhgTrendCommandHandler</c>'s edit, after its id check.</summary>
    public static GhgEdit AddTrendAt(GhgBody document, GhgModel model, string id, double x, double y)
    {
        var start = GhgScale.MonthContaining(x, model.TimeUnit);
        var trend = new GhgTrend(
            id,
            GhgEdits.UniqueName(model.Trends.Select(trend => trend.Name), AddGhgTrendCommandHandler.DefaultName),
            start,
            start + (AddGhgTrendCommandHandler.DefaultMonths * model.TimeUnit.Months),
            GhgScale.RowAtMiddle(y),
            GhgPhases.Count,
            [null, null, null],
            [],
            Description: "",
            Range: new LineRange(0, 0));

        return AddTrend(document, model, trend);
    }

    /// <summary><c>AddGhgTriggerCommandHandler</c>'s edit, after its id check.</summary>
    public static GhgEdit AddTriggerAt(GhgBody document, GhgModel model, string id, double x, double y)
    {
        var trigger = new GhgTrigger(
            id,
            GhgEdits.UniqueName(model.Triggers.Select(existing => existing.Name), AddGhgTriggerCommandHandler.DefaultName),
            GhgScale.MonthContaining(x, model.TimeUnit),
            GhgScale.RowAtMiddle(y),
            [],
            Description: "",
            Range: new LineRange(0, 0));

        return AddTrigger(document, model, trigger);
    }

    /// <summary><c>AddGhgNoteCommandHandler</c>'s edit, after its id check.</summary>
    public static GhgEdit AddNoteAt(GhgBody document, GhgModel model, string id, double x, double y)
    {
        var note = new GhgNote(
            id,
            "",
            GhgScale.MonthContaining(x, model.TimeUnit),
            (int)Math.Floor(y / GhgScale.RowStep),
            AddGhgNoteCommandHandler.DefaultWidth,
            AddGhgNoteCommandHandler.DefaultHeight,
            new LineRange(0, 0));

        return AddNote(document, model, note);
    }

    /// <summary><c>ClearGhgBoundariesCommandHandler</c>'s edit.</summary>
    public static GhgEdit ClearBoundaries(GhgBody document, GhgModel model, string trendId)
    {
        if (GhgEdits.TrendOf(model, trendId) is not { } trend)
        {
            return GhgEdits.Gone();
        }

        return trend.DraggedEnds.All(boundary => boundary is null)
            ? GhgEdit.Refused("This trend's phases are already even.")
            : GhgWriter.SetBoundaries(document, trend, [null, null, null]);
    }

    /// <summary><c>RemoveGhgElementCommandHandler</c>'s edit.</summary>
    public static GhgEdit Remove(GhgBody document, GhgModel model, string elementId)
    {
        if (GhgEdits.TrendOf(model, elementId) is { } trend)
        {
            return RemoveTrend(document, model, trend);
        }

        if (GhgEdits.TriggerOf(model, elementId) is { } trigger)
        {
            return RemoveTrigger(document, model, trigger);
        }

        return GhgEdits.NoteOf(model, elementId) is { } note
            ? RemoveNote(document, note)
            : GhgEdits.Gone();
    }

    /// <summary><c>SetGhgSpanCommandHandler</c>'s write of a trend's new span, its boundaries rescaled and clamped.</summary>
    public static GhgEdit SetSpan(GhgBody document, GhgTrend trend, int start, int stop)
    {
        var dragged = GhgPhases.Rescaled(trend.DraggedEnds, trend.Start!.Value, trend.Stop!.Value, start, stop, trend.VisiblePhases);
        return GhgWriter.SetSpan(document, trend, start, stop, trend.Row, dragged);
    }

    /// <summary>
    /// <c>GhgContextActionProvider</c>'s confirmation before removing <paramref name="id"/>, as
    /// "title | message | confirm label | danger"; null when it removed at once.
    /// </summary>
    public static string? RemoveConfirmation(GhgModel model, string id)
    {
        var trend = GhgEdits.TrendOf(model, id);
        var trigger = GhgEdits.TriggerOf(model, id);
        var note = GhgEdits.NoteOf(model, id);
        var what = trend is not null ? "trend" : "trigger";
        var going = note is not null ? 0 : model.Influences.Count(influence => influence.From == id || influence.To == id);
        if (going == 0)
        {
            return null;
        }

        var message = going == 1
            ? $"Removing this {what} also removes the 1 influence to or from it."
            : $"Removing this {what} also removes the {going} influences to or from it.";
        return $"Remove | {message} | Remove | True";
    }

    /// <summary>
    /// Removes a trend and every influence touching it - the binding's cascade - in one edit, so one
    /// undo restores all of them (Requirement 11.2).
    /// </summary>
    public static GhgEdit RemoveTrend(GhgBody body, GhgModel model, GhgTrend trend)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(trend);

        return body.Remove(Trend, trend.Id, trend.Range);
    }


    /// <summary>Appends a trend entry, in the indentation the document already uses.</summary>
    public static GhgEdit AddTrend(GhgBody body, GhgModel model, GhgTrend trend)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(trend);

        if (!trend.HasSpan)
        {
            return GhgEdit.Refused("A trend must be at least one month long.");
        }

        return body.Change(new ModelChange.Add(Trend, trend.Id, Values(
            ("name", trend.Name),
            ("start", GhgScale.FormatMonth(trend.Start!.Value)),
            ("stop", GhgScale.FormatMonth(trend.Stop!.Value)),
            ("row", trend.Row),
            ("phases", trend.Phases),
            ("tags", TagList(trend.Tags)))));
    }


    /// <summary>
    /// Removes a trigger and every influence touching it - the binding's cascade - in one edit
    /// (Requirement 3.5).
    /// </summary>
    public static GhgEdit RemoveTrigger(GhgBody body, GhgModel model, GhgTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(trigger);

        return body.Remove(Trigger, trigger.Id, trigger.Range);
    }

    /// <summary>Removes one note. A note takes part in no relation, so nothing else goes with it.</summary>
    public static GhgEdit RemoveNote(GhgBody body, GhgNote note)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(note);

        return body.Remove(Note, note.Id, note.Range);
    }

    /// <summary>
    /// Appends a trigger entry. A document without a <c>triggers:</c> list gets one before
    /// <c>influences:</c> (the binding's <c>insert.create</c>), so it is written exactly as before
    /// until one is added.
    /// </summary>
    public static GhgEdit AddTrigger(GhgBody body, GhgModel model, GhgTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(trigger);

        if (trigger.Date is not { } date)
        {
            return GhgEdit.Refused("A trigger needs a date.");
        }

        return body.Change(new ModelChange.Add(Trigger, trigger.Id, Values(
            ("name", trigger.Name),
            ("date", GhgScale.FormatMonth(date)),
            ("row", trigger.Row),
            ("tags", TagList(trigger.Tags)))));
    }

    /// <summary>Appends a note entry, opening a <c>notes:</c> list before <c>influences:</c> when the document has none.</summary>
    public static GhgEdit AddNote(GhgBody body, GhgModel model, GhgNote note)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(note);

        if (!note.IsPlaceable)
        {
            return GhgEdit.Refused("A note needs a position, a width and a height.");
        }

        return body.Change(new ModelChange.Add(Note, note.Id, Values(
            ("text", Normalised(note.Text)),
            ("at", GhgScale.FormatMonth(note.At!.Value)),
            ("row", note.Row),
            ("width", note.Width!.Value),
            ("height", note.Height!.Value))));
    }

    private static Dictionary<string, object?> Values(params (string Attribute, object? Value)[] values) =>
        values.ToDictionary(value => value.Attribute, value => value.Value, StringComparer.Ordinal);

    /// <summary>Line breaks as LF, trailing ones dropped: FBL writes a multi-line value as a literal block in the body's own ending.</summary>
    private static string Normalised(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n');

    private static List<object?> TagList(IReadOnlyList<string> tags) => [.. tags];
}
