namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>One breach, named by rule and by the entries involved.</summary>
/// <param name="RuleId">The rule broken, one of <see cref="GhgRuleIds"/>.</param>
/// <param name="Message">What is wrong, in a sentence meant for the Errors and Warnings panel.</param>
/// <param name="Entries">The ids involved, so the panel can point at them rather than at a line.</param>
/// <param name="Line">The zero-based line to point at.</param>
public sealed record GhgBreach(string RuleId, string Message, IReadOnlyList<string> Entries, int Line);

/// <summary>The rule ids the design's table names, stated once.</summary>
public static class GhgRuleIds
{
    public const string DuplicateInfluence = "ghg.duplicate-influence";
    public const string SelfInfluence = "ghg.self-influence";
    public const string StopBeforeStart = "ghg.stop-before-start";
    public const string PhaseCount = "ghg.phase-count";
    public const string BoundaryOrder = "ghg.boundary-order";
    public const string BadAttachment = "ghg.bad-attachment";
    public const string DanglingReference = "ghg.dangling-reference";
    public const string DuplicateId = "ghg.duplicate-id";
    public const string UnreadableEntry = "ghg.unreadable-entry";
    public const string InfluenceIntoTrigger = "ghg.influence-into-trigger";
    public const string TriggerDate = "ghg.trigger-date";
    public const string NotePosition = "ghg.note-position";

    /// <summary>The twelve: the nine of the design's order, then the three triggers and notes added.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        DuplicateInfluence, SelfInfluence, StopBeforeStart, PhaseCount, BoundaryOrder,
        BadAttachment, DanglingReference, DuplicateId, UnreadableEntry,
        InfluenceIntoTrigger, TriggerDate, NotePosition,
    ];
}
