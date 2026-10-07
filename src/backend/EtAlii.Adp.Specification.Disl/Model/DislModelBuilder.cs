using System.Text.Json;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// What a model built from a reading holds: the diagram, every finding (the reader's first, in
/// reading order, then the runtime's, §8.6), and the elements kept aside - the header and the entries
/// the reader could not read.
/// </summary>
/// <param name="IsUnreadable">Whether the body could not be read at all; the diagram is then empty.</param>
public sealed record DislModel(DislDiagram Diagram, IReadOnlyList<Finding> Findings, IReadOnlyList<FblElement> Header, IReadOnlyList<FblElement> Unreadable, bool IsUnreadable);

/// <summary>
/// Builds the DISL model of a format binding's reading (DISL §4.11, §11.5, §14.2): the stored elements
/// as the binding read them, typed by the metamodel, then the derived ids and the derived relations.
/// </summary>
/// <remarks>
/// <para>
/// <b>The binding's types are mapped by the specification's <c>persistence.typeMap</c></b> when it
/// has one: a type <c>as</c> a metamodel type (with <c>attributes</c> renamed, <c>source</c> and
/// <c>target</c> taking a relation's ends, and <c>hostAttributes</c> kept beside the model), <c>as</c>
/// <c>diagram</c> for diagram attributes, <c>as</c> <c>header</c> or <c>unreadable</c> to keep the
/// element aside. Without a map, a binding type is the metamodel type of the same name.
/// </para>
/// <para>
/// <b>A value is typed by its attribute</b> (<see cref="DislValues.TryFromRead"/>): one that does not
/// fit stays unset, so it reads as its default, as the code it replaces falls back.
/// </para>
/// </remarks>
public static class DislModelBuilder
{
    /// <summary>Two derived elements with one id, or a derived id equal to a stored one (§4.11.5).</summary>
    public const string DerivedId = "std.derivedId";

    /// <summary>Items of a derived relation type whose ends its declaration does not allow (§4.11.3).</summary>
    public const string DerivedEnds = "std.derivedEnds";

    /// <summary>A derived type whose <c>from</c> or per-item expressions failed (§4.11.5).</summary>
    public const string DerivedFailed = "std.derivedFailed";

    /// <summary>The model of <paramref name="reading"/> under <paramref name="specification"/>.</summary>
    public static DislModel From(FblModel reading, DislSpecification specification)
    {
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(specification);

        var diagram = new DislDiagram(specification);
        List<Finding> findings = [.. reading.Findings];
        List<FblElement> header = [];
        List<FblElement> unreadable = [];
        if (reading.Unreadable) return new DislModel(diagram, findings, header, unreadable, true);

        var map = DislTypeMap.Of(specification);
        var metamodel = specification.Metamodel;
        var built = new Dictionary<FblElement, DislElement>(ReferenceEqualityComparer.Instance);
        var pending = new List<(FblElement Element, DislTypeMapping Mapping, DislType Type)>();

        foreach (var element in reading.Elements)
        {
            var mapping = map.For(element.Type);
            switch (mapping.As)
            {
                case "header":
                    header.Add(element);
                    continue;
                case "diagram":
                    foreach ((string name, object? raw) in element.Attributes)
                    {
                        var target = mapping.Attributes.GetValueOrDefault(name, name);
                        if (metamodel.DiagramAttributes.TryGetValue(target, out var attribute) && DislValues.TryFromRead(raw, attribute, metamodel, out var value))
                        {
                            diagram.SetAttribute(target, value);
                        }
                    }
                    continue;
            }
            if (mapping.As == "unreadable" || metamodel.TypeOf(mapping.As) is not { Abstract: false } type)
            {
                unreadable.Add(element);
                continue;
            }
            pending.Add((element, mapping, type));
        }

        // Nodes in reading order, each after its parent; then relations, whose ends are nodes.
        foreach ((FblElement element, DislTypeMapping mapping, DislType type) in pending.Where(entry => !entry.Type.IsRelation))
        {
            Node(element, mapping, type);
        }
        foreach ((FblElement element, DislTypeMapping mapping, DislType type) in pending.Where(entry => entry.Type.IsRelation))
        {
            var attributes = Attributes(element, mapping, type, metamodel, out var host, out var ends);
            var sourceId = ends.GetValueOrDefault("source") ?? element.Source;
            var targetId = ends.GetValueOrDefault("target") ?? element.Target;
            diagram.Add(new DislElement(diagram, type, element.Id, attributes)
            {
                Source = End(sourceId),
                Target = End(targetId),
                SourceId = sourceId,
                TargetId = targetId,
                HostAttributes = host,
                Line = element.Line,
                IdIsStored = element.IdIsStored,
            });
        }

        findings.AddRange(Complete(diagram));
        return new DislModel(diagram, findings, header, unreadable, false);

        DislElement Node(FblElement element, DislTypeMapping mapping, DislType type)
        {
            if (built.TryGetValue(element, out var done)) return done;
            var parent = element.ParentId is { } parentId && pending.FirstOrDefault(entry => entry.Element.Id == parentId) is { Element: not null } owner && !owner.Type.IsRelation
                ? Node(owner.Element, owner.Mapping, owner.Type)
                : null;
            var attributes = Attributes(element, mapping, type, metamodel, out var host, out _);
            var node = diagram.Add(new DislElement(diagram, type, element.Id, attributes)
            {
                Parent = parent,
                Slot = parent is null ? null : element.ParentSlot,
                HostAttributes = host,
                Line = element.Line,
                IdIsStored = element.IdIsStored,
            });
            built[element] = node;
            return node;
        }

        // A relation end names a stored id (an FBL reference by id) or an FBL element's id, which is the
        // node's; the first element with it keeps it (§11.5.4).
        DislElement? End(string? id) => id is null ? null : diagram.ElementById(id);
    }

    /// <summary>
    /// Completes a diagram whose stored elements are in place (§14.2): the derived ids of its nodes,
    /// ancestors first, then its derived relations, then the derived ids of its stored relations.
    /// </summary>
    /// <returns>The runtime's findings, in the order they arose.</returns>
    public static IReadOnlyList<Finding> Complete(DislDiagram diagram)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        List<Finding> findings = [];
        DerivedIds(diagram, diagram.Nodes, findings);
        DerivedRelations(diagram, findings);
        DerivedIds(diagram, [.. diagram.Relations.Where(relation => !relation.IsDerived)], findings);
        return findings;
    }

    /// <summary>The ids of the <c>derived</c> strategy (§11.5.2), in the identity context, in the order given.</summary>
    private static void DerivedIds(DislDiagram diagram, IReadOnlyList<DislElement> elements, List<Finding> findings)
    {
        var specification = diagram.Specification;
        var programs = new Dictionary<string, CelProgram>(StringComparer.Ordinal);
        var computed = new Dictionary<string, DislElement>(StringComparer.Ordinal);
        foreach (var element in elements)
        {
            var rule = specification.IdRuleOf(element.Type.Name);
            if (rule is not { Strategy: "derived", Expression: { } expression }) continue;

            if (!programs.TryGetValue(expression, out var program))
            {
                program = programs[expression] = specification.Environment(DislContexts.Identity).Compile(expression);
            }
            var value = program.Evaluate(new Dictionary<string, object?> { ["self"] = element, ["diagram"] = diagram, ["env"] = null });
            if (value is not string id)
            {
                var why = value is CelError error ? error.Message : "it did not give a string";
                findings.Add(new Finding(FindingCodes.MissingId, FindingSeverity.Warning, $"The id of this {element.Type.Name} could not be computed: {why}", null));
                element.IdIsStored = false;
                continue;
            }

            element.Id = id;
            element.IdIsStored = false;
            if (!computed.TryAdd(id, element))
            {
                findings.Add(new Finding(FindingCodes.DuplicateId, FindingSeverity.Warning, $"The id '{id}' is computed for two elements; the first keeps it.", null));
            }
        }
    }

    /// <summary>The derived relation types (§4.11.3), in the order of <c>metamodel.relations</c>.</summary>
    private static void DerivedRelations(DislDiagram diagram, List<Finding> findings)
    {
        var specification = diagram.Specification;
        foreach (var type in specification.Metamodel.Relations.Values)
        {
            if (type.Derived is not { ValueKind: JsonValueKind.Object } derived) continue;
            var pointer = DislJson.Pointer(DislJson.Pointer("/metamodel/relations", type.Name), "derived");
            if (derived.TryGetProperty("key", out _))
            {
                findings.Add(new Finding(DerivedFailed, FindingSeverity.Warning, $"{type.Name} groups its items by key, which this runtime does not compute yet.", null));
                continue;
            }

            var variables = new Dictionary<string, object?> { ["diagram"] = diagram, ["env"] = null };
            if (Evaluate(specification, DislJson.Pointer(pointer, "from"), variables) is not IReadOnlyList<object?> items)
            {
                findings.Add(new Finding(DerivedFailed, FindingSeverity.Warning, $"The items of {type.Name} could not be computed, so there are none.", null));
                continue;
            }

            var failures = new HashSet<string>(StringComparer.Ordinal);
            var badEnds = new Dictionary<string, int>(StringComparer.Ordinal);
            var repeats = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var index = 0; index < items.Count; index++)
            {
                var item = new Dictionary<string, object?>(variables) { ["item"] = items[index], ["group"] = new List<object?> { items[index] }, ["index"] = (long)index };
                var source = Evaluate(specification, DislJson.Pointer(pointer, "source"), item);
                var target = Evaluate(specification, DislJson.Pointer(pointer, "target"), item);
                if (source is CelError || target is CelError)
                {
                    failures.Add(((source as CelError) ?? (CelError)target!).Message);
                    continue;
                }
                if (!Allows(type.Source, source)) { badEnds["source"] = badEnds.GetValueOrDefault("source") + 1; continue; }
                if (!Allows(type.Target, target)) { badEnds["target"] = badEnds.GetValueOrDefault("target") + 1; continue; }
                var from = (DislElement)source!;
                var to = (DislElement)target!;

                string id;
                if (derived.TryGetProperty("id", out _))
                {
                    if (Evaluate(specification, DislJson.Pointer(pointer, "id"), item) is not string written)
                    {
                        failures.Add("the id of an item is not a string");
                        continue;
                    }
                    id = written;
                }
                else
                {
                    var plain = $"{type.Name}:{from.Id}->{to.Id}";
                    var seen = repeats[plain] = repeats.GetValueOrDefault(plain) + 1;
                    id = seen == 1 ? plain : $"{plain}#{seen}";
                }

                IReadOnlyList<DislElement> sources = items[index] is DislElement element ? [element] : [];
                if (derived.TryGetProperty("sources", out _))
                {
                    if (Evaluate(specification, DislJson.Pointer(pointer, "sources"), item) is not IReadOnlyList<object?> listed)
                    {
                        failures.Add("the sources of an item are not a list");
                        continue;
                    }
                    sources = [.. listed.OfType<DislElement>()];
                }

                var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var attribute in DislJson.Members(derived, "attributes"))
                {
                    attributes[attribute.Name] = Evaluate(specification, DislJson.Pointer(DislJson.Pointer(pointer, "attributes"), attribute.Name), item);
                }

                if (diagram.ElementById(id) is { } taken)
                {
                    findings.Add(new Finding(DerivedId, FindingSeverity.Warning, $"{type.Name} '{id}' has the id of {taken}; it is not drawn.", null));
                    continue;
                }
                diagram.Add(new DislElement(diagram, type, id, attributes)
                {
                    Source = from,
                    Target = to,
                    SourceId = from.Id,
                    TargetId = to.Id,
                    IsDerived = true,
                    Sources = sources,
                    IdIsStored = false,
                });
            }

            foreach (var failure in failures)
            {
                findings.Add(new Finding(DerivedFailed, FindingSeverity.Warning, $"Items of {type.Name} were dropped: {failure}", null));
            }
            foreach ((string end, int count) in badEnds)
            {
                findings.Add(new Finding(DerivedEnds, FindingSeverity.Warning, $"{count} item(s) of {type.Name} were dropped: their {end} is not one its declaration allows.", null));
            }
        }
    }

    private static bool Allows(DislRelationEnd? end, object? value) =>
        value is DislElement { Type.IsRelation: false } element
        && (end is null || end.Types.Count == 0 || end.Types.Any(element.IsA))
        && (end is null || !end.Exclude.Any(element.IsA));

    private static object? Evaluate(DislSpecification specification, string pointer, IReadOnlyDictionary<string, object?> variables) =>
        specification.ExpressionAt(pointer) is { } expression
            ? expression.Program.Evaluate(variables)
            : new CelError($"No expression was compiled at {pointer}.");

    /// <summary>A binding element's attributes: the metamodel's typed, the ends named, the rest kept as read.</summary>
    private static Dictionary<string, object?> Attributes(
        FblElement element,
        DislTypeMapping mapping,
        DislType type,
        DislMetamodel metamodel,
        out Dictionary<string, object?> host,
        out Dictionary<string, string?> ends)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        host = new Dictionary<string, object?>(StringComparer.Ordinal);
        ends = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach ((string name, object? raw) in element.Attributes)
        {
            var target = mapping.Attributes.GetValueOrDefault(name, name);
            if (type.IsRelation && target is "source" or "target")
            {
                ends[target] = raw as string ?? raw?.ToString();
            }
            else if (!mapping.HostAttributes.Contains(name) && type.Attributes.TryGetValue(target, out var attribute))
            {
                if (DislValues.TryFromRead(raw, attribute, metamodel, out var value)) attributes[target] = value;
            }
            else
            {
                host[name] = raw;
            }
        }
        return attributes;
    }
}
