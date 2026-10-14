namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>The stable identifiers of this module's rules, prefixed with its short name.</summary>
/// <remarks>
/// The codes are the definition's (<c>timeline.dis</c>, <c>constraints</c>); they are named here because
/// <see cref="TimelineDefinition"/> groups the findings by them, which <c>constraints.order</c> cannot
/// state, and reports the two id findings at a line rather than at an element (<c>timeline.md</c>,
/// <i>What stays code</i>).
/// </remarks>
public static class TimelineRules
{
    /// <summary>An element ends before it begins - readable from a hand-edited file, creatable by nothing in the application (Requirement 3.4).</summary>
    public const string EndBeforeBegin = "timeline.end-before-begin";

    /// <summary>A begin or end that is not a time this module can read.</summary>
    public const string UnreadableTime = "timeline.unreadable-time";

    /// <summary>A connection naming an element that does not exist.</summary>
    public const string DanglingConnection = "timeline.dangling-connection";

    /// <summary>One element mixing a date-only value with a date-time one (Requirement 3.2).</summary>
    public const string MixedPrecision = "timeline.mixed-precision";

    /// <summary>Two declarations sharing one id, which makes every reference to it ambiguous.</summary>
    public const string DuplicateId = "timeline.duplicate-id";

    /// <summary>An element with no id at all, which nothing can select, connect or edit.</summary>
    public const string MissingId = "timeline.missing-id";
}
