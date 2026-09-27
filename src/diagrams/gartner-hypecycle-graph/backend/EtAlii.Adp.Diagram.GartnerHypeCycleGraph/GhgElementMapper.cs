using Google.Protobuf;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// A <c>.ghg</c> model as the library's elements: trends as arrow banners, influences as the
/// connections between them.
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

    /// <summary>The library type of an influence.</summary>
    public const string InfluenceType = Prefix + "influence";

    /// <summary>
    /// The elements and connections a view of <paramref name="viewport"/> should hold: every trend
    /// that overlaps it, and every influence whose span does, together with its two ends.
    /// </summary>
    public IReadOnlyList<DiagramElement> Visible(GhgModel model, DiagramViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(model);

        var (trends, influences) = Drawable(model);
        var byId = trends.ToDictionary(trend => trend.Id, StringComparer.Ordinal);
        var unit = model.TimeUnit;

        var shownIds = trends
            .Where(trend => Overlaps(Bounds(trend, unit), viewport))
            .Select(trend => trend.Id)
            .ToHashSet(StringComparer.Ordinal);

        var shownInfluences = influences
            .Where(influence => Overlaps(Union(Bounds(byId[influence.From], unit), Bounds(byId[influence.To], unit)), viewport))
            .ToArray();

        foreach (var influence in shownInfluences)
        {
            shownIds.Add(influence.From);
            shownIds.Add(influence.To);
        }

        return
        [
            .. trends.Where(trend => shownIds.Contains(trend.Id)).Select(trend => Trend(trend, unit)),
            .. shownInfluences.Select(Influence),
        ];
    }

    private static (IReadOnlyList<GhgTrend> Trends, IReadOnlyList<GhgInfluence> Influences) Drawable(GhgModel model)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);

        var trends = model.Trends
            .Where(trend => trend.Id.Length > 0 && trend.HasSpan && taken.Add(trend.Id))
            .ToList();

        var trendIds = trends.Select(trend => trend.Id).ToHashSet(StringComparer.Ordinal);
        var influences = model.Influences
            .Where(influence => influence.Id.Length > 0
                && trendIds.Contains(influence.From)
                && trendIds.Contains(influence.To)
                && taken.Add(influence.Id))
            .ToList();

        return (trends, influences);
    }

    private readonly record struct Box(double MinX, double MinY, double MaxX, double MaxY);

    private static Box Bounds(GhgTrend trend, GhgTimeUnit unit)
    {
        var left = GhgScale.XOf(trend.Start!.Value, unit);
        var top = GhgScale.TopOf(trend.Row);
        return new Box(left, top, left + GhgScale.WidthOf(trend.Months, unit), top + GhgScale.TrendHeight);
    }

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
