using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// The rows "Arrange diagram" gives a timeline: as few as its elements allow, with related
/// elements on rows close together - their times never change.
/// </summary>
/// <remarks>
/// <para>
/// <b>Across is time, so only rows move.</b> That makes the arrangement the shared
/// <see cref="RowPacking"/>, fed with what each element covers across as the canvas first draws it:
/// a period from its begin to its end, a moment as its diamond with its label beside it.
/// </para>
/// <para>
/// <b>The canvas's own first scale</b>: the client fits the whole timeline into 1200 units with a
/// tenth to spare either side (<c>timelineScaleOf</c>), and its labels are 12 units high, so the
/// widths here are the ones a reader sees at the fit - which is where clutter is judged.
/// </para>
/// </remarks>
public static class TimelineArrangement
{
    /// <summary>The width the client's first fit spreads the timeline's span over, with its margin.</summary>
    private const double FitWidth = 1200 / 1.2;

    /// <summary>The canvas's label size.</summary>
    private const double LabelFontSize = 12;

    /// <summary>A moment's diamond radius, and the space between it and its label.</summary>
    private const double MomentRadius = 9;
    private const double LabelGap = 6;

    /// <summary>The least clear space between two elements on one row.</summary>
    private const double Gap = 12;

    private const double Day = 86400;

    /// <summary>The row of every element that can be drawn, by id.</summary>
    public static IReadOnlyDictionary<string, int> RowsOf(TimelineModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var taken = new HashSet<string>(StringComparer.Ordinal);
        var elements = model.Elements
            .Where(element => element.Id.Length > 0 && element.Begin.IsReadable && taken.Add(element.Id))
            .ToList();
        if (elements.Count == 0)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        double Begin(TimelineElement element) => TimelineScale.ToSeconds(element.Begin.Value!.Value);
        double End(TimelineElement element) => element.End is { IsReadable: true } end
            ? Math.Max(Begin(element), TimelineScale.ToSeconds(end.Value!.Value))
            : Begin(element);

        var first = elements.Min(Begin);
        var span = Math.Max(elements.Max(End) - first, Day);
        var unitsPerSecond = FitWidth / span;

        RowItem ItemOf(TimelineElement element)
        {
            var left = (Begin(element) - first) * unitsPerSecond;
            if (element.End is { IsReadable: true })
            {
                return new RowItem(element.Id, left, Math.Max(left + 2, (End(element) - first) * unitsPerSecond));
            }

            var label = element.Label.Length > 0 ? element.Label : element.Id;
            return new RowItem(element.Id, left - MomentRadius, left + MomentRadius + LabelGap + TextMetric.WidthOf(label, LabelFontSize));
        }

        var links = model.Connections.Select(connection => (connection.From, connection.To)).ToList();
        return RowPacking.Pack([.. elements.Select(ItemOf)], links, Gap);
    }
}
