namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>One breach, named by rule and by the elements involved.</summary>
/// <param name="RuleId">The rule broken, one of <see cref="FdgRuleIds"/>.</param>
/// <param name="Message">What is wrong, in a sentence meant for the Errors and Warnings panel.</param>
/// <param name="Elements">The ids involved, so the panel can point at them rather than at a line.</param>
/// <param name="Line">The zero-based line to point at.</param>
public sealed record FdgBreach(string RuleId, string Message, IReadOnlyList<string> Elements, int Line);

/// <summary>The rule ids the design's table names, stated once.</summary>
public static class FdgRuleIds
{
    public const string ForbiddenLink = "fdg.forbidden-link";
    public const string SelfLink = "fdg.self-link";
    public const string SecondParent = "fdg.second-parent";
    public const string SecondShows = "fdg.second-shows";
    public const string OwnershipCycle = "fdg.ownership-cycle";
    public const string DanglingReference = "fdg.dangling-reference";
    public const string DuplicateId = "fdg.duplicate-id";
    public const string UnreadableEntry = "fdg.unreadable-entry";
}

/// <summary>
/// Requirement 5.5: a document that breaks the rules still opens, and every breach is reported.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reporting, never refusing.</b> A document edited outside ADP may say anything; this turns
/// what it says into findings rather than into an exception, which is what lets an author open a
/// file, see what is wrong with it, and fix it in place.
/// </para>
/// <para>
/// <b>Each breach is checked independently, and one connection may break several.</b> A link from
/// a Comment to itself naming an unknown relation is a forbidden link AND a self link: reporting
/// only the first found would have the author fix one and meet the next on the following open.
/// </para>
/// </remarks>
public static class FdgRuleSet
{
    /// <summary>Every breach in the model, in rule order then document order.</summary>
    public static IReadOnlyList<FdgBreach> Breaches(FdgModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        List<FdgBreach> breaches = [];
        var byId = model.Elements
            .Where(element => !string.IsNullOrEmpty(element.Id))
            .GroupBy(element => element.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        breaches.AddRange(DuplicateIds(model));
        breaches.AddRange(UnreadableEntries(model));
        breaches.AddRange(Links(model, byId));
        breaches.AddRange(Cardinality(model));
        breaches.AddRange(Cycles(model));

        return breaches;
    }

    private static IEnumerable<FdgBreach> DuplicateIds(FdgModel model)
    {
        var ids = model.Elements.Select(element => (element.Id, element.Range.Start))
            .Concat(model.Connections.Select(connection => (connection.Id, connection.Range.Start)));

        return ids
            .Where(entry => !string.IsNullOrEmpty(entry.Id))
            .GroupBy(entry => entry.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => new FdgBreach(
                FdgRuleIds.DuplicateId,
                $"`{group.Key}` is declared {group.Count()} times; an id names one entry.",
                [group.Key],
                group.Last().Start));
    }

    /// <summary>
    /// What the parser passed over, plus the sizes a document may state below their minimum.
    /// </summary>
    /// <remarks>
    /// The parser's problems and the size checks share one rule id because they are one thing to
    /// an author: the entry is not as this module expects, the lines are kept, and here is why.
    /// </remarks>
    private static IEnumerable<FdgBreach> UnreadableEntries(FdgModel model)
    {
        foreach (var problem in model.Problems)
        {
            yield return new FdgBreach(FdgRuleIds.UnreadableEntry, problem.Message, [], problem.Line);
        }

        foreach (var element in model.Elements)
        {
            if (element.Width < FdgGeometry.MinimumWidth)
            {
                yield return new FdgBreach(
                    FdgRuleIds.UnreadableEntry,
                    $"`{element.Id}` is {element.Width} wide; the minimum is {FdgGeometry.MinimumWidth}.",
                    [element.Id],
                    element.Range.Start);
            }

            if (element is { IsComment: true, DrawnHeight: < FdgGeometry.MinimumCommentHeight })
            {
                yield return new FdgBreach(
                    FdgRuleIds.UnreadableEntry,
                    $"`{element.Id}` is {element.DrawnHeight} tall; a Comment's minimum is {FdgGeometry.MinimumCommentHeight}.",
                    [element.Id],
                    element.Range.Start);
            }
        }
    }

    private static IEnumerable<FdgBreach> Links(FdgModel model, Dictionary<string, FdgElement> byId)
    {
        foreach (var connection in model.Connections)
        {
            var hasSource = byId.TryGetValue(connection.From, out var source);
            var hasTarget = byId.TryGetValue(connection.To, out var target);

            if (!hasSource || !hasTarget)
            {
                var missing = !hasSource ? connection.From : connection.To;
                yield return new FdgBreach(
                    FdgRuleIds.DanglingReference,
                    $"`{connection.Id}` names `{missing}`, which is not an element in this document.",
                    [connection.Id, missing],
                    connection.Range.Start);
                continue;
            }

            if (string.Equals(connection.From, connection.To, StringComparison.Ordinal))
            {
                yield return new FdgBreach(
                    FdgRuleIds.SelfLink,
                    $"`{connection.Id}` links `{connection.From}` to itself; no relation allows that.",
                    [connection.From],
                    connection.Range.Start);
                continue;
            }

            var relation = FdgRelations.ById(connection.Type);
            if (relation is null)
            {
                yield return new FdgBreach(
                    FdgRuleIds.ForbiddenLink,
                    $"`{connection.Id}` names the relation `{connection.Type}`, which this notation does not have.",
                    [connection.Id],
                    connection.Range.Start);
                continue;
            }

            if (!relation.Admits(source!.Type, target!.Type))
            {
                yield return new FdgBreach(
                    FdgRuleIds.ForbiddenLink,
                    $"`{relation.Id}` does not admit a link from a {source.Type} to a {target.Type}.",
                    [connection.From, connection.To],
                    connection.Range.Start);
            }
        }
    }

    private static IEnumerable<FdgBreach> Cardinality(FdgModel model)
    {
        foreach (var relation in FdgRelations.All)
        {
            var links = model.Connections
                .Where(connection => string.Equals(connection.Type, relation.Id, StringComparison.Ordinal))
                .ToList();

            if (relation.MaxIntoTarget is { } intoTarget)
            {
                foreach (var group in links.GroupBy(connection => connection.To, StringComparer.Ordinal)
                             .Where(group => !string.IsNullOrEmpty(group.Key) && group.Count() > intoTarget))
                {
                    yield return new FdgBreach(
                        FdgRuleIds.SecondParent,
                        $"`{group.Key}` has {group.Count()} `{relation.Id}` parents; it may have {intoTarget}.",
                        [group.Key, .. group.Select(connection => connection.From)],
                        group.Last().Range.Start);
                }
            }

            if (relation.MaxFromSource is not { } fromSource)
            {
                continue;
            }

            foreach (var group in links.GroupBy(connection => connection.From, StringComparer.Ordinal)
                         .Where(group => !string.IsNullOrEmpty(group.Key) && group.Count() > fromSource))
            {
                // `shows` is the only relation with this limit, so its rule id is the specific one.
                yield return new FdgBreach(
                    FdgRuleIds.SecondShows,
                    $"`{group.Key}` has {group.Count()} `{relation.Id}` links; it may have {fromSource}.",
                    [group.Key, .. group.Select(connection => connection.To)],
                    group.Last().Range.Start);
            }
        }
    }

    private static IEnumerable<FdgBreach> Cycles(FdgModel model) =>
        FdgOwnership.CyclesIn(model).Select(cycle => new FdgBreach(
            FdgRuleIds.OwnershipCycle,
            $"Ownership loops: {string.Join(" -> ", cycle)} -> {cycle[0]}.",
            cycle,
            LineOfFirst(model, cycle)));

    private static int LineOfFirst(FdgModel model, IReadOnlyList<string> cycle) =>
        model.Elements.FirstOrDefault(element => string.Equals(element.Id, cycle[0], StringComparison.Ordinal))?.Range.Start ?? 0;
}
