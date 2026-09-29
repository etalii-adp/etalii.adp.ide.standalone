using Google.Protobuf;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// A <c>.ghg</c> model as the library's elements: trends as arrow banners, triggers as circles,
/// notes as boxes of text, and influences as the connections from a trend or trigger to a trend.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is sent</b>: the type, the trend's centre, and a payload carrying its name, its visible
/// phase count, its drawn boundaries as fractions of its width, its tags and its width. The
/// boundaries are <see cref="GhgPhases.BoundariesOf(GhgTrend)"/>'s, so the client draws what it is
/// given and never recomputes the rule.
/// </para>
/// <para>
/// <b>A Description is never sent</b> (Requirement 12.3). Neither payload has a field for one, so a
/// change to a Description alone changes nothing sent and raises no delta.
/// </para>
/// <para>
/// <b>An influence on a hidden phase is sent unchanged</b> (Requirement 7.2). Hiding it is the
/// canvas's job, from the attachment's region and the trend's phase count; a mapper that dropped it
/// would also take it out of the canvas's model, where the one-per-direction check reads it
/// (Requirement 7.3). That is the defect task 17's guard is seen to fail against.
/// </para>
/// <para>
/// <b>What cannot be drawn is left out, and what breaks a rule is still drawn</b>, as in FDG: a trend
/// without a readable span, an influence whose end is not a drawable trend, and every later entry
/// reusing an id already drawn (the shared diff throws on an id that appears twice).
/// </para>
/// </remarks>
public sealed class GhgElementMapper
{
    private const string Prefix = "gartner/hypecycle-graph+";

    /// <summary>The library type of a trend.</summary>
    public const string TrendType = Prefix + "trend";

    /// <summary>The library type of a trigger.</summary>
    public const string TriggerType = Prefix + "trigger";

    /// <summary>The library type of a note.</summary>
    public const string NoteType = Prefix + "note";

    /// <summary>The library type of an influence.</summary>
    public const string InfluenceType = Prefix + "influence";

    /// <summary>
    /// The elements and connections a view of <paramref name="viewport"/> should hold: every trend,
    /// trigger and note that overlaps it, and every influence whose span does, together with its two ends.
    /// </summary>
    public IReadOnlyList<DiagramElement> Visible(GhgModel model, DiagramViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(model);

        var (trends, triggers, notes, influences) = Drawable(model);
        var unit = model.TimeUnit;
        var bounds = new Dictionary<string, Box>(StringComparer.Ordinal);
        foreach (var trend in trends)
        {
            bounds[trend.Id] = Bounds(trend, unit);
        }

        foreach (var trigger in triggers)
        {
            bounds[trigger.Id] = Bounds(trigger, unit);
        }

        var shownIds = bounds
            .Where(entry => Overlaps(entry.Value, viewport))
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.Ordinal);

        var shownInfluences = influences
            .Where(influence => Overlaps(Union(bounds[influence.From], bounds[influence.To]), viewport))
            .ToArray();

        foreach (var influence in shownInfluences)
        {
            shownIds.Add(influence.From);
            shownIds.Add(influence.To);
        }

        return
        [
            .. trends.Where(trend => shownIds.Contains(trend.Id)).Select(trend => Trend(trend, unit)),
            .. triggers.Where(trigger => shownIds.Contains(trigger.Id)).Select(trigger => Trigger(trigger, unit)),
            .. notes.Where(note => Overlaps(Bounds(note, unit), viewport)).Select(note => Note(note, unit)),
            .. shownInfluences.Select(Influence),
        ];
    }

    /// <summary>
    /// What can be drawn: an element needs an id nothing drawn earlier took and a readable position;
    /// an influence needs a drawable trend or trigger at its source and a drawable trend at its target.
    /// </summary>
    private static (IReadOnlyList<GhgTrend> Trends, IReadOnlyList<GhgTrigger> Triggers, IReadOnlyList<GhgNote> Notes, IReadOnlyList<GhgInfluence> Influences) Drawable(GhgModel model)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);

        var trends = model.Trends
            .Where(trend => trend.Id.Length > 0 && trend.HasSpan && taken.Add(trend.Id))
            .ToList();
        var triggers = model.Triggers
            .Where(trigger => trigger.Id.Length > 0 && trigger.Date is not null && taken.Add(trigger.Id))
            .ToList();
        var notes = model.Notes
            .Where(note => note.Id.Length > 0 && note.IsPlaceable && taken.Add(note.Id))
            .ToList();

        var trendIds = trends.Select(trend => trend.Id).ToHashSet(StringComparer.Ordinal);
        var sourceIds = trendIds.Concat(triggers.Select(trigger => trigger.Id)).ToHashSet(StringComparer.Ordinal);
        var influences = model.Influences
            .Where(influence => influence.Id.Length > 0
                && sourceIds.Contains(influence.From)
                && trendIds.Contains(influence.To)
                && taken.Add(influence.Id))
            .ToList();

        return (trends, triggers, notes, influences);
    }

    private readonly record struct Box(double MinX, double MinY, double MaxX, double MaxY);

    private static Box Bounds(GhgTrend trend, GhgTimeUnit unit)
    {
        var left = GhgScale.XOf(trend.Start!.Value, unit);
        var top = GhgScale.TopOf(trend.Row);
        return new Box(left, top, left + GhgScale.WidthOf(trend.Months, unit), top + GhgScale.TrendHeight);
    }

    private static Box Bounds(GhgTrigger trigger, GhgTimeUnit unit)
    {
        var (x, y) = CentreOf(trigger, unit);
        var half = GhgScale.TriggerSize / 2;
        return new Box(x - half, y - half, x + half, y + half);
    }

    private static Box Bounds(GhgNote note, GhgTimeUnit unit)
    {
        var left = GhgScale.XOf(note.At!.Value, unit);
        var top = GhgScale.TopOf(note.Row);
        return new Box(left, top, left + note.Width!.Value, top + note.Height!.Value);
    }

    /// <summary>A trigger's centre: the start of its month, and its row's middle.</summary>
    private static (double X, double Y) CentreOf(GhgTrigger trigger, GhgTimeUnit unit) =>
        (GhgScale.XOf(trigger.Date!.Value, unit), GhgScale.TopOf(trigger.Row) + (GhgScale.TrendHeight / 2));

    private static Box Union(Box a, Box b) =>
        new(Math.Min(a.MinX, b.MinX), Math.Min(a.MinY, b.MinY), Math.Max(a.MaxX, b.MaxX), Math.Max(a.MaxY, b.MaxY));

    private static bool Overlaps(Box box, DiagramViewport viewport) =>
        box.MaxX >= viewport.MinX && box.MinX <= viewport.MaxX && box.MaxY >= viewport.MinY && box.MinY <= viewport.MaxY;

    private static DiagramElement Trend(GhgTrend trend, GhgTimeUnit unit)
    {
        var width = GhgScale.WidthOf(trend.Months, unit);
        var payload = new GhgTrendPayload
        {
            Name = trend.Name,
            Phases = trend.VisiblePhases,
            Width = width,
            Unit = unit.Name,
        };
        payload.Boundaries.AddRange(GhgPhases.FractionsOf(trend));
        payload.Tags.AddRange(trend.Tags);

        // The document holds the start month and the row; the library draws from the centre.
        return Pack(
            trend.Id,
            GhgScale.XOf(trend.Start!.Value, unit) + (width / 2),
            GhgScale.TopOf(trend.Row) + (GhgScale.TrendHeight / 2),
            TrendType,
            payload);
    }

    private static DiagramElement Trigger(GhgTrigger trigger, GhgTimeUnit unit)
    {
        var payload = new GhgTriggerPayload
        {
            Name = trigger.Name,
            Unit = unit.Name,
            When = GhgScale.FormatWhen(trigger.Date!.Value, unit),
            WhenLong = GhgScale.FormatWhenLong(trigger.Date!.Value, unit),
            // The canvas snaps a leading edge to origin + k * step; these put the CENTRE on the lines.
            SnapX = -GhgScale.TriggerSize / 2,
            SnapY = (GhgScale.TrendHeight - GhgScale.TriggerSize) / 2,
        };
        payload.Tags.AddRange(trigger.Tags);

        var (x, y) = CentreOf(trigger, unit);
        return Pack(trigger.Id, x, y, TriggerType, payload);
    }

    private static DiagramElement Note(GhgNote note, GhgTimeUnit unit)
    {
        var payload = new GhgNotePayload { Text = note.Text, Width = note.Width!.Value, Height = note.Height!.Value };
        var box = Bounds(note, unit);
        return Pack(note.Id, (box.MinX + box.MaxX) / 2, (box.MinY + box.MaxY) / 2, NoteType, payload);
    }

    private static DiagramElement Influence(GhgInfluence influence)
    {
        var payload = new GhgInfluencePayload
        {
            FromElementId = influence.From,
            ToElementId = influence.To,
            SourceAttachment = Attachment(influence.FromEnd),
            TargetAttachment = Attachment(influence.ToEnd),
        };

        return Pack(influence.Id, 0d, 0d, InfluenceType, payload);
    }

    /// <summary>The wire attachment, or null for an end the document states unreadably.</summary>
    private static GhgAttachment? Attachment(GhgEnd end) =>
        end.IsReadable
            ? new GhgAttachment { Edge = end.Edge, Region = end.PhaseIndex, At = end.At!.Value }
            : null;

    private static DiagramElement Pack(string id, double x, double y, string type, IMessage payload) =>
        new(id, x, y, type, $"type.googleapis.com/{payload.Descriptor.FullName}", payload.ToByteArray());
}
