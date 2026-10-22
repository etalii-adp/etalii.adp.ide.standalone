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
/// <para>
/// <b>Under the <c>natural</c> strategy a type's <c>prefix</c> is applied</b> (§11.5, §11.5.1): an
/// element of a type that declares one has that prefix followed by its natural id - the id a node
/// was read with, or for a relation read without an id its ends as read joined by <c>|</c> - and an
/// element of a type that declares none keeps the id it was read with. A relation's ends are found
/// by the ids they were read with, and kept as read in <c>SourceId</c> and <c>TargetId</c>; a reference
/// attribute is stored as the model id of the node it names, so it resolves under a prefix too.
/// </para>
/// </remarks>
public static class DislModelBuilder
{
    /// <summary>Two derived elements with one id, or a derived id equal to a stored one (§4.11.5).</summary>
    public const string DerivedId = "std.derivedId";

    /// <summary>Items of a derived relation type whose ends its declaration does not allow (§4.11.3).</summary>
    private const string DerivedEnds = "std.derivedEnds";

    /// <summary>A derived type whose <c>from</c> or per-item expressions failed (§4.11.5).</summary>
    public const string DerivedFailed = "std.derivedFailed";

    /// <summary>Derived nodes whose computed parent does not allow them, drawn at the top level instead (§4.11.2).</summary>
    public const string Containment = "std.containment";

    /// <summary>The model of <paramref name="reading"/> under <paramref name="specification"/>, built headless.</summary>
    public static DislModel From(FblModel reading, DislSpecification specification) => From(reading, specification, new DislEnv());

    /// <summary>
    /// The model of <paramref name="reading"/> under <paramref name="specification"/>, with its derived
    /// elements and attributes computed under <paramref name="env"/>: for the viewpoint it names. The
    /// diagram's <see cref="DislDiagram.File"/> is <paramref name="file"/> from the start, so that an
    /// expression computed while the model is built - a derived id, a derived element - reads an
    /// element's <c>location()</c> (§12.2) as the findings later do.
    /// </summary>
    public static DislModel From(FblModel reading, DislSpecification specification, DislEnv env, string? file = null)
    {
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(env);

        var diagram = new DislDiagram(specification) { Env = env, File = file };
        List<Finding> findings = [.. reading.Findings];
        List<FblElement> header = [];
        List<FblElement> unreadable = [];
        if (reading.Unreadable) return new DislModel(diagram, findings, header, unreadable, true);

        var typed = Typed(reading, diagram, header, unreadable);
        new StoredElements(diagram, typed).Add();
        findings.AddRange(Complete(diagram));
        return new DislModel(diagram, findings, header, unreadable, false);
    }

    /// <summary>
    /// The elements of <paramref name="reading"/> the type map makes model elements, in reading order, each with its
    /// mapping and metamodel type; a <c>header</c> element is kept in <paramref name="header"/>, a <c>diagram</c>
    /// element's attributes are set on <paramref name="diagram"/>, and an element of no type the model can hold is kept
    /// in <paramref name="unreadable"/>.
    /// </summary>
    private static List<(FblElement Element, DislTypeMapping Mapping, DislType Type)> Typed(FblModel reading, DislDiagram diagram, List<FblElement> header, List<FblElement> unreadable)
    {
        var map = diagram.Specification.TypeMap;
        var metamodel = diagram.Specification.Metamodel;
        var typed = new List<(FblElement Element, DislTypeMapping Mapping, DislType Type)>();
        foreach (var element in reading.Elements)
        {
            var mapping = map.For(element.Type);
            switch (mapping.As)
            {
                case "header":
                    header.Add(element);
                    continue;
                case "diagram":
                    DiagramAttributes(diagram, element, mapping);
                    continue;
            }
            if (mapping.As == "unreadable" || metamodel.TypeOf(mapping.As) is not { Abstract: false } type)
            {
                unreadable.Add(element);
                continue;
            }
            typed.Add((element, mapping, type));
        }
        return typed;
    }

    /// <summary>The attributes of an element mapped <c>as</c> <c>diagram</c>, set on the diagram where they are diagram attributes that fit.</summary>
    private static void DiagramAttributes(DislDiagram diagram, FblElement element, DislTypeMapping mapping)
    {
        var metamodel = diagram.Specification.Metamodel;
        foreach ((string name, object? raw) in element.Attributes)
        {
            var target = mapping.Attributes.GetValueOrDefault(name, name);
            if (metamodel.DiagramAttributes.TryGetValue(target, out var attribute) && DislValues.TryFromRead(raw, attribute, metamodel, out var value))
            {
                diagram.SetAttribute(target, value);
            }
        }
    }

    /// <summary>The file a folder subject's plugin read the element in (FBL §11.2); null for an element of the primary file.</summary>
    private static string? FileOf(FblElement element) => string.IsNullOrEmpty(element.File) ? null : element.File;

    /// <summary>
    /// The id of <paramref name="element"/>: under the <c>natural</c> strategy, the prefix its type
    /// declares followed by its natural id (§11.5.1) - a relation read without an id is named by its
    /// ends as read, joined by <c>|</c> - and otherwise, or without a prefix, the id it was read with.
    /// </summary>
    private static string NaturalId(DislSpecification specification, DislType type, FblElement element, string? sourceId, string? targetId)
    {
        if (specification.IdRuleOf(type.Name) is not { Strategy: "natural", Prefix: { } prefix }) return element.Id;
        return type.IsRelation && !element.IdIsStored
            ? prefix + sourceId + "|" + targetId
            : prefix + element.Id;
    }

    /// <summary>
    /// Completes a diagram whose stored elements are in place (§14.2): the derived ids of its nodes,
    /// ancestors first, then its derived node types and its derived relations (§4.11.5), then the
    /// derived ids of its stored relations.
    /// </summary>
    /// <returns>The runtime's findings, in the order they arose.</returns>
    public static IReadOnlyList<Finding> Complete(DislDiagram diagram)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        List<Finding> findings = [];
        DerivedIds(diagram, diagram.Nodes, findings);
        DerivedNodes(diagram, findings);
        DerivedRelations(diagram, findings);
        DerivedIds(diagram, [.. diagram.Relations.Where(relation => !relation.IsDerived)], findings);
        return findings;
    }

    /// <summary>
    /// The ids of the <c>derived</c> strategy (§11.5.2), in the identity context, in the order given;
    /// an id computed twice is reported as <c>std.duplicateId</c> unless the specification switches that built-in off (§8.1).
    /// </summary>
    private static void DerivedIds(DislDiagram diagram, IReadOnlyList<DislElement> elements, List<Finding> findings)
    {
        var specification = diagram.Specification;
        var computed = new Dictionary<string, DislElement>(specification.IdComparer);
        var reportsDuplicates = specification.Constraints.BuiltIn(FindingCodes.DuplicateId).Enabled;
        foreach (var element in elements)
        {
            var rule = specification.IdRuleOf(element.Type.Name);
            if (rule is not { Strategy: "derived", Expression: { } expression }) continue;

            // An expression that does not compile gives no id, as one that fails does: a rule the loader
            // walks cannot reach here uncompiled, but one under a type whose name its walk passes over
            // (doc, for one) can.
            var value = specification.Programs.TryGet(DislProgramKey.Of(DislContext.Identity, expression, []), out var program, out var problem)
                ? program.Evaluate(new Dictionary<string, object?> { ["self"] = element, ["diagram"] = diagram, ["env"] = diagram.Env.ToCel() })
                : new CelError(problem);
            if (value is not string id)
            {
                var why = value is CelError error ? error.Message : "it did not give a string";
                findings.Add(new Finding(FindingCodes.MissingId, FindingSeverity.Warning, $"The id of this {element.Type.Name} could not be computed: {why}", null));
                element.IdIsStored = false;
                continue;
            }

            element.Id = id;
            element.IdIsStored = false;
            if (!computed.TryAdd(id, element) && reportsDuplicates)
            {
                findings.Add(new Finding(FindingCodes.DuplicateId, FindingSeverity.Warning, $"The id '{id}' is computed for two elements; the first keeps it.", null));
            }
        }
    }

    /// <summary>
    /// The derived node types (§4.11.2), in the order of <c>metamodel.types</c>: each type's items, grouped by its
    /// <c>key</c>, each group giving one node with its <c>id</c>, <c>attributes</c>, <c>parent</c>, <c>slot</c> and
    /// <c>sources</c>. Each type's <c>from</c> sees the stored elements and the derived nodes of the types before it.
    /// </summary>
    private static void DerivedNodes(DislDiagram diagram, List<Finding> findings)
    {
        var taken = new Dictionary<string, DislElement>(diagram.Specification.IdComparer);
        foreach (var element in diagram.Elements) taken.TryAdd(element.Id, element);
        foreach (var type in diagram.Specification.Metamodel.Types.Values)
        {
            if (type is not { DerivedNode: { } derived, Abstract: false }) continue;
            var run = new DerivedItems(diagram, type, findings);
            if (derived.From.Evaluate(run.Variables) is not IReadOnlyList<object?> items)
            {
                run.NoItems();
                continue;
            }

            var groups = Groups(derived.Key, run, items);
            for (var index = 0; index < groups.Count; index++)
            {
                var group = groups[index];
                DerivedNode(run, derived, group, new Dictionary<string, object?>(run.Variables) { ["item"] = group[0], ["group"] = group, ["index"] = (long)index }, taken);
            }
            run.Report();
        }
    }

    /// <summary>The node one group of a derived node type gives, bound as <paramref name="item"/>; none when it is dropped.</summary>
    private static void DerivedNode(DerivedItems run, DislDerivedNode derived, List<object?> group, Dictionary<string, object?> item, Dictionary<string, DislElement> taken)
    {
        if (derived.Id.Evaluate(item) is not string id)
        {
            run.Fail("the id of an item is not a string");
            return;
        }

        // A failing per-item expression drops the item (§4.11.5).
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string name, DislDerivedField value) in derived.Attributes)
        {
            var computed = value.Evaluate(item);
            if (computed is CelError error)
            {
                run.Fail(error.Message);
                return;
            }
            attributes[name] = computed;
        }

        // Without sources, the group when its items are elements (§4.11.2).
        IReadOnlyList<DislElement> sources = group.All(member => member is DislElement) ? [.. group.Cast<DislElement>()] : [];
        if (derived.Sources is { } derivedSources)
        {
            if (derivedSources.Evaluate(item) is not IReadOnlyList<object?> list)
            {
                run.Fail("the sources of an item are not a list");
                return;
            }
            sources = [.. list.OfType<DislElement>()];
        }

        DislElement? parent = null;
        if (derived.Parent?.Evaluate(item) is { } computedParent)
        {
            if (computedParent is CelError error)
            {
                run.Fail(error.Message);
                return;
            }
            parent = computedParent as DislElement;
            if (parent is { Type.IsRelation: true } || parent is not null && !parent.Type.ChildTypes.Any(child => run.Type.Linearisation.Contains(child)))
            {
                run.Uncontained();
                parent = null;
            }
        }
        var slot = parent is null ? null : derived.Slot?.Evaluate(item) as string;

        if (taken.TryGetValue(id, out var holder))
        {
            run.Taken(id, holder);
            return;
        }
        taken[id] = run.Diagram.Add(new DislElement(run.Diagram, run.Type, id, attributes)
        {
            Parent = parent,
            Slot = slot,
            IsDerived = true,
            Sources = sources,
            IdIsStored = false,
        });
    }

    /// <summary>The derived relation types (§4.11.3), in the order of <c>metamodel.relations</c>.</summary>
    private static void DerivedRelations(DislDiagram diagram, List<Finding> findings)
    {
        foreach (var type in diagram.Specification.Metamodel.Relations.Values)
        {
            switch (type.DerivedRelation)
            {
                case DislDerivedRelation.Listed listed:
                    ExpressionForm(new DerivedItems(diagram, type, findings), listed.Items);
                    break;
                case DislDerivedRelation.Mapped mapped:
                    ObjectForm(new DerivedItems(diagram, type, findings), mapped);
                    break;
            }
        }
    }

    /// <summary>
    /// A derived relation type in the object form (§4.11.3): its items, grouped by its <c>key</c>, each group giving
    /// one relation from its <c>source</c> to its <c>target</c>, with its <c>id</c>, <c>sources</c> and <c>attributes</c>.
    /// </summary>
    private static void ObjectForm(DerivedItems run, DislDerivedRelation.Mapped derived)
    {
        if (derived.From.Evaluate(run.Variables) is not IReadOnlyList<object?> items)
        {
            run.NoItems();
            return;
        }

        var groups = Groups(derived.Key, run, items);
        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            ObjectFormItem(run, derived, group, new Dictionary<string, object?>(run.Variables) { ["item"] = group[0], ["group"] = group, ["index"] = (long)index });
        }
        run.Report();
    }

    /// <summary>The relation one group of the object form gives, bound as <paramref name="item"/>; none when it is dropped.</summary>
    private static void ObjectFormItem(DerivedItems run, DislDerivedRelation.Mapped derived, List<object?> group, Dictionary<string, object?> item)
    {
        var source = derived.Source.Evaluate(item);
        var target = derived.Target.Evaluate(item);
        if (source is CelError || target is CelError)
        {
            run.Fail(((source as CelError) ?? (CelError)target!).Message);
            return;
        }
        if (!run.Ends(source, target, out var from, out var to)) return;

        string id;
        if (derived.Id is { } written)
        {
            if (written.Evaluate(item) is not string text)
            {
                run.Fail("the id of an item is not a string");
                return;
            }
            id = text;
        }
        else id = run.PlainId(from, to);

        // Without sources, the group when its items are elements (§4.11.3).
        IReadOnlyList<DislElement> sources = group.All(member => member is DislElement) ? [.. group.Cast<DislElement>()] : [];
        if (derived.Sources is { } derivedSources)
        {
            if (derivedSources.Evaluate(item) is not IReadOnlyList<object?> list)
            {
                run.Fail("the sources of an item are not a list");
                return;
            }
            sources = [.. list.OfType<DislElement>()];
        }

        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string name, DislDerivedField value) in derived.Attributes)
        {
            attributes[name] = value.Evaluate(item);
        }
        run.Add(id, from, to, attributes, sources);
    }

    /// <summary>
    /// The items of a derived type grouped by its <c>key</c> (§4.11.2, §4.11.3): items with equal keys (CEL
    /// <c>==</c>) are one group, the groups in the order of their first item; without a key every item
    /// is a group of its own. An item whose key fails, or is not a string, number, bool or list of
    /// those, is dropped. Keys are looked up by hash, so grouping is linear in the items.
    /// </summary>
    private static List<List<object?>> Groups(DislDerivedField? keyed, DerivedItems run, IReadOnlyList<object?> items)
    {
        if (keyed is null) return [.. items.Select(item => new List<object?> { item })];

        var groups = new List<List<object?>>();
        var byKey = new Dictionary<object, List<object?>>(KeyComparer.Instance);
        for (var index = 0; index < items.Count; index++)
        {
            var bound = new Dictionary<string, object?>(run.Variables) { ["item"] = items[index], ["group"] = new List<object?> { items[index] }, ["index"] = (long)index };
            var key = keyed.Evaluate(bound);
            if (key is CelError error)
            {
                run.Fail(error.Message);
                continue;
            }
            if (!IsKey(key))
            {
                run.Fail("the key of an item is not a string, number, bool or list of those");
                continue;
            }
            if (byKey.TryGetValue(key!, out var members))
            {
                members.Add(items[index]);
            }
            else
            {
                members = [items[index]];
                byKey[key!] = members;
                groups.Add(members);
            }
        }
        return groups;

        static bool IsKey(object? key) => IsScalar(key) || key is IReadOnlyList<object?> list && list.All(IsScalar);

        static bool IsScalar(object? key) => key is string or long or double or bool;
    }

    /// <summary>
    /// Keys compared as CEL's <c>==</c> compares them (<see cref="CelEquality"/>), hashed so that keys it calls equal
    /// hash alike: a number by its value as a double, so that an int and a double of one value meet, a list by its members.
    /// </summary>
    private sealed class KeyComparer : IEqualityComparer<object>
    {
        public static KeyComparer Instance { get; } = new();

        bool IEqualityComparer<object>.Equals(object? x, object? y) => CelEquality.Equal(x, y);

        int IEqualityComparer<object>.GetHashCode(object key) => Hash(key);

        private static int Hash(object? key) => key switch
        {
            string text => StringComparer.Ordinal.GetHashCode(text),
            long number => ((double)number).GetHashCode(),
            double number => number.GetHashCode(),
            bool flag => flag.GetHashCode(),
            IReadOnlyList<object?> list => list.Aggregate(17, (hash, member) => unchecked((hash * 31) + Hash(member))),
            _ => 0,
        };
    }

    /// <summary>
    /// A derived relation type in the expression form (§4.9): an Expression in the <c>derive</c> context
    /// returning a list of maps, each with <c>source</c> and <c>target</c>, yielding one relation. A key
    /// naming an attribute of the type gives that attribute's value, <c>id</c> and <c>sources</c> mean
    /// what they mean in the object form, and any other key is ignored; without an <c>id</c> the id is
    /// <c>&lt;Type&gt;:&lt;source.id&gt;-&gt;&lt;target.id&gt;</c>, with <c>#2</c>, <c>#3</c> for repeats.
    /// </summary>
    private static void ExpressionForm(DerivedItems run, DislDerivedField derived)
    {
        if (derived.Evaluate(run.Variables) is not IReadOnlyList<object?> items)
        {
            run.NoItems();
            return;
        }

        foreach (var item in items)
        {
            ExpressionFormItem(run, item);
        }
        run.Report();
    }

    /// <summary>The relation one item of the expression form gives; none when it is dropped.</summary>
    private static void ExpressionFormItem(DerivedItems run, object? item)
    {
        if (item is not IReadOnlyDictionary<string, object?> map || !map.TryGetValue("source", out var source) || !map.TryGetValue("target", out var target))
        {
            run.Fail("an item is not a map with a source and a target");
            return;
        }
        if (!run.Ends(source, target, out var from, out var to)) return;

        string id;
        if (map.TryGetValue("id", out var written))
        {
            if (written is not string text)
            {
                run.Fail("the id of an item is not a string");
                return;
            }
            id = text;
        }
        else id = run.PlainId(from, to);

        IReadOnlyList<DislElement> sources = [];
        if (map.TryGetValue("sources", out var listed))
        {
            if (listed is not IReadOnlyList<object?> list)
            {
                run.Fail("the sources of an item are not a list");
                return;
            }
            sources = [.. list.OfType<DislElement>()];
        }

        var attributes = map
            .Where(entry => run.Type.Attributes.ContainsKey(entry.Key))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        run.Add(id, from, to, attributes, sources);
    }

    private static bool Allows(DislRelationEnd? end, object? value) =>
        value is DislElement { Type.IsRelation: false } element
        && (end is null || end.Types.Count == 0 || end.Types.Any(element.IsA))
        && (end is null || !end.Exclude.Any(element.IsA));

    /// <summary>A binding element's attributes: the metamodel's typed, a fixed one's ignored, the ends named, the rest kept as read.</summary>
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
                // A fixed attribute is never stored (§4.3): a value the reader finds for one is ignored.
                if (!attribute.IsFixed && DislValues.TryFromRead(raw, attribute, metamodel, out var value)) attributes[target] = value;
            }
            else
            {
                host[name] = raw;
            }
        }
        return attributes;
    }

    /// <summary>
    /// The stored elements of a reading added to its diagram (§11.5): the nodes in reading order, each after its
    /// parent, then the relations, whose ends are nodes, then the reference attributes as model ids.
    /// </summary>
    private sealed class StoredElements(DislDiagram diagram, List<(FblElement Element, DislTypeMapping Mapping, DislType Type)> typed)
    {
        private readonly DislSpecification _specification = diagram.Specification;
        private readonly Dictionary<FblElement, DislElement> _built = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, DislElement> _byReadId = new(diagram.Specification.IdComparer);

        public void Add()
        {
            foreach ((FblElement element, DislTypeMapping mapping, DislType type) in typed.Where(entry => !entry.Type.IsRelation))
            {
                Node(element, mapping, type);
            }
            foreach ((FblElement element, DislTypeMapping mapping, DislType type) in typed.Where(entry => entry.Type.IsRelation))
            {
                Relation(element, mapping, type);
            }
            References();
        }

        private DislElement Node(FblElement element, DislTypeMapping mapping, DislType type)
        {
            if (_built.TryGetValue(element, out var done)) return done;
            var parent = element.ParentId is { } parentId && typed.FirstOrDefault(entry => entry.Element.Id == parentId) is { Element: not null } owner && !owner.Type.IsRelation
                ? Node(owner.Element, owner.Mapping, owner.Type)
                : null;
            var attributes = Attributes(element, mapping, type, _specification.Metamodel, out var host, out _);
            var node = diagram.Add(new DislElement(diagram, type, NaturalId(_specification, type, element, null, null), attributes)
            {
                Parent = parent,
                Slot = parent is null ? null : element.ParentSlot,
                HostAttributes = host,
                Line = element.Line,
                File = FileOf(element),
                IdIsStored = element.IdIsStored,
            });
            _built[element] = node;
            _byReadId.TryAdd(element.Id, node);
            return node;
        }

        private void Relation(FblElement element, DislTypeMapping mapping, DislType type)
        {
            var attributes = Attributes(element, mapping, type, _specification.Metamodel, out var host, out var ends);
            var sourceId = ends.GetValueOrDefault("source") ?? element.Source;
            var targetId = ends.GetValueOrDefault("target") ?? element.Target;
            diagram.Add(new DislElement(diagram, type, NaturalId(_specification, type, element, sourceId, targetId), attributes)
            {
                Source = End(sourceId),
                Target = End(targetId),
                SourceId = sourceId,
                TargetId = targetId,
                HostAttributes = host,
                Line = element.Line,
                File = FileOf(element),
                IdIsStored = element.IdIsStored,
            });
        }

        /// <summary>
        /// A reference attribute holds the id its target was read with, which under a prefix is not the
        /// target's model id: it is stored as the model id of the first node read with it (§11.5.4), and
        /// one that names nothing stays as read.
        /// </summary>
        private void References()
        {
            foreach (var element in diagram.Elements)
            {
                foreach (var attribute in element.Type.Attributes.Values)
                {
                    if (_specification.Metamodel.TypeOf(attribute.Type) is null || !element.Attributes.TryGetValue(attribute.Name, out var stored)) continue;
                    element.Store(attribute.Name, stored switch
                    {
                        string id => ModelId(id),
                        IReadOnlyList<object?> ids => ids.Select(id => id is string text ? ModelId(text) : id).ToList(),
                        _ => stored,
                    });
                }
            }
        }

        private string ModelId(string id) => _byReadId.TryGetValue(id, out var node) ? node.Id : id;

        // A relation end names a stored id (an FBL reference by id) or an FBL element's id, which is the
        // node's as it was read; the first element with it keeps it (§11.5.4).
        private DislElement? End(string? id) => id is null ? null : _byReadId.GetValueOrDefault(id) ?? diagram.ElementById(id);
    }

    /// <summary>
    /// One derived relation type's items as they become relations (§4.11.3, §4.9): the variables its expressions
    /// read, the relations it adds, and what it drops, reported once its items are done.
    /// </summary>
    private sealed class DerivedItems(DislDiagram diagram, DislType type, List<Finding> findings)
    {
        private readonly HashSet<string> _failures = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _badEnds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _repeats = new(StringComparer.Ordinal);
        private int _uncontained;

        public DislType Type => type;

        public DislDiagram Diagram => diagram;

        /// <summary>What the type's expressions read besides an item: <c>diagram</c> and <c>env</c>.</summary>
        public Dictionary<string, object?> Variables { get; } = new() { ["diagram"] = diagram, ["env"] = diagram.Env.ToCel() };

        /// <summary>Reports that the type's items could not be computed.</summary>
        public void NoItems() =>
            findings.Add(new Finding(DerivedFailed, FindingSeverity.Warning, $"The items of {type.Name} could not be computed, so there are none.", null));

        /// <summary>Notes why an item was dropped, reported once by <see cref="Report"/>.</summary>
        public void Fail(string failure) => _failures.Add(failure);

        /// <summary>Whether <paramref name="source"/> and <paramref name="target"/> are ends the declaration allows; an item with one that is not is dropped and counted.</summary>
        public bool Ends(object? source, object? target, out DislElement from, out DislElement to)
        {
            from = null!;
            to = null!;
            if (!Allows(type.Source, source)) return BadEnd("source");
            if (!Allows(type.Target, target)) return BadEnd("target");
            from = (DislElement)source!;
            to = (DislElement)target!;
            return true;
        }

        /// <summary>The id of a relation that states none: <c>&lt;Type&gt;:&lt;source.id&gt;-&gt;&lt;target.id&gt;</c>, with <c>#2</c>, <c>#3</c> for repeats.</summary>
        public string PlainId(DislElement from, DislElement to)
        {
            var plain = $"{type.Name}:{from.Id}->{to.Id}";
            var seen = _repeats[plain] = _repeats.GetValueOrDefault(plain) + 1;
            return seen == 1 ? plain : $"{plain}#{seen}";
        }

        /// <summary>Adds the derived relation, unless its id is taken (§4.11.5).</summary>
        public void Add(string id, DislElement from, DislElement to, Dictionary<string, object?> attributes, IReadOnlyList<DislElement> sources)
        {
            if (diagram.ElementById(id) is { } taken)
            {
                findings.Add(new Finding(DerivedId, FindingSeverity.Warning, $"{type.Name} '{id}' has the id of {taken}; it is not drawn.", null));
                return;
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

        /// <summary>Notes a node whose computed parent does not allow it, which is drawn at the top level (§4.11.2).</summary>
        public void Uncontained() => _uncontained++;

        /// <summary>Reports a derived node whose id an element computed or read earlier holds; it is not drawn (§4.11.5).</summary>
        public void Taken(string id, DislElement holder) =>
            findings.Add(new Finding(DerivedId, FindingSeverity.Warning, $"{type.Name} '{id}' has the id of {holder}; it is not drawn.", null));

        /// <summary>Reports the items dropped: why, then how many for an end the declaration does not allow, then the nodes drawn at the top level.</summary>
        public void Report()
        {
            foreach (var failure in _failures)
            {
                findings.Add(new Finding(DerivedFailed, FindingSeverity.Warning, $"Items of {type.Name} were dropped: {failure}", null));
            }
            foreach ((string end, int count) in _badEnds)
            {
                findings.Add(new Finding(DerivedEnds, FindingSeverity.Warning, $"{count} item(s) of {type.Name} were dropped: their {end} is not one its declaration allows.", null));
            }
            if (_uncontained > 0)
            {
                findings.Add(new Finding(Containment, FindingSeverity.Warning, $"{_uncontained} {type.Name} node(s) are drawn at the top level: their computed parent does not allow them.", null));
            }
        }

        private bool BadEnd(string end)
        {
            _badEnds[end] = _badEnds.GetValueOrDefault(end) + 1;
            return false;
        }
    }
}
