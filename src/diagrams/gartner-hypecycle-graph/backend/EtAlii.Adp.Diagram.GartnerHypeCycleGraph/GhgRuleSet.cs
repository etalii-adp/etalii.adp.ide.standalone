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

    /// <summary>The nine, in the design's order.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        DuplicateInfluence, SelfInfluence, StopBeforeStart, PhaseCount, BoundaryOrder,
        BadAttachment, DanglingReference, DuplicateId, UnreadableEntry,
    ];
}

/// <summary>
/// Requirement 2.4: a document that breaks the rules still opens, and every breach is reported.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reporting, never refusing.</b> A document edited outside ADP may say anything; this turns what
/// it says into findings rather than into an exception.
/// </para>
/// <para>
/// <b>One influence per DIRECTION, never per pair</b> (the user's ruling Q3, 2026-09-26): A -&gt; B
/// and B -&gt; A may both exist; a second A -&gt; B may not. The duplicate check groups by the
/// ORDERED pair, which is what task 16's guard is seen to fail against - an unordered grouping
/// reports the legitimate opposite pair.
/// </para>
/// <para>
/// <b>An influence hidden by a phase count counts like any other</b> (Requirement 7.3). Nothing here
/// reads what is drawn: the rules read the document, where a hidden influence is exactly as present
/// as a visible one.
/// </para>
/// </remarks>
public static class GhgRuleSet
{
    /// <summary>Every breach in the model, in rule order then document order.</summary>
    public static IReadOnlyList<GhgBreach> Breaches(GhgModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var trendIds = model.Trends
            .Where(trend => trend.Id.Length > 0)
            .Select(trend => trend.Id)
            .ToHashSet(StringComparer.Ordinal);

        List<GhgBreach> breaches = [];
        breaches.AddRange(DuplicateInfluences(model));
        breaches.AddRange(SelfInfluences(model));
        breaches.AddRange(Spans(model));
        breaches.AddRange(PhaseCounts(model));
        breaches.AddRange(BoundaryOrders(model));
        breaches.AddRange(BadAttachments(model));
        breaches.AddRange(DanglingReferences(model, trendIds));
        breaches.AddRange(DuplicateIds(model));
        breaches.AddRange(model.Problems.Select(problem => new GhgBreach(GhgRuleIds.UnreadableEntry, problem.Message, [], problem.Line)));
        return breaches;
    }

    /// <summary>Whether a new influence from <paramref name="from"/> to <paramref name="to"/> would repeat one in the same direction.</summary>
    public static bool AlreadyInfluences(GhgModel model, string from, string to)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Influences.Any(influence =>
            string.Equals(influence.From, from, StringComparison.Ordinal) &&
            string.Equals(influence.To, to, StringComparison.Ordinal));
    }

    private static IEnumerable<GhgBreach> DuplicateInfluences(GhgModel model) =>
        model.Influences
            .Where(influence => influence.From.Length > 0 && influence.To.Length > 0)
            .GroupBy(influence => (influence.From, influence.To))
            .Where(group => group.Count() > 1)
            .Select(group => new GhgBreach(
                GhgRuleIds.DuplicateInfluence,
                $"`{group.Key.From}` influences `{group.Key.To}` {group.Count()} times; a trend influences another once in each direction.",
                [.. group.Select(influence => influence.Id)],
                group.Skip(1).First().Range.Start));

    private static IEnumerable<GhgBreach> SelfInfluences(GhgModel model) =>
        model.Influences
            .Where(influence => influence.From.Length > 0 && string.Equals(influence.From, influence.To, StringComparison.Ordinal))
            .Select(influence => new GhgBreach(
                GhgRuleIds.SelfInfluence,
                $"`{influence.Id}` has `{influence.From}` influence itself; a trend cannot.",
                [influence.Id, influence.From],
                influence.Range.Start));

    private static IEnumerable<GhgBreach> Spans(GhgModel model) =>
        model.Trends
            .Where(trend => trend.Start is not null && trend.Stop is not null && trend.Stop <= trend.Start)
            .Select(trend => new GhgBreach(
                GhgRuleIds.StopBeforeStart,
                $"`{trend.Id}` stops at {GhgScale.FormatMonth(trend.Stop!.Value)}, not after it starts at {GhgScale.FormatMonth(trend.Start!.Value)}; a trend is at least a month long.",
                [trend.Id],
                trend.Range.Start));

    private static IEnumerable<GhgBreach> PhaseCounts(GhgModel model) =>
        model.Trends
            .Where(trend => trend.Phases is < 1 or > GhgPhases.Count)
            .Select(trend => new GhgBreach(
                GhgRuleIds.PhaseCount,
                $"`{trend.Id}` shows {trend.Phases} phases; a trend shows 1 to {GhgPhases.Count}.",
                [trend.Id],
                trend.Range.Start));

    /// <summary>Stored boundaries must lie strictly inside the span, and strictly in phase order.</summary>
    private static IEnumerable<GhgBreach> BoundaryOrders(GhgModel model)
    {
        foreach (var trend in model.Trends.Where(trend => trend.HasSpan))
        {
            var previous = trend.Start!.Value;
            for (var index = 0; index < trend.DraggedEnds.Count; index++)
            {
                if (trend.DraggedEnds[index] is not { } boundary)
                {
                    continue;
                }

                if (boundary <= previous || boundary >= trend.Stop!.Value)
                {
                    yield return new GhgBreach(
                        GhgRuleIds.BoundaryOrder,
                        $"`{trend.Id}`'s `{GhgPhases.BoundaryKeys[index]}: {GhgScale.FormatMonth(boundary)}` is out of order or outside {GhgScale.FormatMonth(trend.Start.Value)} to {GhgScale.FormatMonth(trend.Stop!.Value)}.",
                        [trend.Id],
                        trend.Range.Start);
                    break;
                }

                previous = boundary;
            }
        }
    }

    private static IEnumerable<GhgBreach> BadAttachments(GhgModel model)
    {
        foreach (var influence in model.Influences)
        {
            foreach (var (side, end) in new[] { ("from", influence.FromEnd), ("to", influence.ToEnd) })
            {
                if (!end.IsReadable)
                {
                    yield return new GhgBreach(
                        GhgRuleIds.BadAttachment,
                        $"`{influence.Id}`'s {side} end ({end}) does not name a phase, a top or bottom edge, and an `at` from 0 to 1.",
                        [influence.Id],
                        influence.Range.Start);
                }
            }
        }
    }

    private static IEnumerable<GhgBreach> DanglingReferences(GhgModel model, HashSet<string> trendIds)
    {
        foreach (var influence in model.Influences)
        {
            foreach (var id in new[] { influence.From, influence.To }.Where(id => !trendIds.Contains(id)))
            {
                yield return new GhgBreach(
                    GhgRuleIds.DanglingReference,
                    $"`{influence.Id}` names `{id}`, which is not a trend in this document.",
                    [influence.Id, id],
                    influence.Range.Start);
            }
        }
    }

    private static IEnumerable<GhgBreach> DuplicateIds(GhgModel model) =>
        model.Trends.Select(trend => (trend.Id, trend.Range.Start))
            .Concat(model.Influences.Select(influence => (influence.Id, influence.Range.Start)))
            .Where(entry => entry.Id.Length > 0)
            .GroupBy(entry => entry.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => new GhgBreach(
                GhgRuleIds.DuplicateId,
                $"`{group.Key}` is declared {group.Count()} times; an id names one entry.",
                [group.Key],
                group.Last().Start));
}
