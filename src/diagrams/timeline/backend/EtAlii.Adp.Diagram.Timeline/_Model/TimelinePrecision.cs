namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>How finely an author wrote a moment in time.</summary>
/// <remarks>
/// Carried alongside the value because a date and a midnight date-time are the same instant and
/// a different statement. Requirement 3.2 forbids mixing them within one element, and a drag
/// must give back what it was handed - so the precision travels with the value rather than being
/// re-guessed by whoever formats it.
/// </remarks>
public enum TimelinePrecision
{
    /// <summary>A whole day: <c>2026-01-05</c>.</summary>
    Date,

    /// <summary>A time of day as well: <c>2026-01-05T14:30:00</c>.</summary>
    DateTime,
}
