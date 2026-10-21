using System.Globalization;
using System.Text.RegularExpressions;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// A date, a date and time or a time: compared and sorted as the point it is, and written in
/// ISO 8601. The three differ only in what that point is and how it is written.
/// </summary>
internal abstract class KnowledgeMomentType(string name) : KnowledgeValueType(name, name, ["is", "is-before", "is-after", "is-on-or-before", "is-on-or-after"])
{
    /// <summary>A value of this type as a point to compare, or null for what is not one.</summary>
    public abstract override long? Moment(string value);

    protected override bool HoldsGiven(string comparison, IReadOnlyList<string> values, string given)
    {
        if (Moment(given) is not { } asked)
        {
            return true;
        }

        if (Moment(First(values)) is not { } moment)
        {
            return false;
        }

        return comparison switch
        {
            "is" => moment == asked,
            "is-before" => moment < asked,
            "is-after" => moment > asked,
            "is-on-or-before" => moment <= asked,
            "is-on-or-after" => moment >= asked,
            _ => true,
        };
    }

    public override KnowledgeSortKey SortKeyOf(KnowledgeProperty property, string value) =>
        Moment(value) is { } moment ? new KnowledgeSortKey(moment, "") : KnowledgeSortKey.Empty;

    public override void ConvertTo(KnowledgeValueType to, KnowledgeConversion conversion) => ToText(to, conversion);

    public override void FromText(string value, KnowledgeConversion conversion) => FromWritten(value, conversion);
}

/// <summary>A date, as year-month-day: its point is the day.</summary>
internal sealed class KnowledgeDateType() : KnowledgeMomentType("date")
{
    public const string Expected = "A date is expected, as year-month-day.";

    public override KnowledgeFigures Figures => KnowledgeFigures.Dates;

    public override long? Moment(string value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date.DayNumber : null;

    /// <summary>Days apart, as a date's points are days.</summary>
    public override TimeSpan Between(long earliest, long latest) => TimeSpan.FromDays(latest - earliest);

    public override string Write(string value, out object? written)
    {
        written = value;
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return Expected;
        }

        written = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return "";
    }
}

/// <summary>
/// A date and a time, carrying its offset: its point is the instant, in UTC. One given without an
/// offset is taken to be this machine's local time.
/// </summary>
internal sealed partial class KnowledgeDateTimeType() : KnowledgeMomentType("dateTime")
{
    public const string Expected = "A date and a time are expected.";

    [GeneratedRegex(@"(Z|[+-]\d\d:\d\d)$")]
    private static partial Regex Offset();

    public override KnowledgeFigures Figures => KnowledgeFigures.Dates;

    public override long? Moment(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var instant) ? instant.UtcTicks : null;

    public override string Write(string value, out object? written)
    {
        written = value;
        if (Offset().IsMatch(value))
        {
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _) ? "" : Expected;
        }

        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return Expected;
        }

        written = new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeZoneInfo.Local.GetUtcOffset(local)).ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
        return "";
    }
}

/// <summary>A time of day, as hours:minutes, with the seconds where there are any: its point is the time of day.</summary>
internal sealed class KnowledgeTimeType() : KnowledgeMomentType("time")
{
    public const string Expected = "A time is expected, as hours:minutes.";

    public override long? Moment(string value) =>
        TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time.Ticks : null;

    public override string Write(string value, out object? written)
    {
        written = value;
        if (!TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return Expected;
        }

        written = time.ToString(time.Second == 0 ? "HH:mm" : "HH:mm:ss", CultureInfo.InvariantCulture);
        return "";
    }
}
