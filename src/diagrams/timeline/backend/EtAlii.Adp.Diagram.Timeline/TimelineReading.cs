using System.Globalization;
using System.Runtime.CompilerServices;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// The binding's reading of a body as the <see cref="TimelineModel"/> the module's host code reads:
/// the element mapper, the session, the scale, the arrangement and the commands keep reading what
/// they read before the body was read through FBL, so their answers do not change.
/// </summary>
/// <remarks>
/// <para>
/// <b>Per element</b>: the id it is written with (the binding's <c>storedId</c>, empty for none), its
/// label, its begin and, for a period, its end, each read as a time at face value, its row, and the
/// lines its entry occupies. <b>Per relation</b>: its id, its <c>from</c> and <c>to</c> as written, its
/// label and its lines. Every value is the text it was written with; a missing one is the empty text.
/// </para>
/// <para>
/// <b>A row is .NET's integer parse of its text</b>, as the tool has always read it: <c>"3"</c> and
/// <c>+4</c> are rows, <c>1.0</c> and <c>0x10</c> are row 0 (finding B13).
/// </para>
/// <para>
/// <b>An entry's lines are the lines of the span the library read it from</b>, which ends at the
/// entry's last line of content, so a comment or a blank line after it is not its own: the ranges the
/// commands have always spliced on (<see cref="YamlNodeRange"/>), as <c>TimelineReadingTests</c> holds
/// for the whole corpus.
/// </para>
/// <para>
/// <b>Each model remembers the body it was read from</b>, so <see cref="TimelineDefinition"/> derives
/// a menu, a row and a finding from the same reading the model was projected from.
/// </para>
/// </remarks>
public static class TimelineReading
{
    private static readonly ConditionalWeakTable<TimelineModel, TimelineBody> Bodies = [];

    /// <summary>The model of <paramref name="text"/>; empty for a body the library cannot read.</summary>
    public static TimelineModel Of(string text) => Of(TimelineBody.Parse(text));

    /// <summary>The model of what <paramref name="body"/> reads; empty for a body the library cannot read.</summary>
    internal static TimelineModel Of(TimelineBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var reading = body.Model;
        if (reading.Unreadable) return TimelineModel.Empty;

        var elements = new List<TimelineElement>();
        var connections = new List<TimelineConnection>();
        foreach (var element in reading.Elements.OrderBy(element => element.OwnSpan.Start))
        {
            var range = body.LinesOf(element.OwnSpan);
            switch (element.Rule)
            {
                case "period":
                case "moment":
                    elements.Add(new TimelineElement(
                        TextOf(element, "storedId"),
                        TextOf(element, "label"),
                        Instant(TextOf(element, "begin")),
                        element.Rule == "period" ? Instant(TextOf(element, "end")) : null,
                        Row(TextOf(element, "row")),
                        range));
                    break;
                case "connection":
                    connections.Add(new TimelineConnection(
                        TextOf(element, "storedId"),
                        TextOf(element, "from"),
                        TextOf(element, "to"),
                        TextOf(element, "label"),
                        range));
                    break;
            }
        }

        var model = new TimelineModel(elements, connections);
        Bodies.AddOrUpdate(model, body);
        return model;
    }

    /// <summary>The body <paramref name="model"/> was read from; null for one no body was read into, such as <see cref="TimelineModel.Empty"/>.</summary>
    internal static TimelineBody? BodyOf(TimelineModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Bodies.TryGetValue(model, out var body) ? body : null;
    }

    /// <summary>The text an attribute was written with; empty when it is not written or is no scalar.</summary>
    internal static string TextOf(FblElement element, string attribute) => element.Attributes.GetValueOrDefault(attribute) switch
    {
        string text => text,
        long or double or bool => Convert.ToString(element.Attributes[attribute], CultureInfo.InvariantCulture) ?? "",
        _ => "",
    };

    /// <summary>
    /// Reads a time at face value, as the tool always has: no timezone conversion, no calendar, offset
    /// zero; its precision is decided by what was written, so midnight written as a date stays a date.
    /// </summary>
    private static TimelineInstant Instant(string text)
    {
        var trimmed = text.Trim();
        var precision = trimmed.Contains('T', StringComparison.Ordinal) ? TimelinePrecision.DateTime : TimelinePrecision.Date;
        return new TimelineInstant(text, TimelineInstants.Parse(trimmed), precision);
    }

    /// <summary>A row that will not read is row zero, and the rules say so.</summary>
    private static int Row(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var row) ? row : 0;
}
