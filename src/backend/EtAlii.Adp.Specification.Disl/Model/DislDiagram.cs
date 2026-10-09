using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// A diagram as DISL's CEL sees it (§12.2): the root element, its attributes as fields, and its nodes
/// and relations in model order - the stored ones in persistence order, then the derived ones in
/// their computation order (§12.5).
/// </summary>
/// <remarks>
/// Built element by element, parents before their children, by <see cref="DislModelBuilder"/> or a
/// test. The element lists are what the expressions iterate, so their order is the order every derived id,
/// finding and position depends on.
/// </remarks>
public sealed class DislDiagram : ICelObject
{
    private static readonly object Missing = new();

    private readonly Dictionary<string, object?> _attributes;
    private readonly List<DislElement> _nodes = [];
    private readonly List<DislElement> _relations = [];
    private Dictionary<DislElement, (List<DislElement> Incoming, List<DislElement> Outgoing)>? _ends;

    /// <summary>A diagram of <paramref name="specification"/> with the diagram attributes <paramref name="attributes"/>, as CEL values.</summary>
    public DislDiagram(DislSpecification specification, IReadOnlyDictionary<string, object?>? attributes = null)
    {
        ArgumentNullException.ThrowIfNull(specification);
        Specification = specification;
        _attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string name, object? value) in attributes ?? new Dictionary<string, object?>())
        {
            if (!specification.Metamodel.DiagramAttributes.ContainsKey(name))
            {
                throw new ArgumentException($"'{name}' is not an attribute of the diagram.", nameof(attributes));
            }
            _attributes[name] = value;
        }
    }

    public DislSpecification Specification { get; }

    /// <summary>
    /// What <c>diagram.file</c> reads (§8.2): the file a finding attributes the diagram to, relative to
    /// its subject, which a host binds when a message names it; null, and no <c>file</c> member, when
    /// the host gives none. A diagram attribute named <c>file</c> shadows it.
    /// </summary>
    public string? File { get; set; }

    /// <summary>The diagram attributes that have a stored value.</summary>
    public IReadOnlyDictionary<string, object?> Attributes => _attributes;

    /// <summary>The model's nodes, in model order.</summary>
    public IReadOnlyList<DislElement> Nodes => _nodes;

    /// <summary>The model's relations, in model order.</summary>
    public IReadOnlyList<DislElement> Relations => _relations;

    /// <summary>The nodes, then the relations.</summary>
    public IReadOnlyList<DislElement> Elements => [.. _nodes, .. _relations];

    /// <summary>Adds a node of <paramref name="type"/>, beneath <paramref name="parent"/> when one is given.</summary>
    public DislElement AddNode(string type, string id, IReadOnlyDictionary<string, object?>? attributes = null, DislElement? parent = null, string? slot = null) =>
        Add(new DislElement(this, TypeOf(type, relation: false), id, attributes) { Parent = parent, Slot = parent is null ? null : slot });

    /// <summary>Adds a relation of <paramref name="type"/> from <paramref name="source"/> to <paramref name="target"/>; either may be missing, as a model being read can have it.</summary>
    public DislElement AddRelation(string type, string id, DislElement? source, DislElement? target, IReadOnlyDictionary<string, object?>? attributes = null) =>
        Add(new DislElement(this, TypeOf(type, relation: true), id, attributes) { Source = source, Target = target, SourceId = source?.Id, TargetId = target?.Id });

    /// <summary>Adds an element made for this diagram: a node beneath its parent, a relation at its ends.</summary>
    internal DislElement Add(DislElement element)
    {
        if (element.Diagram != this) throw new ArgumentException("The element was made for another diagram.", nameof(element));
        if (element.Type.IsRelation)
        {
            if (element.Source is not null && element.Source.Diagram != this || element.Target is not null && element.Target.Diagram != this)
            {
                throw new ArgumentException("A relation's ends belong to its own diagram.", nameof(element));
            }
            _relations.Add(element);
            _ends = null;
            return element;
        }
        if (element.Parent is { } parent)
        {
            if (parent.Diagram != this) throw new ArgumentException("The parent belongs to another diagram.", nameof(element));
            parent.AddChild(element);
        }
        _nodes.Add(element);
        return element;
    }

    /// <summary>Takes <paramref name="element"/> out of the diagram, with its relations' ends left as they are: an action's effect on the working state (§9.4).</summary>
    internal void Remove(DislElement element)
    {
        if (element.Type.IsRelation)
        {
            _relations.Remove(element);
            _ends = null;
            return;
        }
        element.Parent?.RemoveChild(element);
        _nodes.Remove(element);
        _ends = null;
    }

    /// <summary>
    /// Moves node <paramref name="element"/>, with everything beneath it, under <paramref name="parent"/>
    /// in <paramref name="slot"/>, or to the top level: an action's effect on the working state (§9.4).
    /// </summary>
    /// <param name="index">
    /// Where among the new parent's children it goes, counted before the move with the node itself
    /// included, as <c>ModelChange.Move</c> counts it; negative or past the end puts it last.
    /// </param>
    /// <remarks>The node list keeps document order: the moved subtree follows the sibling it now comes after.</remarks>
    internal void Reparent(DislElement element, DislElement? parent, string? slot, int index)
    {
        if (element.Diagram != this || element.Type.IsRelation) throw new ArgumentException("Only a node of this diagram can move.", nameof(element));
        if (parent is not null && (parent.Diagram != this || parent.Type.IsRelation)) throw new ArgumentException("A node moves beneath a node of its own diagram.", nameof(parent));
        if (parent is not null && (ReferenceEquals(parent, element) || parent.Ancestors().Contains(element))) throw new InvalidOperationException("A node cannot move beneath itself.");

        var siblings = SiblingsUnder(parent);
        var at = index < 0 || index > siblings.Count ? siblings.Count : index;
        var current = ReferenceEquals(element.Parent, parent) ? siblings.IndexOf(element) : -1;
        if (current >= 0 && current < at) at--;

        var subtree = element.Descendants().Prepend(element).ToHashSet();
        var block = _nodes.Where(subtree.Contains).ToList();
        _nodes.RemoveAll(subtree.Contains);
        element.Parent?.RemoveChild(element);
        element.Parent = parent;
        element.Slot = parent is null ? null : slot;
        parent?.InsertChild(at, element);

        var after = SiblingsUnder(parent).Where(sibling => !ReferenceEquals(sibling, element)).ElementAtOrDefault(at - 1);
        var position = after is not null
            ? after.Descendants().Prepend(after).Max(_nodes.IndexOf) + 1
            : parent is not null
                ? _nodes.IndexOf(parent) + 1
                : _nodes.FindIndex(node => node.Parent is null) is var first and >= 0
                    ? first
                    : _nodes.Count;
        _nodes.InsertRange(position, block);
        _ends = null;
    }

    /// <summary>The children of <paramref name="parent"/>, or the top-level nodes, in model order.</summary>
    internal List<DislElement> SiblingsUnder(DislElement? parent) =>
        parent is not null ? [.. parent.Children] : [.. _nodes.Where(node => node.Parent is null)];

    /// <summary>Sets diagram attribute <paramref name="name"/>, as a reader does.</summary>
    internal void SetAttribute(string name, object? value) => _attributes[name] = value;

    /// <summary>The value CEL reads for diagram attribute <paramref name="name"/>: stored, else its default, else its type's zero value (§4.3).</summary>
    public object? ValueOf(string name) =>
        Specification.Metamodel.DiagramAttributes.TryGetValue(name, out var attribute)
            ? _attributes.TryGetValue(name, out var value) ? value : DislValues.Unset(attribute, Specification.Metamodel)
            : throw new ArgumentException($"'{name}' is not an attribute of the diagram.", nameof(name));

    /// <summary>The first element with <paramref name="id"/> in model order, or null.</summary>
    public DislElement? ElementById(string id) =>
        _nodes.FirstOrDefault(node => node.Id == id) ?? _relations.FirstOrDefault(relation => relation.Id == id);

    /// <summary>The nodes that are <paramref name="type"/> or one of its subtypes, in model order.</summary>
    public IReadOnlyList<DislElement> NodesOfType(string type) => [.. _nodes.Where(node => node.IsA(type))];

    /// <summary>The relations that are <paramref name="type"/> or one of its subtypes, in model order.</summary>
    public IReadOnlyList<DislElement> RelationsOfType(string type) => [.. _relations.Where(relation => relation.IsA(type))];

    internal IReadOnlyList<DislElement> IncomingOf(DislElement node) => Ends().GetValueOrDefault(node).Incoming ?? [];

    internal IReadOnlyList<DislElement> OutgoingOf(DislElement node) => Ends().GetValueOrDefault(node).Outgoing ?? [];

    private Dictionary<DislElement, (List<DislElement> Incoming, List<DislElement> Outgoing)> Ends()
    {
        if (_ends is not null) return _ends;
        var ends = new Dictionary<DislElement, (List<DislElement> Incoming, List<DislElement> Outgoing)>(ReferenceEqualityComparer.Instance);
        foreach (var relation in _relations)
        {
            if (relation.Source is { } source) Lists(source).Outgoing.Add(relation);
            if (relation.Target is { } target) Lists(target).Incoming.Add(relation);
        }
        return _ends = ends;

        (List<DislElement> Incoming, List<DislElement> Outgoing) Lists(DislElement node) =>
            ends.TryGetValue(node, out var lists) ? lists : ends[node] = ([], []);
    }

    private DislType TypeOf(string name, bool relation)
    {
        var type = Specification.Metamodel.TypeOf(name) ?? throw new ArgumentException($"'{name}' is not a type of this specification.", nameof(name));
        if (type.IsRelation != relation)
        {
            throw new ArgumentException($"'{name}' is {(type.IsRelation ? "a relation type" : "a node type")}.", nameof(name));
        }
        return type.Abstract ? throw new ArgumentException($"'{name}' is abstract.", nameof(name)) : type;
    }

    // ---- CEL -------------------------------------------------------------------------------------

    /// <inheritdoc />
    public bool TryGetMember(string name, out object? value)
    {
        value = name switch
        {
            "nodes" => Nodes.Cast<object?>().ToList(),
            "relations" => Relations.Cast<object?>().ToList(),
            "elements" => Elements.Cast<object?>().ToList(),
            "id" => "",
            "type" => "diagram",
            "kind" => "diagram",
            _ when Specification.Metamodel.DiagramAttributes.ContainsKey(name) => ValueOf(name),
            "file" when File is not null => File,
            _ => Missing,
        };
        if (ReferenceEquals(value, Missing))
        {
            value = null;
            return false;
        }
        return true;
    }

    /// <inheritdoc />
    public bool HasMember(string name) =>
        Specification.Metamodel.DiagramAttributes.ContainsKey(name) ? _attributes.ContainsKey(name) : TryGetMember(name, out _);

    /// <inheritdoc />
    public bool TryInvoke(string name, IReadOnlyList<object?> arguments, out object? value)
    {
        value = (name, arguments.Count) switch
        {
            ("nodesOfType", 1) => NodesOfType(DislElement.Text(arguments[0])).Cast<object?>().ToList(),
            // View-only elements are a view's; a headless model has none, so the flag changes nothing here.
            ("nodesOfType", 2) => NodesOfType(DislElement.Text(arguments[0])).Cast<object?>().ToList(),
            ("relationsOfType", 1) => RelationsOfType(DislElement.Text(arguments[0])).Cast<object?>().ToList(),
            ("elementById", 1) => ElementById(DislElement.Text(arguments[0])),
            _ => Missing,
        };
        if (ReferenceEquals(value, Missing))
        {
            value = null;
            return false;
        }
        return true;
    }

    public override string ToString() => "diagram";
}
