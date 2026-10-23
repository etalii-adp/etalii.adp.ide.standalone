using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// The state of one budget (DISL §3.2.1) over a diagram, as <c>budget(id)</c> reads it in CEL.
/// </summary>
/// <param name="Id">The budget's id.</param>
/// <param name="Shown">How many candidates are drawn: the kept ones of a truncating budget, every one otherwise.</param>
/// <param name="Total">The measure: how many candidates a <c>count</c> budget has, or what its <c>cel</c> gives.</param>
/// <param name="Exceeded">Whether the measure is greater than the budget's <c>max</c>.</param>
/// <param name="Truncated">Whether the budget cuts what is drawn: it truncates and is exceeded.</param>
/// <param name="Withheld">Whether edits are withheld: it is exceeded and withholds something.</param>
/// <param name="Cut">The candidates it leaves undrawn; empty when it does not truncate.</param>
public sealed record DislBudgetState(string Id, long Shown, long Total, bool Exceeded, bool Truncated, bool Withheld, IReadOnlySet<DislElement> Cut);

/// <summary>
/// Computes the budgets of a diagram (DISL §3.2.1): each one's measure, the candidates a truncating <c>count</c>
/// budget keeps - sorted by its <c>order</c>, ties in persistence order, whole <c>unit</c>s kept while they fit - and
/// whether it withholds edits.
/// </summary>
internal static class DislBudgets
{
    /// <summary>The sentence a withheld edit is refused with when the budget words none.</summary>
    private const string DefaultReason = "The diagram shows only part of this model, so edits through it are withheld.";

    public static IReadOnlyList<DislBudgetState> Compute(DislDiagram diagram)
    {
        var states = new List<DislBudgetState>();
        foreach (var budget in diagram.Specification.Budgets)
        {
            states.Add(State(diagram, budget));
        }
        return states;
    }

    private static DislBudgetState State(DislDiagram diagram, DislBudget budget)
    {
        var withholds = budget.Withhold is not "none";
        if (budget.Measure is { } measure)
        {
            // A measure that fails, or is not a number, measures nothing: the budget is not exceeded.
            var total = measure.Evaluate(Variables(diagram, null, 0)) switch
            {
                long count => count,
                double count => (long)count,
                _ => 0L,
            };
            var exceeded = total > budget.Max;
            return new DislBudgetState(budget.Id, total, total, exceeded, false, exceeded && withholds, new HashSet<DislElement>());
        }

        var candidates = diagram.Elements.Where(element => budget.Types.Any(element.IsA)).ToList();
        var over = candidates.Count > budget.Max;
        if (!budget.Truncate || !over)
        {
            return new DislBudgetState(budget.Id, candidates.Count, candidates.Count, over, false, over && withholds, new HashSet<DislElement>());
        }

        var sorted = Sorted(diagram, budget, candidates);
        var kept = new HashSet<DislElement>(ReferenceEqualityComparer.Instance);
        foreach (var unit in Units(diagram, budget, sorted, candidates))
        {
            if (kept.Count + unit.Count > budget.Max) break;
            kept.UnionWith(unit);
        }
        var cut = new HashSet<DislElement>(candidates.Where(candidate => !kept.Contains(candidate)), ReferenceEqualityComparer.Instance);
        return new DislBudgetState(budget.Id, kept.Count, candidates.Count, true, true, withholds, cut);
    }

    /// <summary>
    /// The candidates of the budget <paramref name="id"/> that are drawn (§3.2.1), in its <c>order</c> with ties in
    /// persistence order, whether or not it cuts; empty when the specification declares no <c>count</c> budget by that id.
    /// </summary>
    public static IReadOnlyList<DislElement> Ordered(DislDiagram diagram, string id)
    {
        if (diagram.Specification.Budgets.FirstOrDefault(declared => declared.Id == id) is not { Measure: null } budget) return [];
        var candidates = diagram.Elements.Where(element => budget.Types.Any(element.IsA)).ToList();
        return [.. Sorted(diagram, budget, candidates).Where(diagram.IsDrawn)];
    }

    /// <summary>The candidates sorted by the budget's <c>order</c>, ties in persistence order; as they are without one.</summary>
    private static List<DislElement> Sorted(DislDiagram diagram, DislBudget budget, List<DislElement> candidates) =>
        budget.Order is { } order
            ? candidates.Select((candidate, index) => (Candidate: candidate, Key: order.Evaluate(Variables(diagram, candidate, index)), Index: index))
                .OrderBy(entry => entry.Key, KeyOrder.Instance).ThenBy(entry => entry.Index).Select(entry => entry.Candidate).ToList()
            : candidates;

    /// <summary>The units of the sorted candidates, in the order of each one's first candidate; each candidate its own without a <c>unit</c>.</summary>
    private static IEnumerable<List<DislElement>> Units(DislDiagram diagram, DislBudget budget, List<DislElement> sorted, List<DislElement> candidates)
    {
        if (budget.Unit is not { } unit) return sorted.Select(candidate => new List<DislElement> { candidate });

        var positions = new Dictionary<DislElement, int>(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < candidates.Count; index++) positions[candidates[index]] = index;
        var units = new List<List<DislElement>>();
        var byKey = new Dictionary<string, List<DislElement>>(StringComparer.Ordinal);
        foreach (var candidate in sorted)
        {
            var key = DislEvaluation.TextOf(unit.Evaluate(Variables(diagram, candidate, positions[candidate])));
            if (!byKey.TryGetValue(key, out var members))
            {
                members = [];
                byKey[key] = members;
                units.Add(members);
            }
            members.Add(candidate);
        }
        return units;
    }

    private static Dictionary<string, object?> Variables(DislDiagram diagram, DislElement? self, int index) =>
        new(StringComparer.Ordinal) { ["self"] = self, ["index"] = (long)index, ["diagram"] = diagram, ["env"] = diagram.Env.ToCel() };

    /// <summary>
    /// The reason an edit is withheld (§3.2.1): that of the first exceeded budget that withholds it - every budget
    /// withholding <c>model</c> or <c>all</c> withholds a change to the model, only <c>all</c> one to stored view data -
    /// or null when none does.
    /// </summary>
    public static string? WithheldReason(DislDiagram diagram, bool viewData)
    {
        var states = diagram.Budgets;
        for (var index = 0; index < states.Count; index++)
        {
            var budget = diagram.Specification.Budgets[index];
            if (!states[index].Withheld || viewData && budget.Withhold != "all") continue;
            var variables = new Dictionary<string, object?>(StringComparer.Ordinal) { ["self"] = diagram, ["diagram"] = diagram, ["env"] = diagram.Env.ToCel() };
            return budget.WithholdReason?.Text(diagram.Specification, variables) is { Length: > 0 } text ? text : DefaultReason;
        }
        return null;
    }

    /// <summary>The refusal of a gesture a budget withholds (§3.2.1), as a finding about <paramref name="self"/>; null when none withholds it.</summary>
    public static DislFinding? Refusal(DislDiagram diagram, bool viewData, DislElement? self) =>
        WithheldReason(diagram, viewData) is { } reason
            ? new DislFinding(Withheld, "budget", "error", reason, self is null ? [] : [self.Id], self?.Line)
            : null;

    /// <summary>The code of a gesture refused because a budget withholds it.</summary>
    public const string Withheld = "std.budgetWithheld";

    /// <summary>
    /// What an operation changes (§3.2.1): <c>model</c> when it runs a plugin or any action that changes the model,
    /// <c>view</c> when it only lays out or writes view data, and null when it changes neither.
    /// </summary>
    public static string? Changes(DislOperation operation) =>
        operation.Plugin is not null ? "model" : Changes(operation.Actions.Actions);

    private static string? Changes(IReadOnlyList<DislAction> actions)
    {
        string? changes = null;
        foreach (var action in actions)
        {
            var kind = action switch
            {
                DislAction.Let or DislAction.Select or DislAction.Abort => null,
                DislAction.Layout or DislAction.View => "view",
                DislAction.If branch => Changes(branch.Then.Actions) is "model" || Changes(branch.Else.Actions) is "model" ? "model" : Changes(branch.Then.Actions) ?? Changes(branch.Else.Actions),
                DislAction.ForEach loop => Changes(loop.Do.Actions),
                _ => "model",
            };
            if (kind == "model") return kind;
            changes ??= kind;
        }
        return changes;
    }

    /// <summary>
    /// Sort keys compared as CEL orders them; a list element by element, a shorter list first when it is a prefix; a
    /// key that cannot be compared (a failed one among them) after every key that can.
    /// </summary>
    private sealed class KeyOrder : IComparer<object?>
    {
        public static KeyOrder Instance { get; } = new();

        public int Compare(object? x, object? y)
        {
            if (x is IReadOnlyList<object?> left && y is IReadOnlyList<object?> right)
            {
                for (var index = 0; index < Math.Min(left.Count, right.Count); index++)
                {
                    var order = Compare(left[index], right[index]);
                    if (order != 0) return order;
                }
                return left.Count.CompareTo(right.Count);
            }
            var comparable = x is string or long or double or bool;
            var otherComparable = y is string or long or double or bool;
            if (!comparable || !otherComparable) return comparable.CompareTo(otherComparable) * -1;
            if (x!.GetType() != y!.GetType() && (x is string || y is string || x is bool || y is bool)) return Rank(x).CompareTo(Rank(y));
            return CelOrdering.Compare(x, y);

            static int Rank(object value) => value switch { bool => 0, long or double => 1, _ => 2 };
        }
    }
}
