using System.Text.Json;
using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// The findings of a model (DISL §8.3, §8.6, §8.7): the reader's, the configured built-ins' and every
/// live invariant's, each with its code, severity, message and line.
/// </summary>
/// <remarks>
/// <para>
/// <b>Built-ins</b>: <c>std.unparseable</c> and <c>std.unreadableEntry</c> as the reader raised them
/// (<see cref="DislConstraintOptions.ReaderFindings"/>); <c>std.duplicateId</c> over the written ids;
/// <c>std.endpoints</c> from each relation type's ends, <c>allowSelfLoops</c> and <c>allowParallel</c>;
/// <c>std.references</c> for a relation end that names nothing. Each takes its <c>code</c>,
/// <c>severity</c>, <c>message</c> and <c>enabled</c> from <c>constraints.builtIn</c>, and two
/// proposed keys: <c>x-builtIn.code</c>, an expression in the message's context giving the code per
/// finding, and <c>x-builtIn.oncePerGroup</c> (<c>second</c> or <c>last</c>), which reports a group
/// of duplicates once, at that member, rather than once for each member after the first.
/// </para>
/// <para>
/// <b>Order</b>: without <c>constraints.x-order</c>, §8.6's - the reader's findings, the built-ins,
/// then the rules in declaration order, each by element in model order. With it (a proposed key, a
/// list of codes), findings sort by the position of their code in it, codes it does not list last;
/// then the reader's findings in reading order, then the rest in document order: by the line of the
/// element a rule was evaluated for, and for a group of duplicates, by the line of its first member.
/// </para>
/// <para>
/// <b>A file that does not parse has no other finding</b> (§8.6): with a <c>std.unparseable</c>
/// finding nothing else is evaluated.
/// </para>
/// <para>
/// <b>An expression that fails proves nothing</b>: a <c>when</c> that fails skips the rule, a
/// <c>rule</c> that fails raises no finding, a message that fails is the built-in's or the rule's id.
/// </para>
/// </remarks>
public static class ConstraintEvaluator
{
    /// <summary>The findings of <paramref name="diagram"/> under <paramref name="specification"/>.</summary>
    public static IReadOnlyList<DislFinding> Evaluate(DislSpecification specification, DislDiagram diagram, DislConstraintOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(diagram);
        options ??= new DislConstraintOptions();

        var run = new Run(specification, diagram, options);
        run.Reader();
        if (!run.Unparseable)
        {
            run.DuplicateIds();
            run.Endpoints();
            run.References();
            run.Rules();
        }
        return run.Ordered();
    }

    private sealed class Run(DislSpecification specification, DislDiagram diagram, DislConstraintOptions options)
    {
        private readonly List<(DislFinding Finding, int Reading, int? Position)> _found = [];
        private readonly JsonElement _constraints = specification.Root.TryGetProperty("constraints", out var constraints) && constraints.ValueKind == JsonValueKind.Object ? constraints : default;
        private readonly CelMap _env = (options.Env ?? new DislEnv()).ToCel();

        public bool Unparseable { get; private set; }

        public void Reader()
        {
            var reading = 0;
            foreach (var finding in options.ReaderFindings ?? [])
            {
                var setting = Setting(finding.BuiltIn);
                if (!setting.Enabled) continue;
                Unparseable |= finding.BuiltIn == DislReaderFinding.Unparseable;
                var detail = new CelMap();
                foreach (var (key, value) in finding.Detail) detail[key] = value;
                var variables = Variables(diagram, detail);
                var fallback = detail.TryGetValue("reason", out var reason) && reason is string text ? text : finding.BuiltIn;
                _found.Add((new DislFinding(setting.CodeOf(variables), finding.BuiltIn, setting.Severity, setting.MessageOf(variables, fallback), [], finding.Line), reading++, finding.Line));
            }
        }

        /// <summary><c>std.duplicateId</c>: per written id held by several stored elements, each later holder, in reading order.</summary>
        public void DuplicateIds()
        {
            var setting = Setting("std.duplicateId");
            if (!setting.Enabled) return;
            var stored = diagram.Elements.Where(element => !element.IsDerived && WrittenId(element).Length > 0).OrderBy(element => element.Line ?? int.MaxValue).ToList();
            foreach (var group in stored.GroupBy(WrittenId, StringComparer.Ordinal).Where(group => group.Count() > 1))
            {
                var members = group.ToList();
                foreach (var flagged in Flagged(members, setting))
                {
                    var detail = new CelMap { ["id"] = group.Key, ["first"] = members[0], ["count"] = (long)members.Count };
                    Add(setting, "std.duplicateId", flagged, detail, $"The id '{group.Key}' is declared more than once.", members[0]);
                }
            }
        }

        /// <summary><c>std.endpoints</c>: per stored relation with both ends, an end of a type it does not allow, a loop it does not allow, a parallel it does not allow.</summary>
        public void Endpoints()
        {
            var setting = Setting("std.endpoints");
            if (!setting.Enabled) return;
            var parallels = new Dictionary<(DislType, DislElement, DislElement), List<DislElement>>();
            var violations = new List<(DislElement Relation, string Violation)>();
            foreach (var relation in diagram.Relations.Where(relation => !relation.IsDerived))
            {
                if (relation.Source is not { } source || relation.Target is not { } target) continue;
                var type = relation.Type;
                if (!Allows(type.Source, source)) violations.Add((relation, "source"));
                if (!Allows(type.Target, target)) violations.Add((relation, "target"));
                if (ReferenceEquals(source, target) && !Flag(type, "allowSelfLoops", true)) violations.Add((relation, "selfLoop"));
                if (!Flag(type, "allowParallel", true))
                {
                    var directed = Flag(type, "directed", true);
                    var key = directed || source.GetHashCode() <= target.GetHashCode() ? (type, source, target) : (type, target, source);
                    if (!parallels.TryGetValue(key, out var group)) parallels[key] = group = [];
                    group.Add(relation);
                    if (group.Count > 1) violations.Add((relation, "parallel"));
                }
            }

            foreach (var (relation, violation) in violations)
            {
                var position = relation;
                if (violation == "parallel")
                {
                    var group = parallels.Values.First(members => members.Contains(relation));
                    if (!Flagged(group, setting).Contains(relation)) continue;
                    position = group[0];
                }
                var end = violation is "source" or "target" ? violation : "target";
                var detail = new CelMap { ["violation"] = violation, ["end"] = end, ["relationType"] = relation.Type.Name };
                Add(setting, "std.endpoints", relation, detail, violation switch
                {
                    "selfLoop" => $"A {relation.Type.Name} cannot connect an element to itself.",
                    "parallel" => $"A {relation.Type.Name} already connects these two elements.",
                    _ => $"A {relation.Type.Name} cannot have this {end}.",
                }, position);
            }
        }

        /// <summary><c>std.references</c>: per stored relation, an end that names nothing, the source before the target.</summary>
        public void References()
        {
            var setting = Setting("std.references");
            if (!setting.Enabled) return;
            foreach (var relation in diagram.Relations.Where(relation => !relation.IsDerived))
            {
                if (relation.Source is null) Missing(relation, "source", relation.SourceId);
                if (relation.Target is null && !(relation.TargetId is null && relation.Type.Target is { Optional: true })) Missing(relation, "target", relation.TargetId);
            }

            void Missing(DislElement relation, string end, string? id)
            {
                var detail = new CelMap { ["end"] = end, ["missingId"] = id ?? "", ["attribute"] = end };
                Add(setting, "std.references", relation, detail, $"The {end} '{id}' of this {relation.Type.Name} names nothing.", relation);
            }
        }

        /// <summary>The live invariants, in declaration order, each per scope element in model order.</summary>
        public void Rules()
        {
            if (_constraints.ValueKind != JsonValueKind.Object || !_constraints.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array) return;
            var defaultTiming = _constraints.TryGetProperty("defaults", out var defaults) ? DislJson.Strings(defaults, "timing") : [];
            var defaultSeverity = _constraints.ValueKind == JsonValueKind.Object && defaults.ValueKind == JsonValueKind.Object ? DislJson.String(defaults, "severity") : null;
            var index = 0;
            foreach (var rule in rules.EnumerateArray())
            {
                var pointer = DislJson.Pointer("/constraints/rules", index++);
                if (DislJson.String(rule, "kind") is { } kind && kind != "invariant") continue;
                if (rule.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False) continue;
                var timing = DislJson.Strings(rule, "timing") is { Count: > 0 } own ? own : defaultTiming.Count > 0 ? defaultTiming : ["live", "save"];
                if (!timing.Contains("live")) continue;
                foreach (var self in Scope(rule)) Rule(rule, pointer, self, defaultSeverity);
            }
        }

        private void Rule(JsonElement rule, string pointer, object self, string? defaultSeverity)
        {
            var id = DislJson.String(rule, "id") ?? pointer;
            var code = DislJson.String(rule, "code") ?? id;
            var variables = Variables(self, null);
            if (rule.TryGetProperty("forEach", out var forEach))
            {
                if (DislEvaluation.Expression(specification, forEach, DislJson.Pointer(pointer, "forEach"), DislContexts.Constraint, variables) is not IReadOnlyList<object?> items) return;
                for (var item = 0; item < items.Count; item++)
                {
                    var bound = new Dictionary<string, object?>(variables) { ["item"] = items[item], ["index"] = (long)item };
                    One(bound, items[item] as DislElement);
                }
                return;
            }
            One(variables, null);

            void One(Dictionary<string, object?> bound, DislElement? item)
            {
                if (rule.TryGetProperty("when", out var when) && !DislEvaluation.Holds(specification, when, DislJson.Pointer(pointer, "when"), DislContexts.Constraint, bound)) return;
                var holds = rule.TryGetProperty("rule", out var condition)
                    ? DislEvaluation.Expression(specification, condition, DislJson.Pointer(pointer, "rule"), DislContexts.Constraint, bound)
                    : false;
                if (holds is not false) return;

                var targets = Targets(bound, item);
                var severity = rule.TryGetProperty("severity", out var declared)
                    ? declared.ValueKind == JsonValueKind.String ? declared.GetString()! : DislEvaluation.Expression(specification, declared, DislJson.Pointer(pointer, "severity"), DislContexts.Constraint, bound) as string ?? defaultSeverity ?? "error"
                    : defaultSeverity ?? "error";
                var message = rule.TryGetProperty("message", out var text)
                    ? DislEvaluation.Message(specification, text, DislJson.Pointer(pointer, "message"), DislContexts.Constraint, bound, id)
                    : id;
                var line = targets.Select(element => element.Line).FirstOrDefault(found => found is not null);
                _found.Add((new DislFinding(code, id, severity, message, [.. targets.Select(WrittenId)], line), -1, (Unwrapped(self) ?? item)?.Line ?? line));
            }

            List<DislElement> Targets(Dictionary<string, object?> bound, DislElement? item)
            {
                if (rule.TryGetProperty("target", out var target) && !(target.ValueKind == JsonValueKind.String && target.GetString() == "self"))
                {
                    return DislEvaluation.Expression(specification, target, DislJson.Pointer(pointer, "target"), DislContexts.Constraint, bound) switch
                    {
                        DislElement one => [one],
                        IReadOnlyList<object?> list => [.. list.OfType<DislElement>()],
                        _ => [],
                    };
                }
                if (item is not null) return [item];
                return Unwrapped(self) is { } own ? [own] : [];
            }
        }

        /// <summary>The findings in the order of <c>constraints.x-order</c>, or as they arose.</summary>
        public IReadOnlyList<DislFinding> Ordered()
        {
            var order = _constraints.ValueKind == JsonValueKind.Object && _constraints.TryGetProperty("x-order", out var declared) ? DislJson.Strings(declared) : [];
            if (order.Count == 0) return [.. _found.Select(found => found.Finding)];
            return
            [
                .. _found
                    .Select((found, arising) => (found.Finding, found.Reading, found.Position, Arising: arising))
                    .OrderBy(found => IndexOf(order, found.Finding.Code) is var position and >= 0 ? position : order.Count)
                    .ThenBy(found => found.Reading >= 0 ? 0 : 1)
                    .ThenBy(found => found.Reading >= 0 ? found.Reading : found.Position ?? int.MaxValue)
                    .ThenBy(found => found.Arising)
                    .Select(found => found.Finding),
            ];
        }

        private static int IndexOf(IReadOnlyList<string> list, string value)
        {
            for (var index = 0; index < list.Count; index++)
            {
                if (list[index] == value) return index;
            }
            return -1;
        }

        private IEnumerable<object> Scope(JsonElement rule)
        {
            var scope = DislJson.Strings(rule, "scope");
            if (scope.Count == 0 || scope.Contains("diagram")) return [diagram];
            if (scope.Contains("*")) return diagram.Elements.Select(View);
            return diagram.Elements
                .Where(element => scope.Contains("node") && !element.Type.IsRelation || scope.Contains("relation") && element.Type.IsRelation || scope.Any(element.IsA))
                .Select(View);
        }

        /// <summary>A built-in's finding on <paramref name="self"/>, ordered as <paramref name="position"/> is in the document: a group of duplicates where its first member is.</summary>
        private void Add(BuiltIn setting, string builtIn, DislElement self, CelMap detail, string fallback, DislElement position)
        {
            var variables = Variables(View(self), detail);
            _found.Add((new DislFinding(setting.CodeOf(variables), builtIn, setting.Severity, setting.MessageOf(variables, fallback), [WrittenId(self)], self.Line), -1, position.Line));
        }

        private Dictionary<string, object?> Variables(object self, CelMap? detail)
        {
            var variables = new Dictionary<string, object?> { ["self"] = self, ["diagram"] = diagram, ["env"] = _env };
            if (detail is not null) variables["detail"] = detail;
            return variables;
        }

        private string WrittenId(DislElement element) => options.WrittenId?.Invoke(element) ?? element.Id;

        /// <summary>An element as its findings name it: by its written id when that is not its model id.</summary>
        private object View(DislElement element) => WrittenId(element) is var written && written != element.Id ? new DislWrittenElement(element, written) : element;

        private static DislElement? Unwrapped(object self) => self switch
        {
            DislElement element => element,
            DislWrittenElement written => written.Element,
            _ => null,
        };

        private static IEnumerable<DislElement> Flagged(IReadOnlyList<DislElement> group, BuiltIn setting) => setting.OncePerGroup switch
        {
            "second" => [group[1]],
            "last" => [group[^1]],
            _ => group.Skip(1),
        };

        private static bool Allows(DislRelationEnd? end, DislElement element) =>
            end is null || (end.Types.Count == 0 || end.Types.Any(element.IsA)) && !end.Exclude.Any(element.IsA);

        private static bool Flag(DislType type, string name, bool fallback) =>
            type.Json.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;

        private BuiltIn Setting(string id) => new(specification, id, _constraints.ValueKind == JsonValueKind.Object && _constraints.TryGetProperty("builtIn", out var all) && all.TryGetProperty(id, out var own) ? own : default);
    }

    /// <summary>One built-in's setting (§8.1), with the runtime's defaults.</summary>
    private sealed class BuiltIn(DislSpecification specification, string id, JsonElement json)
    {
        private readonly string _pointer = DislJson.Pointer("/constraints/builtIn", id);

        public bool Enabled => json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("enabled", out var enabled) || enabled.ValueKind != JsonValueKind.False;

        public string Severity => (json.ValueKind == JsonValueKind.Object ? DislJson.String(json, "severity") : null) ?? id switch
        {
            "std.unreadableEntry" or "std.duplicateId" or "std.missingId" => "warning",
            _ => "error",
        };

        public string? OncePerGroup => json.ValueKind == JsonValueKind.Object ? DislJson.String(json, "x-builtIn.oncePerGroup") : null;

        public string CodeOf(IReadOnlyDictionary<string, object?> variables)
        {
            if (json.ValueKind != JsonValueKind.Object) return id;
            if (json.TryGetProperty("x-builtIn.code", out var computed))
            {
                var source = computed.ValueKind == JsonValueKind.Object ? DislJson.String(computed, "cel") : computed.ValueKind == JsonValueKind.String ? computed.GetString() : null;
                if (source is not null && DislEvaluation.Of(specification, DislContexts.BuiltInMessage, source, variables) is string code) return code;
            }
            return DislJson.String(json, "code") ?? id;
        }

        public string MessageOf(IReadOnlyDictionary<string, object?> variables, string fallback) =>
            json.ValueKind == JsonValueKind.Object && json.TryGetProperty("message", out var message)
                ? DislEvaluation.Message(specification, message, DislJson.Pointer(_pointer, "message"), DislContexts.BuiltInMessage, variables, fallback)
                : fallback;
    }
}
