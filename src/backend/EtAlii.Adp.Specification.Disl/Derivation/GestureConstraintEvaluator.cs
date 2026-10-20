using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// The refusals of a gesture (DISL §8.4): every declared gesture constraint of its kind whose scope
/// takes the element, whose enforcement prevents, whose <c>when</c> holds and whose <c>rule</c> does
/// not, in the order of <c>rules</c>, each with its message evaluated in the gesture's context.
/// </summary>
/// <remarks>
/// <para>
/// <b>Declared constraints only.</b> The built-ins that also refuse (§8.4, step 3) are checked by the
/// host where it has them; no bundled definition configures one for a change, a placement, a deletion or
/// a reorder.
/// </para>
/// <para>
/// <b>A rule that fails to evaluate refuses</b>: a gesture is let through only when its rule is seen
/// to hold. A <c>when</c> that fails does not apply the rule.
/// </para>
/// </remarks>
public static class GestureConstraintEvaluator
{
    /// <summary>A change of <paramref name="attribute"/> of <paramref name="self"/> from its value to <paramref name="newValue"/>.</summary>
    public static IReadOnlyList<DislFinding> Change(DislSpecification specification, DislElement self, string attribute, object? newValue, DislEnv? env = null)
    {
        ArgumentNullException.ThrowIfNull(self);
        var oldValue = self.Type.Attributes.ContainsKey(attribute) ? self.ValueOf(attribute) : null;
        return Refusals(specification, "change", self, new Dictionary<string, object?>
        {
            ["attribute"] = attribute,
            ["oldValue"] = oldValue,
            ["newValue"] = newValue,
        }, env);
    }

    /// <summary>
    /// A placement of <paramref name="self"/>: a <paramref name="gesture"/> (<c>move</c>, <c>resize</c>,
    /// <c>reparent</c>) from <paramref name="oldBounds"/> to <paramref name="newBounds"/>, in domain values.
    /// </summary>
    public static IReadOnlyList<DislFinding> Placement(
        DislSpecification specification,
        DislElement self,
        string gesture,
        IReadOnlyDictionary<string, object?> oldBounds,
        IReadOnlyDictionary<string, object?> newBounds,
        DislElement? newParent = null,
        DislEnv? env = null) =>
        Refusals(specification, "placement", self, new Dictionary<string, object?>
        {
            ["gesture"] = gesture,
            ["oldBounds"] = Map(oldBounds),
            ["newBounds"] = Map(newBounds),
            ["newParent"] = newParent,
        }, env);

    /// <summary>A deletion of <paramref name="self"/>, with <paramref name="selection"/> the elements deleted with it; itself alone when none is given.</summary>
    public static IReadOnlyList<DislFinding> Delete(DislSpecification specification, DislElement self, IReadOnlyList<DislElement>? selection = null, DislEnv? env = null)
    {
        ArgumentNullException.ThrowIfNull(self);
        return Refusals(specification, "delete", self, new Dictionary<string, object?>
        {
            ["selection"] = selection?.Cast<object?>().ToList() ?? [self],
        }, env);
    }

    /// <summary>
    /// A reorder of <paramref name="self"/> among the children of its parent: from <paramref name="oldIndex"/> to
    /// <paramref name="newIndex"/> among <paramref name="siblings"/>, itself included.
    /// </summary>
    public static IReadOnlyList<DislFinding> Reorder(DislSpecification specification, DislElement self, IReadOnlyList<DislElement> siblings, int oldIndex, int newIndex, DislEnv? env = null)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(siblings);
        return Refusals(specification, "reorder", self, new Dictionary<string, object?>
        {
            ["parent"] = self.Parent,
            ["oldIndex"] = (long)oldIndex,
            ["newIndex"] = (long)newIndex,
            ["siblings"] = siblings.Cast<object?>().ToList(),
        }, env);
    }

    /// <summary>The refusals of a gesture of <paramref name="kind"/> on <paramref name="self"/>, its own variables in <paramref name="gesture"/>.</summary>
    private static IReadOnlyList<DislFinding> Refusals(DislSpecification specification, string kind, DislElement? self, IReadOnlyDictionary<string, object?> gesture, DislEnv? env = null)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(gesture);

        List<DislFinding> refusals = [];
        if (specification.Constraints.Rules is not { } rules) return refusals;

        var variables = new Dictionary<string, object?>(gesture)
        {
            ["self"] = self,
            ["diagram"] = self?.Diagram,
            ["env"] = (env ?? new DislEnv()).ToCel(),
        };
        foreach (var rule in rules)
        {
            if (rule.Kind != kind) continue;
            if (!rule.Enabled) continue;
            if ((rule.Enforcement ?? "prevent") is not ("prevent" or "prevent-and-report")) continue;
            var scope = rule.Scope;
            if (scope.Count > 0 && !(self is not null && scope.Any(self.IsA)) && !scope.Contains("*")) continue;

            if (rule.When is { } when && !when.Holds(variables)) continue;
            if (rule.Rule is { } condition && condition.Evaluate(variables) is true) continue;

            var message = rule.Message?.Text(variables, rule.Id) ?? rule.Id;
            refusals.Add(new DislFinding(rule.Code, rule.Id, rule.SeverityText ?? "error", message, self is null ? [] : [self.Id], self?.Line));
        }
        return refusals;
    }

    private static CelMap Map(IReadOnlyDictionary<string, object?> values)
    {
        var map = new CelMap();
        foreach ((string key, object? value) in values) map[key] = value;
        return map;
    }
}
