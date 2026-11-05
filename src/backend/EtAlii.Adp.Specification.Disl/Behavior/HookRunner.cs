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
        return !relation.Type.IsRelation
            ? throw new ArgumentException($"{relation} is no relation, so it was not drawn.", nameof(relation))
            : Run(specification, relation, null, "connect", [], ids, env, source);
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
        var runner = new ActionRunner(specification, self.Diagram, ids);
        var ran = new HashSet<(string Hook, DislElement Element)>();
        var budget = specification.Behavior.HookDepth();
        Ran(self, old, kind, attributes);
        return runner.Transaction;

        bool Ran(DislElement element, DislElement? before, string happened, IReadOnlyCollection<string> changed)
        {
            foreach (var hook in Hooks(specification, element, happened, changed))
            {
                if (!ran.Add((hook.Id, element))) continue;
                if (--budget < 0) return runner.Refuse("The change set off more hooks than behavior.maxHookDepth allows.");

                var variables = OperationInterpreter.Variables(DislContext.Hook, element.Diagram, env);
                variables["self"] = element;
                variables["old"] = before;
                variables["event"] = Event(element, before, happened, changed, source);
                if (hook.When is { } when && !when.Holds(variables)) continue;

                var first = runner.Changes.Count;
                var snapshots = new Dictionary<DislElement, DislElement>(ReferenceEqualityComparer.Instance);
                runner.Before = snapshots;
                if (hook.Actions is { } actions && !runner.Run(actions, variables)) return false;
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

    private static IEnumerable<DislHook> Hooks(DislSpecification specification, DislElement element, string kind, IReadOnlyCollection<string> changed)
    {
        var declared = new List<(DislHook Hook, int Index)>();
        var index = 0;
        foreach (var hook in specification.Behavior.Hooks)
        {
            var position = index++;
            if (!hook.On.Contains(kind)) continue;
            if (hook.Phase != "after") continue;
            if (hook.For.Count > 0 && !hook.For.Any(element.IsA)) continue;
            if (kind == "change" && hook.Attribute.Count > 0 && !hook.Attribute.Any(changed.Contains)) continue;
            // An order that is not a number threw here when it was read from the JSON at run time, and still does.
            if (hook.OrderMalformed is { } malformed) throw new InvalidOperationException(malformed);
            declared.Add((hook, position));
        }
        return declared.OrderBy(hook => hook.Hook.Order).ThenBy(hook => hook.Index).Select(hook => hook.Hook);
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
}
