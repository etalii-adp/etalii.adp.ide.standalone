using System.Globalization;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// The CEL functions of the definition's <c>net.etalii.adp.generic.timelineTimes</c> plugin (DISL §13.1.1),
/// implemented with the calls the module itself reads and writes times with, so a rule, a form check
/// and a toolbox drop judge a time exactly as the parser and the writer do.
/// </summary>
/// <remarks>
/// <para>
/// <b>DISL's CEL has no timestamps</b>, and the timeline keeps every time as the text it was written
/// with; reading one is .NET's <see cref="DateTimeOffset.TryParse(string, IFormatProvider, DateTimeStyles, out DateTimeOffset)"/>
/// under the invariant culture, offset zero, which accepts more than ISO 8601. The definition's
/// fallbacks only approximate that, so the module always supplies these.
/// </para>
/// <para>
/// <b>An end as written</b> (<c>writtenEnd</c>) is the <c>from</c> or <c>to</c> text the model keeps
/// beside a relation (<see cref="TimelineDisl"/>), because a relation end that names no element is
/// null in the model and CEL could not otherwise say what it named.
/// </para>
/// </remarks>
internal static class TimelineTimes
{
    /// <summary>The implementations, for <see cref="BundledDefinition.Load(System.Reflection.Assembly, string, DislPluginFunctions)"/>.</summary>
    public static DislPluginFunctions Plugins() => new DislPluginFunctions()
        .Add("readableTime", arguments => Read(Text(arguments[0])) is not null)
        .Add("compareTimes", arguments => (long)Readable(arguments[0]).CompareTo(Readable(arguments[1])))
        .Add("dateAfter", arguments => TimelineScale.ToText(Readable(arguments[0]).AddDays(Days(arguments[1])), TimelinePrecision.Date))
        .Add("today", _ => TimelineScale.ToText(DateTimeOffset.UtcNow, TimelinePrecision.Date))
        .Add("writtenEnd", arguments => WrittenEnd(arguments[0], Text(arguments[1])));

    /// <summary>The instant <paramref name="text"/> names, read as the parser reads a time: trimmed, at face value, offset zero; null when it will not read.</summary>
    public static DateTimeOffset? Read(string text) => TimelineInstants.Parse(text.Trim());

    private static DateTimeOffset Readable(object? argument) =>
        Read(Text(argument)) ?? throw new CelException($"'{Text(argument)}' is not a time this timeline can read.");

    private static long Days(object? argument) =>
        argument is long days ? days : throw new CelException("dateAfter() takes a whole number of days.");

    private static string Text(object? argument) =>
        argument as string ?? throw new CelException("A timeline time function takes text.");

    private static string WrittenEnd(object? relation, string end) =>
        relation is DislElement element && element.HostAttributes.TryGetValue(end == "source" ? TimelineDisl.WrittenFrom : TimelineDisl.WrittenTo, out var written)
            ? written as string ?? ""
            : throw new CelException("writtenEnd() takes a relation of this timeline and 'source' or 'target'.");
}
