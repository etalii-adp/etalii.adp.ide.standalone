using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// A node or a relation as DISL's CEL sees it (§12.2): <c>id</c>, <c>type</c>, <c>kind</c>, every
/// attribute as a field, the containment members of a node, the ends of a relation, and the methods
/// <c>isA</c>, <c>descendants</c>, <c>ancestors</c>, <c>childrenOfType</c>, <c>incomingOf</c>,
/// <c>outgoingOf</c>, <c>positionIn</c>, <c>label</c>, <c>other</c> and <c>location</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>An unset attribute reads as its default, else its type's zero value</b>, while
/// <c>has(e.attr)</c> holds only for a stored value (§4.3). A reference attribute reads as the
/// element its stored id names, or <c>null</c>.
/// </para>
/// <para>
/// <b>Elements compare by identity</b>, never by id: CEL's <c>==</c>, <c>in</c> and
/// <c>positionIn</c> all see two elements with one id as two, which is what lets ids be computed
/// from positions (§11.5.2) and duplicate ids be found.
/// </para>
/// </remarks>
public sealed class DislElement : ICelObject
{
    private static readonly object Missing = new();

    private readonly Dictionary<string, object?> _attributes;
    private readonly List<DislElement> _children = [];

    internal DislElement(DislDiagram diagram, DislType type, string id, IReadOnlyDictionary<string, object?>? attributes)
    {
        Diagram = diagram;
        Type = type;
        Id = id;
        _attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string name, object? value) in attributes ?? new Dictionary<string, object?>())
        {
            if (!type.Attributes.ContainsKey(name))
            {
                throw new ArgumentException($"'{name}' is not an attribute of {type.Name}.", nameof(attributes));
            }
            _attributes[name] = value;
        }
    }

    public DislDiagram Diagram { get; }

    public DislType Type { get; private set; }

    /// <summary>The element's id: stored, or computed from the id rule once the model is complete (§11.5).</summary>
    public string Id { get; internal set; }

    /// <summary><c>node</c> or <c>relation</c>.</summary>
    public string Kind => Type.IsRelation ? "relation" : "node";

    /// <summary>The attributes that have a stored value, as CEL values.</summary>
    public IReadOnlyDictionary<string, object?> Attributes => _attributes;

    /// <summary>A node's parent; null for a top-level node and for a relation.</summary>
    public DislElement? Parent { get; internal set; }

    /// <summary>The children slot a node is in beneath its parent (§4.8); null at the top level.</summary>
    public string? Slot { get; internal set; }

    /// <summary>A node's children, in model order.</summary>
    public IReadOnlyList<DislElement> Children => _children;

    /// <summary>A relation's source; null for a node, and for a relation whose stored source names nothing.</summary>
    public DislElement? Source { get; internal init; }

    /// <summary>A relation's target; null for a node, and for a relation without one (§4.9).</summary>
    public DislElement? Target { get; internal init; }

    /// <summary>Whether the element is derived (§4.11) rather than stored.</summary>
    public bool IsDerived { get; internal init; }

    /// <summary>A derived element's sources (§4.11.4); empty for a stored one.</summary>
    public IReadOnlyList<DislElement> Sources { get; internal init; } = [];

    /// <summary>Whether the id was read from storage; false for an id the reader made up or the runtime computed.</summary>
    public bool IdIsStored { get; internal set; } = true;

    /// <summary>A relation's source as stored, which names nothing when <see cref="Source"/> is null.</summary>
    public string? SourceId { get; internal init; }

    /// <summary>A relation's target as stored, which names nothing when <see cref="Target"/> is null.</summary>
    public string? TargetId { get; internal init; }

    /// <summary>What the reader read beside the metamodel's attributes (<c>hostAttributes</c>, unmapped keys), as it read it; CEL does not see these.</summary>
    public IReadOnlyDictionary<string, object?> HostAttributes { get; internal init; } = new Dictionary<string, object?>();

    /// <summary>The 1-based line the reader found the element on, when it was read.</summary>
    public int? Line { get; internal init; }

    /// <summary>
    /// The file the reader found the element in, relative to the diagram's subject (§8.6), when it
    /// recorded one; null for an element of the primary file, whose file is the diagram's.
    /// </summary>
    public string? File { get; internal init; }

    /// <summary>
    /// Where the element is in the files (§8.6): its own file, else the diagram's, with its line. The
    /// file is null when neither is known, and the line when the reader recorded none.
    /// </summary>
    internal (string? File, int? Line) Location => (File ?? Diagram.File, Line);

    /// <summary>
    /// What <c>self.view</c> reads (§12.2): the viewer's state of this element, such as <c>collapsed</c>
    /// (§11.6), which a host that holds viewer state binds before deriving a menu or a label; null,
    /// and no <c>view</c> member, for a headless model.
    /// </summary>
    public CelMap? View { get; set; }

    /// <summary>Whether the element's type is <paramref name="type"/> or one of its subtypes (§2.7).</summary>
    public bool IsA(string type) => Type.Linearisation.Contains(type);

    /// <summary>
    /// The value CEL reads for <paramref name="name"/>: a derived attribute's expression (§4.3), else
    /// stored, else its default, else its type's zero value; a reference resolved to its element.
    /// </summary>
    public object? ValueOf(string name)
    {
        if (!Type.Attributes.TryGetValue(name, out var attribute)) throw new ArgumentException($"'{name}' is not an attribute of {Type.Name}.", nameof(name));
        if (attribute.Json.TryGetProperty("derived", out _)) return Derived(attribute);
        if (!_attributes.TryGetValue(name, out var value)) return DislValues.Unset(attribute, Diagram.Specification.Metamodel);
        if (Diagram.Specification.Metamodel.TypeOf(attribute.Type) is null) return value;
        return value switch
        {
            string id => Diagram.ElementById(id),
            IReadOnlyList<object?> ids => ids.Select(id => id is string text ? Diagram.ElementById(text) : null).ToList(),
            _ => value,
        };
    }

    /// <summary>
    /// A derived attribute's value (§4.3): its expression, compiled by the loader in the
    /// <c>element</c> context, with <c>self</c> this element and <c>env</c> the diagram's. An expression
    /// that fails, or that reads its own value, gives the attribute's unset value, as a visual property
    /// falls back to its default (§2.5).
    /// </summary>
    private object? Derived(DislAttribute attribute)
    {
        var metamodel = Diagram.Specification.Metamodel;
        var declaredIn = metamodel.Types.ContainsKey(attribute.DeclaredBy) ? "/metamodel/types" : "/metamodel/relations";
        var pointer = DislJson.Pointer(DislJson.Pointer(DislJson.Pointer(DislJson.Pointer(declaredIn, attribute.DeclaredBy), "attributes"), attribute.Name), "derived");
        if (Diagram.Specification.ExpressionAt(pointer) is not { } expression) return DislValues.Unset(attribute, metamodel);

        _computing ??= new HashSet<(DislElement, string)>();
        if (!_computing.Add((this, attribute.Name))) return DislValues.Unset(attribute, metamodel);
        try
        {
            var value = expression.Program.Evaluate(new Dictionary<string, object?> { ["self"] = this, ["diagram"] = Diagram, ["env"] = Diagram.Env.ToCel() });
            return value is CelError ? DislValues.Unset(attribute, metamodel) : value;
        }
        finally
        {
            _computing.Remove((this, attribute.Name));
        }
    }

    /// <summary>The derived attributes being computed on this thread, so one that reads itself stops.</summary>
    [ThreadStatic]
    private static HashSet<(DislElement Element, string Attribute)>? _computing;

    /// <summary>Every node beneath this one, depth first in model order.</summary>
    public IEnumerable<DislElement> Descendants() => _children.SelectMany(child => child.Descendants().Prepend(child));

    /// <summary>The parent first, the top-level ancestor last.</summary>
    public IEnumerable<DislElement> Ancestors()
    {
        for (var ancestor = Parent; ancestor is not null; ancestor = ancestor.Parent) yield return ancestor;
    }

    /// <summary>The relations that end at this node, in model order.</summary>
    public IReadOnlyList<DislElement> Incoming => Diagram.IncomingOf(this);

    /// <summary>The relations that start at this node, in model order.</summary>
    public IReadOnlyList<DislElement> Outgoing => Diagram.OutgoingOf(this);

    /// <summary>The value of the type's label attribute (§4.6), as text; empty when it has none.</summary>
    private string Label() => Type.LabelAttribute is { } name && ValueOf(name) is { } value ? value as string ?? value.ToString() ?? "" : "";

    /// <summary>
    /// Makes this node a <paramref name="type"/> in place (a <c>retype</c> action, §9.4): the attributes both
    /// types declare are kept, the others forgotten, and <paramref name="mapped"/> stored over them. Its
    /// parent, children and relations stay as they are.
    /// </summary>
    internal void Retype(DislType type, IReadOnlyDictionary<string, object?> mapped)
    {
        foreach (var name in _attributes.Keys.Where(name => !type.Attributes.ContainsKey(name)).ToList()) _attributes.Remove(name);
        Type = type;
        foreach ((string name, object? value) in mapped)
        {
            if (value is null) _attributes.Remove(name);
            else _attributes[name] = value;
        }
    }

    internal void AddChild(DislElement child) => _children.Add(child);

    internal void RemoveChild(DislElement child) => _children.Remove(child);

    internal void InsertChild(int index, DislElement child) => _children.Insert(index, child);

    /// <summary>Stores <paramref name="value"/> for <paramref name="name"/>, or forgets the stored value for null: an action's effect on the working state (§9.4).</summary>
    internal void Store(string name, object? value)
    {
        if (!Type.Attributes.ContainsKey(name)) throw new ArgumentException($"'{name}' is not an attribute of {Type.Name}.", nameof(name));
        if (value is null) _attributes.Remove(name);
        else _attributes[name] = value;
    }

    /// <summary>
    /// The element as it is now, apart from the diagram: what a hook reads as <c>old</c> (§9.2). Its
    /// attributes are a copy, its relations are not followed, and it is equal only to itself.
    /// </summary>
    internal DislElement Snapshot() => new(Diagram, Type, Id, _attributes)
    {
        Parent = Parent, Slot = Slot, Source = Source, Target = Target, IsDerived = IsDerived, Sources = Sources,
        IdIsStored = IdIsStored, SourceId = SourceId, TargetId = TargetId, HostAttributes = HostAttributes, Line = Line, File = File,
    };

    public override string ToString() => $"{Type.Name} {Id}";

    /// <summary>Identity, as everywhere; the element a finding names it by its written id (<see cref="DislWrittenElement"/>) is this one too.</summary>
    public override bool Equals(object? obj) => ReferenceEquals(this, obj) || obj is DislWrittenElement written && ReferenceEquals(this, written.Element);

    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);

    // ---- CEL -------------------------------------------------------------------------------------

    /// <inheritdoc />
    public bool TryGetMember(string name, out object? value)
    {
        value = Member(name);
        if (ReferenceEquals(value, Missing))
        {
            value = null;
            return false;
        }
        return true;
    }

    /// <inheritdoc />
    public bool HasMember(string name) => Type.Attributes.TryGetValue(name, out var attribute)
        ? _attributes.ContainsKey(name) || attribute.Json.TryGetProperty("derived", out _)
        : Member(name) is var value && !ReferenceEquals(value, Missing) && value is not null;

    private object? Member(string name)
    {
        // An attribute named like a member shadows it for its type (§12.2); the reserved names (§2.2) keep the rest apart.
        if (Type.Attributes.ContainsKey(name)) return ValueOf(name);
        return name switch
        {
            "id" => Id,
            "type" => Type.Name,
            "kind" => Kind,
            "derived" => IsDerived,
            "sources" => List(Sources),
            "parent" when !Type.IsRelation => Parent,
            "owner" => Parent is null ? Diagram : Parent,
            "slot" when !Type.IsRelation => Slot ?? "",
            "children" when !Type.IsRelation => List(_children),
            "incoming" when !Type.IsRelation => List(Incoming),
            "outgoing" when !Type.IsRelation => List(Outgoing),
            "ports" when !Type.IsRelation => new List<object?>(),
            "source" when Type.IsRelation => Source,
            "target" when Type.IsRelation => Target,
            "view" when View is not null => View,
            _ => Missing,
        };
    }

    /// <inheritdoc />
    public bool TryInvoke(string name, IReadOnlyList<object?> arguments, out object? value)
    {
        value = (name, arguments.Count) switch
        {
            ("isA", 1) => IsA(Text(arguments[0])),
            ("label", 0) => Label(),
            ("positionIn", 1) => PositionIn(arguments[0]),
            ("descendants", 0) when !Type.IsRelation => List(Descendants()),
            ("ancestors", 0) when !Type.IsRelation => List(Ancestors()),
            ("childrenOfType", 1) when !Type.IsRelation => List(_children.Where(child => child.IsA(Text(arguments[0])))),
            ("incomingOf", 1) when !Type.IsRelation => List(Incoming.Where(relation => relation.IsA(Text(arguments[0])))),
            ("outgoingOf", 1) when !Type.IsRelation => List(Outgoing.Where(relation => relation.IsA(Text(arguments[0])))),
            ("reachable", 1) when !Type.IsRelation => List(Reachable(Text(arguments[0]))),
            ("inCycle", 1) when !Type.IsRelation => Reachable(Text(arguments[0])).Contains(this),
            ("other", 1) when Type.IsRelation => Other(arguments[0]),
            ("location", 0) => LocationMap(),
            _ => Missing,
        };
        if (ReferenceEquals(value, Missing))
        {
            value = null;
            return false;
        }
        return true;
    }

    /// <summary><c>e.positionIn(l)</c>: the zero-based position of this element in <paramref name="list"/>, by identity, or −1 (§12.4).</summary>
    private long PositionIn(object? list)
    {
        if (list is not IReadOnlyList<object?> items) throw new CelException("positionIn() takes a list.");
        for (var index = 0; index < items.Count; index++)
        {
            if (ReferenceEquals(items[index], this)) return index;
        }
        return -1;
    }

    /// <summary>
    /// <c>e.location()</c> (§12.2): the SourceLocation the reader recorded, a map with <c>file</c> and,
    /// when known, <c>line</c>; <c>optional.none()</c> when no file is known, since a SourceLocation
    /// always names one (§8.6).
    /// </summary>
    private CelOptional LocationMap()
    {
        (string? file, int? line) = Location;
        if (file is null) return CelOptional.None;
        var location = new CelMap { ["file"] = file };
        if (line is { } known) location["line"] = (long)known;
        return CelOptional.Of(location);
    }

    private DislElement? Other(object? end) =>
        ReferenceEquals(end, Source) ? Target
        : ReferenceEquals(end, Target) ? Source
        : throw new CelException($"other() was given an element that is not an end of {this}.");

    private static List<object?> List(IEnumerable<DislElement> elements) => [.. elements];

    /// <summary>
    /// The elements reached from this one over relations of <paramref name="relationType"/> and its subtypes (DISL §12.4), in the
    /// order a breadth-first walk first meets them; this element is among them only when a walk comes back to it, on a cycle.
    /// A relation whose target is unset leads nowhere.
    /// </summary>
    private List<DislElement> Reachable(string relationType)
    {
        var found = new List<DislElement>();
        var seen = new HashSet<DislElement>(ReferenceEqualityComparer.Instance);
        var pending = new Queue<DislElement>([this]);
        while (pending.Count > 0)
        {
            foreach (var relation in pending.Dequeue().Outgoing.Where(relation => relation.IsA(relationType)))
            {
                if (relation.Target is { } next && seen.Add(next))
                {
                    found.Add(next);
                    pending.Enqueue(next);
                }
            }
        }

        return found;
    }

    internal static string Text(object? value) => value as string ?? throw new CelException("A type name was expected.");
}
