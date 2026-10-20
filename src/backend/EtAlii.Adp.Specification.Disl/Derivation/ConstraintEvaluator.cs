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
/// <c>severity</c>, <c>message</c> and <c>enabled</c> from <c>constraints.builtIn</c> - a <c>code</c>
/// that is a <c>{cel}</c> object is computed per finding in the message's context (DISL 0.3 §8.1),
/// falling back to the built-in's id - and <c>oncePerGroup</c> (DISL 0.3 §8.1; <c>second</c> or
/// <c>last</c>), which reports a group of duplicates once, at that member, rather than once for each
/// member after the first. <c>std.endpoints</c> binds <c>detail.violation</c> (§8.7): <c>source</c>,
/// <c>target</c>, <c>selfLoop</c> or <c>parallel</c>, one finding per limit broken.
/// </para>
/// <para>
/// <b>Order</b>: without <c>constraints.order</c>, §8.6's - the reader's findings, the built-ins,
/// then the rules in declaration order, each by element in model order. With it (DISL 0.3 §8.6, a
/// list of codes), findings sort by the position of their code in it, codes it does not list last;
/// then the reader's findings in reading order, then the rest in document order: by the line of the
/// element a rule was evaluated for, and for a group of duplicates, by the line of its first member.
/// </para>
/// <para>
/// <b>Where a finding is</b> (§8.2, §8.6): a rule's <c>location</c>, a SourceLocation map or
/// <c>null</c>, else the <c>location()</c> of its first target with a line, else of its first
/// target; a built-in's is the element it is about; a reader's is the file and line the reader gave.
/// A rule's <c>subject</c> is evaluated as a display string. A line that is not a positive int is no
/// line.
/// </para>
/// <para>
/// <b>A file that does not parse has no other finding</b> (§8.6): with a <c>std.unparseable</c>
/// finding for the primary file - one naming no file, or the diagram's own - nothing else is
/// evaluated; with one naming another file of a folder, everything is evaluated and only the
/// findings located in that file are dropped.
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
        if (!run.PrimaryUnparseable)
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
        private readonly DislConstraints _constraints = specification.Constraints;
        private readonly CelMap _env = (options.Env ?? new DislEnv()).ToCel();
        private readonly HashSet<string> _unparseable = new(StringComparer.Ordinal);

        /// <summary>Whether the primary file did not parse, so that no constraint is evaluated (§8.6).</summary>
        public bool PrimaryUnparseable { get; private set; }

        public void Reader()
        {
            var reading = 0;
            foreach (var finding in options.ReaderFindings ?? [])
            {
                var setting = Setting(finding.BuiltIn);
                if (!setting.Enabled) continue;
                if (finding.BuiltIn == DislReaderFinding.Unparseable)
                {
                    if (finding.File is null || finding.File == diagram.File) PrimaryUnparseable = true;
                    else _unparseable.Add(finding.File);
                }
                var detail = new CelMap();
                foreach ((string key, object? value) in finding.Detail) detail[key] = value;
                var variables = Variables(diagram, detail);
                var fallback = detail.TryGetValue("reason", out var reason) && reason is string text ? text : finding.BuiltIn;
                _found.Add((new DislFinding(setting.CodeOf(variables), finding.BuiltIn, setting.Severity, setting.MessageOf(variables, fallback), [], finding.Line, finding.File), reading++, finding.Line));
            }
        }

        /// <summary><c>std.duplicateId</c>: per written id held by several stored elements, each later holder, in reading order.</summary>
        public void DuplicateIds()
        {
            var setting = Setting("std.duplicateId");
            if (!setting.Enabled) return;
            var stored = diagram.Elements.Where(element => !element.IsDerived && WrittenId(element).Length > 0).OrderBy(element => element.Line ?? int.MaxValue).ToList();
            foreach (var group in stored.GroupBy(WrittenId, specification.IdComparer).Where(group => group.Count() > 1))
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

            foreach ((DislElement relation, string violation) in violations)
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
            if (_constraints.Rules is not { } rules) return;
            var defaultTiming = _constraints.DefaultTiming;
            foreach (var rule in rules)
            {
                if (rule.Kind is { } kind && kind != "invariant") continue;
                // An invariant that is not an object threw here when it was read from the JSON at run time, and still does.
                if (rule.NotAnObject is { } malformed) throw new InvalidOperationException(malformed);
                if (!rule.Enabled) continue;
                var timing = rule.Timing is { Count: > 0 } own ? own : defaultTiming.Count > 0 ? defaultTiming : ["live", "save"];
                if (!timing.Contains("live")) continue;
                foreach (var self in Scope(rule)) Rule(rule, self, _constraints.DefaultSeverity);
            }
        }

        private void Rule(DislConstraintRule rule, object self, string? defaultSeverity)
        {
            var variables = Variables(self, null);
            if (rule.ForEach is { } forEach)
            {
                if (forEach.Evaluate(variables) is not IReadOnlyList<object?> items) return;
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
                if (rule.When is { } when && !when.Holds(bound)) return;
                var holds = rule.Rule is { } condition ? condition.Evaluate(bound) : false;
                if (holds is not false) return;

                var targets = Targets(bound, item);
                var severity = rule.SeverityText ?? rule.Severity?.Evaluate(bound) as string ?? defaultSeverity ?? "error";
                var message = rule.Message?.Text(bound, rule.Id) ?? rule.Id;
                var at = targets.FirstOrDefault(element => element.Line is not null) ?? targets.FirstOrDefault();
                (string? file, int? line) = rule.Location is { } location
                    ? Located(location.Evaluate(bound), at)
                    : at?.Location ?? (null, null);
                var subject = rule.Subject?.Evaluate(bound) as string;
                var ordered = (Unwrapped(self) ?? item)?.Line ?? at?.Line;
                _found.Add((new DislFinding(rule.Code, rule.Id, severity, message, [.. targets.Select(WrittenId)], line, file, subject), -1, ordered));
            }

            List<DislElement> Targets(Dictionary<string, object?> bound, DislElement? item)
            {
                if (rule.Target is { } target)
                {
                    return target.Evaluate(bound) switch
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

        /// <summary>
        /// A rule's evaluated <c>location</c> (§8.2): a SourceLocation map, optional or not, gives its
        /// file and line; <c>null</c> or <c>optional.none()</c> gives none. One that fails, or that
        /// names no file, proves nothing, and the finding is located at <paramref name="target"/>.
        /// </summary>
        private static (string? File, int? Line) Located(object? value, DislElement? target)
        {
            if (value is CelOptional optional) value = optional.HasValue ? optional.Value : null;
            if (value is null) return (null, null);
            if (value is not IReadOnlyDictionary<string, object?> map || !map.TryGetValue("file", out var file) || file is not string named) return target?.Location ?? (null, null);
            return (named, map.TryGetValue("line", out var line) && line is long and >= 1 and <= int.MaxValue and var number ? (int)number : null);
        }

        /// <summary>
        /// The findings in the order of <c>constraints.order</c>, or as they arose, without those
        /// located in a file of the folder that did not parse (§8.6), that file's own finding apart.
        /// </summary>
        public IReadOnlyList<DislFinding> Ordered()
        {
            var kept = _unparseable.Count == 0
                ? _found
                : [.. _found.Where(entry => entry.Finding.ConstraintId == DislReaderFinding.Unparseable || entry.Finding.File is not { } file || !_unparseable.Contains(file))];
            var order = _constraints.Order;
            if (order.Count == 0) return [.. kept.Select(entry => entry.Finding)];
            return
            [
                .. kept
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

        private IEnumerable<object> Scope(DislConstraintRule rule)
        {
            var scope = rule.Scope;
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
            _found.Add((new DislFinding(setting.CodeOf(variables), builtIn, setting.Severity, setting.MessageOf(variables, fallback), [WrittenId(self)], self.Line, self.Location.File), -1, position.Line));
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

        private BuiltIn Setting(string id) => new(id, _constraints.BuiltIn(id));
    }

    /// <summary>One built-in's setting (§8.1), with the runtime's defaults.</summary>
    private sealed class BuiltIn(string id, DislBuiltIn setting)
    {
        public bool Enabled => setting.Enabled;

        public string Severity => setting.Severity ?? id switch
        {
            "std.unreadableEntry" or "std.duplicateId" or "std.missingId" => "warning",
            _ => "error",
        };

        public string? OncePerGroup => setting.OncePerGroup;

        public string CodeOf(IReadOnlyDictionary<string, object?> variables) =>
            setting.CodeText ?? (setting.Code?.Evaluate(variables) is string computed ? computed : id);

        public string MessageOf(IReadOnlyDictionary<string, object?> variables, string fallback) =>
            setting.Message?.Text(variables, fallback) ?? fallback;
    }
}
