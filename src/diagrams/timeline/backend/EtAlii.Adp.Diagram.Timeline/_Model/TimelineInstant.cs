namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// A begin or an end, as the author wrote it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Value"/> is null when <see cref="Text"/> is not a time this module can read. That
/// is deliberately not an exception: Requirement 12.2 says an unreadable time is a warning
/// naming the element while the rest of the diagram still draws, so the parser records the
/// problem and the rule set reports it. A parser that threw would take the whole document with
/// one bad element.
/// </para>
/// <para>
/// <see cref="Text"/> is kept because the wire payload carries the value <em>as written</em>, and
/// because a writer that reformatted <c>2026-01-05</c> into <c>2026-01-05T00:00:00</c> on an
/// unrelated edit would breach Requirement 2.2.
/// </para>
/// </remarks>
/// <param name="Text">Exactly what the document said.</param>
/// <param name="Value">The instant, or null when <paramref name="Text"/> could not be read.</param>
/// <param name="Precision">How finely it was written.</param>
public sealed record TimelineInstant(string Text, DateTimeOffset? Value, TimelinePrecision Precision)
{
    /// <summary>Whether this is a time the module could actually read.</summary>
    public bool IsReadable => Value is not null;
}
