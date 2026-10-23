using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// The refusals of a gesture (DISL §8.4): every declared gesture constraint of its kind whose scope
/// takes the element, whose enforcement prevents, whose <c>when</c> holds and whose <c>rule</c> does
/// not, in the order of <c>rules</c>, each with its message evaluated in the gesture's context.
/// </summary>
/// <remarks>
/// <para>
/// <b>The built-ins refuse a connect only.</b> <see cref="Connect"/> checks <c>std.endpoints</c>,
/// <c>std.multiplicity</c> and <c>std.acyclic</c> before the declared constraints (§8.4, step 3). For a
/// change, a placement, a deletion or a reorder the built-ins that also refuse are checked by the host
/// where it has them; no bundled definition configures one for those.
/// </para>
/// <para>
/// <b>A rule that fails to evaluate refuses</b>: a gesture is let through only when its rule is seen
/// to hold. A <c>when</c> that fails does not apply the rule.
/// </para>
/// <para>
/// <b>Before every other check</b> (§8.4, step 1): the notation's <c>refusals</c> for the gesture on the element's kind
/// (§6.9, §6.10) - <c>move</c>, <c>resize</c> or <c>reparent</c> for a placement, <c>delete</c>, <c>reconnect</c> of the
/// relation reconnected and <c>connect</c> of either end - and then, for a placement, the refusal of an element whose id
/// is ephemeral (§11.5.3), since a placement stores view data or a parent by that id. A notation states a refusal for a
/// gesture it switches off, so a refusal it states is shown, the first of its Reasons that applies.
/// </para>
/// </remarks>
public static class GestureConstraintEvaluator
{
    /// <summary>
    /// The refusal of a connect gesture (§8.4): a relation of <paramref name="relationType"/> from <paramref name="source"/>
    /// to <paramref name="target"/>, a new one or <paramref name="self"/> reconnected; null when it is allowed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The first refusal only</b>, in the order of §8.4: the notation's <c>reconnect</c> of <paramref name="self"/> and <c>connect</c> of each end, then the built-ins <c>std.endpoints</c>, <c>std.multiplicity</c> and
    /// <c>std.acyclic</c>, then the declared <c>connect</c> constraints in the order of <c>rules</c>. A built-in that
    /// <c>constraints.builtIn</c> switches off refuses nothing. Each is judged against the diagram as it is, with
    /// <paramref name="self"/>, when it is given, left out of it.
    /// </para>
    /// <para>
    /// <b>The checks.</b> <c>std.endpoints</c>: an end whose element the relation type does not allow there, the
    /// source before the target, then a loop <c>allowSelfLoops: false</c> forbids, then a repeat <c>allowParallel:
    /// false</c> forbids, each read as the relation type's own declaration states it, as the findings read it.
    /// <c>std.multiplicity</c>: an end that states a <c>max</c>, the source end before the target, when the element at
    /// it already has that many relations of the end's declaring type and its subtypes there. <c>std.acyclic</c>: for
    /// each acyclic type the relation type is or extends, and that no acyclic supertype covers, whether the source is
    /// <c>reachable</c> (§12.4) from the target over it and its subtypes; nothing is enumerated.
    /// </para>
    /// <para>
    /// <b>The wording</b> is the built-in's <c>refusal</c> (§8.1), evaluated in the gesture's context with
    /// <c>violation</c> bound (§8.4): <c>rule</c>, and <c>relationType</c> and <c>end</c> for <c>std.endpoints</c> (the
    /// target end for a loop or a repeat); <c>min</c> when the end states one, <c>max</c>, <c>count</c> (what the element
    /// would have), <c>relationType</c> (the end's declaring type) and <c>end</c> for <c>std.multiplicity</c>;
    /// <c>relationType</c> (the acyclic type) and <c>cycle</c>, the elements of the loop in order from the source, for
    /// <c>std.acyclic</c>. Without a <c>refusal</c>, or with one that fails, the runtime words it, never from
    /// <c>message</c>. The refusal's code is the built-in's <c>code</c> when that is a string, else its id: a computed
    /// <c>code</c> is evaluated in the <c>constraint</c> context, which a gesture does not have. It is about the source
    /// and the target, and has no line.
    /// </para>
    /// </remarks>
    public static DislFinding? Connect(DislSpecification specification, DislDiagram diagram, string relationType, DislElement source, DislElement target, DislElement? self = null, DislEnv? env = null)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(diagram);
        ArgumentNullException.ThrowIfNull(relationType);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        if (specification.Metamodel.TypeOf(relationType) is not { IsRelation: true } type)
        {
            throw new ArgumentException($"'{relationType}' is not a relation type of this specification.", nameof(relationType));
        }

        var gesture = new Dictionary<string, object?>
        {
            ["relationType"] = relationType,
            ["source"] = source,
            ["target"] = target,
            ["sourcePort"] = null,
            ["targetPort"] = null,
        };
        var switchedOff = (self is null ? null : SwitchedOff(specification, self, "reconnect", env))
            ?? SwitchedOff(specification, source, "connect", env)
            ?? SwitchedOff(specification, target, "connect", env);
        if (switchedOff is not null) return switchedOff;
        if (DislBudgets.Refusal(diagram, viewData: false, self) is { } withheld) return withheld;
        var connect = new ConnectGesture(specification, diagram, type, source, target, self, gesture, (env ?? new DislEnv()).ToCel());
        return connect.Endpoints() ?? connect.Multiplicity() ?? connect.Acyclic()
            ?? Refusals(specification, "connect", self, gesture, env, diagram, relationType).FirstOrDefault();
    }

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

    /// <summary>
    /// The refusals of a gesture of <paramref name="kind"/> on <paramref name="self"/>, its own variables in
    /// <paramref name="gesture"/>, on <paramref name="diagram"/> (else the diagram of <paramref name="self"/>). A connect
    /// gesture's scope is judged on <paramref name="scoped"/>, the relation type it makes, since a new relation has no
    /// <c>self</c>.
    /// </summary>
    private static IReadOnlyList<DislFinding> Refusals(DislSpecification specification, string kind, DislElement? self, IReadOnlyDictionary<string, object?> gesture, DislEnv? env = null, DislDiagram? diagram = null, string? scoped = null)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(gesture);

        // The notation's refusal and an ephemeral id's come first (§8.4, step 1).
        if (self is not null && FirstStep(specification, kind, self, gesture, env) is { } first) return [first];

        // A withheld edit is refused before every constraint (§3.2.1); a move or a resize writes view data only.
        var viewData = kind == "placement" && gesture.GetValueOrDefault("gesture") is "move" or "resize";
        if ((diagram ?? self?.Diagram) is { } judged && DislBudgets.Refusal(judged, viewData, self) is { } withheld) return [withheld];

        List<DislFinding> refusals = [];
        if (specification.Constraints.Rules is not { } rules) return refusals;

        var variables = new Dictionary<string, object?>(gesture)
        {
            ["self"] = self,
            ["diagram"] = diagram ?? self?.Diagram,
            ["env"] = (env ?? new DislEnv()).ToCel(),
        };
        foreach (var rule in rules)
        {
            if (rule.Kind != kind) continue;
            if (!rule.Enabled) continue;
            if ((rule.Enforcement ?? "prevent") is not ("prevent" or "prevent-and-report")) continue;
            var scope = rule.Scope;
            if (scope.Count > 0 && !InScope(specification, scope, self, scoped) && !scope.Contains("*")) continue;

            if (rule.When is { } when && !when.Holds(variables)) continue;
            if (rule.Rule is { } condition && condition.Evaluate(variables) is true) continue;

            var message = rule.Message?.Text(variables, rule.Id) ?? rule.Id;
            refusals.Add(new DislFinding(rule.Code, rule.Id, rule.SeverityText ?? "error", message, About(self, gesture), self?.Line));
        }
        return refusals;
    }

    /// <summary>The code of a gesture the notation switches off for the element's kind (§6.9, §6.10).</summary>
    public const string SwitchedOffCode = "std.switchedOff";

    /// <summary>The code of a gesture that would store an ephemeral id (§11.5.3).</summary>
    public const string EphemeralCode = "std.ephemeralId";

    /// <summary>The runtime's sentence for an ephemeral element whose id rule states no <c>reason</c> (§11.5.3).</summary>
    private const string EphemeralSentence = "This element's identity does not survive reading the file again, so nothing can be stored for it.";

    /// <summary>
    /// The first refusal of step 1 of §8.4 for a gesture of <paramref name="kind"/> on <paramref name="self"/>: the
    /// notation's for a placement's gesture or a deletion, then, for a placement, that of an ephemeral element; null when neither refuses.
    /// </summary>
    private static DislFinding? FirstStep(DislSpecification specification, string kind, DislElement self, IReadOnlyDictionary<string, object?> gesture, DislEnv? env)
    {
        var name = kind switch
        {
            "placement" => gesture.GetValueOrDefault("gesture") as string,
            "delete" => "delete",
            _ => null,
        };
        if (name is not null && SwitchedOff(specification, self, name, env) is { } switchedOff) return switchedOff;
        if (kind != "placement" || !specification.IsEphemeral(self)) return null;
        return new DislFinding(EphemeralCode, "ids", "error", specification.EphemeralReason(self) ?? EphemeralSentence, [self.Id], self.Line);
    }

    /// <summary>
    /// The refusal the notation of <paramref name="element"/>'s kind states for <paramref name="gesture"/> (§6.9, §6.10): the
    /// first of its Reasons that applies, evaluated in the element context; null when it states none or none applies.
    /// </summary>
    private static DislFinding? SwitchedOff(DislSpecification specification, DislElement element, string gesture, DislEnv? env)
    {
        var reasons = specification.Notation.RefusalsOf(element, gesture);
        if (reasons.Count == 0) return null;
        var variables = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["self"] = element,
            ["diagram"] = element.Diagram,
            ["env"] = (env ?? element.Diagram.Env).ToCel(),
        };
        return DislReason.First(reasons, specification, variables) is { } text
            ? new DislFinding(SwitchedOffCode, "notation", "error", text, [element.Id], element.Line)
            : null;
    }

    /// <summary>Whether <paramref name="scope"/> takes <paramref name="self"/>, or for a connect gesture the relation type <paramref name="scoped"/>.</summary>
    private static bool InScope(DislSpecification specification, IReadOnlyList<string> scope, DislElement? self, string? scoped)
    {
        if (self is not null) return scope.Any(self.IsA);
        if (scoped is null) return false;
        return scope.Contains("relation") || specification.Metamodel.TypeOf(scoped) is { } type && scope.Any(type.Linearisation.Contains);
    }

    /// <summary>What a refusal is about: <paramref name="self"/>, else a connect gesture's source and target.</summary>
    private static IReadOnlyList<string> About(DislElement? self, IReadOnlyDictionary<string, object?> gesture) =>
        self is not null ? [self.Id]
        : gesture.GetValueOrDefault("source") is DislElement source && gesture.GetValueOrDefault("target") is DislElement target ? [source.Id, target.Id]
        : [];

    /// <summary>The built-ins' checks of one connect gesture (§8.4, step 3), each giving its refusal or null.</summary>
    private sealed class ConnectGesture(
        DislSpecification specification,
        DislDiagram diagram,
        DislType type,
        DislElement source,
        DislElement target,
        DislElement? self,
        IReadOnlyDictionary<string, object?> gesture,
        CelMap env)
    {
        /// <summary><c>std.endpoints</c>: an end of a type the relation does not allow there, then a loop, then a repeat it forbids.</summary>
        public DislFinding? Endpoints()
        {
            if (!Enabled("std.endpoints")) return null;
            string? violation =
                !Allows(type.Source, source) ? "source"
                : !Allows(type.Target, target) ? "target"
                : ReferenceEquals(source, target) && !type.AllowSelfLoops ? "selfLoop"
                : !type.AllowParallel && Others().Any(Parallel) ? "parallel"
                : null;
            if (violation is null) return null;
            var end = violation is "source" or "target" ? violation : "target";
            return Refused("std.endpoints", new CelMap { ["relationType"] = type.Name, ["end"] = end }, violation switch
            {
                "selfLoop" => $"A {type.Name} cannot connect an element to itself.",
                "parallel" => $"A {type.Name} already connects these two elements.",
                _ => $"A {type.Name} cannot have this {end}.",
            });
        }

        /// <summary><c>std.multiplicity</c>: an end whose element already has as many relations there as its <c>max</c> allows, the source end first.</summary>
        public DislFinding? Multiplicity()
        {
            if (!Enabled("std.multiplicity")) return null;
            foreach ((string side, DislRelationEnd? end, DislElement element) in new[] { ("source", type.Source, source), ("target", type.Target, target) })
            {
                if (end is not { Max: { } max, DeclaredBy: { } declaredBy }) continue;
                var count = Others().Count(relation => relation.IsA(declaredBy) && ReferenceEquals(side == "source" ? relation.Source : relation.Target, element)) + 1L;
                if (count <= max) continue;
                var violation = new CelMap { ["max"] = max, ["count"] = count, ["relationType"] = declaredBy, ["end"] = side };
                if (end.Min is { } min) violation["min"] = min;
                var noun = side == "source" ? "outgoing" : "incoming";
                return Refused("std.multiplicity", violation, $"'{element.Id}' already has {count - 1} {noun} {declaredBy} relations; it may have {max}.");
            }
            return null;
        }

        /// <summary><c>std.acyclic</c>: per acyclic type the relation type is or extends, uncovered by an acyclic supertype, whether the source is reachable from the target.</summary>
        public DislFinding? Acyclic()
        {
            if (!Enabled("std.acyclic")) return null;
            var acyclic = type.Linearisation
                .Select(specification.Metamodel.TypeOf)
                .OfType<DislType>()
                .Where(candidate => candidate.Acyclic && !candidate.Linearisation.Skip(1).Any(name => specification.Metamodel.TypeOf(name) is { Acyclic: true }));
            foreach (var covering in acyclic)
            {
                if (PathBack(covering.Name) is not { } back) continue;
                return Refused("std.acyclic", new CelMap { ["relationType"] = covering.Name, ["cycle"] = back }, $"This {covering.Name} would close a cycle: {string.Join(" -> ", back.Cast<DislElement>().Append(source).Select(element => element.Id))}.");
            }
            return null;
        }

        /// <summary>
        /// The loop the new relation would close over <paramref name="relationType"/> and its subtypes: the source, then the
        /// elements a breadth-first walk from the target meets on its way back to the source, in loop order; null when the
        /// source is not <c>reachable</c> from the target, and the source alone for a loop of one.
        /// </summary>
        private List<object?>? PathBack(string relationType)
        {
            if (ReferenceEquals(source, target)) return [source];
            var reachedFrom = new Dictionary<DislElement, DislElement>(ReferenceEqualityComparer.Instance) { [target] = target };
            var pending = new Queue<DislElement>([target]);
            while (pending.Count > 0)
            {
                var at = pending.Dequeue();
                foreach (var relation in at.Outgoing.Where(relation => !ReferenceEquals(relation, self) && relation.IsA(relationType)))
                {
                    if (relation.Target is not { } next || !reachedFrom.TryAdd(next, at)) continue;
                    if (ReferenceEquals(next, source)) return Loop();
                    pending.Enqueue(next);
                }
            }
            return null;

            List<object?> Loop()
            {
                var back = new List<object?>();
                for (var at = reachedFrom[source]; !ReferenceEquals(at, target); at = reachedFrom[at]) back.Add(at);
                back.Add(target);
                back.Add(source);
                back.Reverse();
                return back;
            }
        }

        /// <summary>The diagram's stored relations but the one being reconnected.</summary>
        private IEnumerable<DislElement> Others() => diagram.Relations.Where(relation => !relation.IsDerived && !ReferenceEquals(relation, self));

        /// <summary>Whether <paramref name="relation"/> is of the relation type between the same two elements: in the same direction, or in either for an undirected type.</summary>
        private bool Parallel(DislElement relation) =>
            ReferenceEquals(relation.Type, type)
            && (ReferenceEquals(relation.Source, source) && ReferenceEquals(relation.Target, target)
                || !type.Directed && ReferenceEquals(relation.Source, target) && ReferenceEquals(relation.Target, source));

        private bool Enabled(string builtIn) => specification.Constraints.BuiltIn(builtIn).Enabled;

        private DislFinding Refused(string builtIn, CelMap violation, string fallback)
        {
            var setting = specification.Constraints.BuiltIn(builtIn);
            violation["rule"] = builtIn;
            var variables = new Dictionary<string, object?>(gesture) { ["self"] = self, ["diagram"] = diagram, ["env"] = env, ["violation"] = violation };
            var message = setting.Refusal?.Text(variables, fallback) ?? fallback;
            return new DislFinding(setting.CodeText ?? builtIn, builtIn, setting.Severity ?? "error", message, [source.Id, target.Id], null);
        }

        private static bool Allows(DislRelationEnd? end, DislElement element) =>
            end is null || (end.Types.Count == 0 || end.Types.Any(element.IsA)) && !end.Exclude.Any(element.IsA);
    }

    private static CelMap Map(IReadOnlyDictionary<string, object?> values)
    {
        var map = new CelMap();
        foreach ((string key, object? value) in values) map[key] = value;
        return map;
    }
}
