using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// The rows "Arrange diagram" gives a hype cycle graph: as few as its trends, triggers and notes
/// allow, with influenced trends on rows close together - no date ever changes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Across is time, so only rows move.</b> That makes the arrangement the shared
/// <see cref="RowPacking"/>, fed with what each element covers across as the canvas draws it: a
/// trend's banner with its name before it, a trigger's circle with its name and date before it, a
/// note's box over as many rows as it is tall.
/// </para>
/// <para>
/// <b>The influences are the links</b>, so a trend sits near what it influences and the curves stay
/// short; a note is linked to nothing and fills whatever room is left.
/// </para>
/// </remarks>
public static class GhgArrangement
{
    /// <summary>The canvas's label size, and the space between a label and what it names.</summary>
    private const double LabelFontSize = 12;
    private const double LabelGap = 8;

    /// <summary>The least clear space between two elements on one row.</summary>
    private const double Gap = 16;

    /// <summary>The row of every element that can be drawn, by id.</summary>
    public static IReadOnlyDictionary<string, int> RowsOf(GhgModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var unit = model.TimeUnit;
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<RowItem>();

        foreach (var trend in model.Trends.Where(trend => trend.Id.Length > 0 && trend.HasSpan && taken.Add(trend.Id)))
        {
            var left = GhgScale.XOf(trend.Start!.Value, unit);
            items.Add(new RowItem(
                trend.Id,
                left - LabelGap - TextMetric.WidthOf(trend.Name, LabelFontSize),
                left + GhgScale.WidthOf(trend.Months, unit)));
        }

        foreach (var trigger in model.Triggers.Where(trigger => trigger.Id.Length > 0 && trigger.Date is not null && taken.Add(trigger.Id)))
        {
            var centre = GhgScale.XOf(trigger.Date!.Value, unit);
            var half = GhgScale.TriggerSize / 2;
            var label = $"{trigger.Name} · {GhgScale.FormatWhen(trigger.Date!.Value, unit)}";
            items.Add(new RowItem(trigger.Id, centre - half - LabelGap - TextMetric.WidthOf(label, LabelFontSize), centre + half));
        }

        foreach (var note in model.Notes.Where(note => note.Id.Length > 0 && note.IsPlaceable && taken.Add(note.Id)))
        {
            var left = GhgScale.XOf(note.At!.Value, unit);
            items.Add(new RowItem(note.Id, left, left + note.Width!.Value, (int)Math.Ceiling(note.Height!.Value / GhgScale.RowStep)));
        }

        var links = model.Influences.Select(influence => (influence.From, influence.To)).ToList();
        return RowPacking.Pack(items, links, Gap);
    }
}
