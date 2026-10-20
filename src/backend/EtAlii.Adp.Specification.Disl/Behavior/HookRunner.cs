using System.Text.Json;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// Runs a definition's <c>after</c> hooks (DISL §9.2) for a change of an element's attributes - every
/// hook <c>on: change</c> whose <c>for</c> takes the element and whose <c>attribute</c> list meets the
/// changed attributes, with <c>self</c> the element after the change and <c>old</c> a snapshot before it -
/// and for a relation drawn: every hook <c>on: connect</c> whose <c>for</c> takes the relation, with
/// <c>self</c> the new relation and <c>old</c> null. Hooks run in <c>order</c> and then declaration order.
/// </summary>
/// <remarks>
/// <para>
/// <b>The host has made the change on the working diagram already</b> and hands in the snapshot it
/// took before; the hooks' own changes are made on it as they run (<see cref="ActionRunner"/>).
/// </para>
/// <para>
/// <b>A hook's change can run further hooks</b>, each at most once per element in the transaction,
/// and no more than <c>behavior.maxHookDepth</c> (100) runs in all; past that the transaction is refused.
/// </para>
/// <para>
/// <b>Only <c>change</c> and <c>connect</c> events, and only <c>after</c> hooks, are run here.</b> A
/// <c>before</c> hook is not run; no bundled definition declares one.
/// </para>
/// </remarks>
public static class HookRunner
{
    /// <summary>
    /// The hooks' changes after <paramref name="attributes"/> of <paramref name="self"/> changed, from
    /// what <paramref name="old"/> held: the snapshot taken with <see cref="Snapshot"/> before the change.
    /// </summary>
    public static DislTransaction AfterChange(
        DislSpecification specification,
        DislElement self,
        DislElement old,
        IReadOnlyCollection<string> attributes,
        IIdSource ids,
        DislEnv? env = null,
        string source = "user")
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(old);
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(ids);

        return Run(specification, self, old, "change", attributes, ids, env, source);
    }

    /// <summary>
    /// The hooks' changes after <paramref name="relation"/> was drawn (an <c>on: connect</c> event): the
    /// host has added it to the working diagram, ends and attributes, before calling this.
    /// </summary>
    public static DislTransaction AfterConnect(
        DislSpecification specification,
        DislElement relation,
        IIdSource ids,
        DislEnv? env = null,
        string source = "user")
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(relation);
        ArgumentNullException.ThrowIfNull(ids);
        if (!relation.Type.IsRelation) throw new ArgumentException($"{relation} is no relation, so it was not drawn.", nameof(relation));

        return Run(specification, relation, null, "connect", [], ids, env, source);
    }

    private static DislTransaction Run(
        DislSpecification specification,
        DislElement self,
        DislElement? old,
        string kind,
        IReadOnlyCollection<string> attributes,
        IIdSource ids,
        DislEnv? env,
        string source)
    {
        var runner = new ActionRunner(specification, self.Diagram, ids, DislContext.Hook);
        var ran = new HashSet<(string Hook, DislElement Element)>();
        var budget = MaxDepth(specification);
        Ran(self, old, kind, attributes);
        return runner.Transaction;

        bool Ran(DislElement element, DislElement? before, string happened, IReadOnlyCollection<string> changed)
        {
            foreach ((JsonElement hook, string pointer) in Hooks(specification, element, happened, changed))
            {
                var id = DislJson.String(hook, "id") ?? pointer;
                if (!ran.Add((id, element))) continue;
                if (--budget < 0) return runner.Refuse("The change set off more hooks than behavior.maxHookDepth allows.");

                var variables = OperationInterpreter.Variables(DislContext.Hook, element.Diagram, env);
                variables["self"] = element;
                variables["old"] = before;
                variables["event"] = Event(element, before, happened, changed, source);
                if (hook.TryGetProperty("when", out var when) && !DislEvaluation.Holds(specification, when, DislJson.Pointer(pointer, "when"), DislContext.Hook, variables)) continue;

                var first = runner.Changes.Count;
                var snapshots = new Dictionary<DislElement, DislElement>(ReferenceEqualityComparer.Instance);
                runner.Before = snapshots;
                if (hook.TryGetProperty("actions", out var actions) && !runner.Run(actions, DislJson.Pointer(pointer, "actions"), variables)) return false;
                runner.Before = null;

                foreach (var set in runner.Changes.Skip(first).OfType<DislChange.Set>().ToList())
                {
                    if (element.Diagram.Elements.FirstOrDefault(candidate => candidate.Id == set.ElementId) is { } target
                        && !Ran(target, snapshots.GetValueOrDefault(target) ?? target.Snapshot(), "change", [.. set.Attributes.Keys]))
                    {
                        return false;
                    }
                }
            }
            return true;
        }
    }

    /// <summary>The element as it is now, to hand to <see cref="AfterChange"/> as <c>old</c> once the change is made.</summary>
    public static DislElement Snapshot(DislElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.Snapshot();
    }

    /// <summary>Stores <paramref name="value"/> for <paramref name="attribute"/> of <paramref name="element"/> on the working diagram, or forgets it for null.</summary>
    public static void Store(DislElement element, string attribute, object? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.Store(attribute, value);
    }

    private static IEnumerable<(JsonElement Hook, string Pointer)> Hooks(DislSpecification specification, DislElement element, string kind, IReadOnlyCollection<string> changed)
    {
        if (!specification.Root.TryGetProperty("behavior", out var behavior) || !behavior.TryGetProperty("hooks", out var hooks) || hooks.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        var declared = new List<(JsonElement Hook, string Pointer, long Order, int Index)>();
        var index = 0;
        foreach (var hook in hooks.EnumerateArray())
        {
            var pointer = DislJson.Pointer("/behavior/hooks", index);
            var position = index++;
            if (!DislJson.Strings(hook, "on").Contains(kind)) continue;
            if ((DislJson.String(hook, "phase") ?? "after") != "after") continue;
            var types = DislJson.Strings(hook, "for");
            if (types.Count > 0 && !types.Any(element.IsA)) continue;
            var watched = DislJson.Strings(hook, "attribute");
            if (kind == "change" && watched.Count > 0 && !watched.Any(changed.Contains)) continue;
            var order = hook.TryGetProperty("order", out var declaredOrder) && declaredOrder.TryGetInt64(out var value) ? value : 0;
            declared.Add((hook, pointer, order, position));
        }
        return declared.OrderBy(hook => hook.Order).ThenBy(hook => hook.Index).Select(hook => (hook.Hook, hook.Pointer));
    }

    private static Cel.CelMap Event(DislElement element, DislElement? before, string kind, IReadOnlyCollection<string> changed, string source)
    {
        var attribute = changed.FirstOrDefault();
        return new Cel.CelMap
        {
            ["kind"] = kind,
            ["attribute"] = attribute,
            ["oldValue"] = attribute is not null && before is not null && before.Type.Attributes.ContainsKey(attribute) ? before.ValueOf(attribute) : null,
            ["newValue"] = attribute is not null && element.Type.Attributes.ContainsKey(attribute) ? element.ValueOf(attribute) : null,
            ["source"] = source,
        };
    }

    private static int MaxDepth(DislSpecification specification) =>
        specification.Root.TryGetProperty("behavior", out var behavior) && behavior.TryGetProperty("maxHookDepth", out var depth) && depth.TryGetInt32(out var value)
            ? value
            : 100;
}
