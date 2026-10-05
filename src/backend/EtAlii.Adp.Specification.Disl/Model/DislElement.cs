using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// A node or a relation as DISL's CEL sees it (§12.2): <c>id</c>, <c>type</c>, <c>kind</c>, every
/// attribute as a field, the containment members of a node, the ends of a relation, and the methods
/// <c>isA</c>, <c>descendants</c>, <c>ancestors</c>, <c>childrenOfType</c>, <c>incomingOf</c>,
/// <c>outgoingOf</c>, <c>positionIn</c>, <c>label</c> and <c>other</c>.
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
        foreach (var (name, value) in attributes ?? new Dictionary<string, object?>())
        {
            if (!type.Attributes.ContainsKey(name))
            {
                throw new ArgumentException($"'{name}' is not an attribute of {type.Name}.", nameof(attributes));
            }
            _attributes[name] = value;
        }
    }

    public DislDiagram Diagram { get; }

    public DislType Type { get; }

    /// <summary>The element's id: stored, or computed from the id rule once the model is complete (§11.5).</summary>
    public string Id { get; internal set; }

    /// <summary><c>node</c> or <c>relation</c>.</summary>
    public string Kind => Type.IsRelation ? "relation" : "node";

    /// <summary>The attributes that have a stored value, as CEL values.</summary>
    public IReadOnlyDictionary<string, object?> Attributes => _attributes;

    /// <summary>A node's parent; null for a top-level node and for a relation.</summary>
    public DislElement? Parent { get; internal init; }

    /// <summary>The children slot a node is in beneath its parent (§4.8); null at the top level.</summary>
    public string? Slot { get; internal init; }

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

    /// <summary>Whether the element's type is <paramref name="type"/> or one of its subtypes (§2.7).</summary>
    public bool IsA(string type) => Type.Linearisation.Contains(type);

    /// <summary>The value CEL reads for <paramref name="name"/>: stored, else its default, else its type's zero value; a reference resolved to its element.</summary>
    public object? ValueOf(string name)
    {
        if (!Type.Attributes.TryGetValue(name, out var attribute)) throw new ArgumentException($"'{name}' is not an attribute of {Type.Name}.", nameof(name));
        if (!_attributes.TryGetValue(name, out var value)) return DislValues.Unset(attribute, Diagram.Specification.Metamodel);
        if (Diagram.Specification.Metamodel.TypeOf(attribute.Type) is null) return value;
        return value switch
        {
            string id => Diagram.ElementById(id),
            IReadOnlyList<object?> ids => ids.Select(id => id is string text ? Diagram.ElementById(text) : null).ToList(),
            _ => value,
        };
    }

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
    public string Label() => Type.LabelAttribute is { } name && ValueOf(name) is { } value ? value as string ?? value.ToString() ?? "" : "";

    internal void AddChild(DislElement child) => _children.Add(child);

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
    public bool HasMember(string name) => Type.Attributes.ContainsKey(name)
        ? _attributes.ContainsKey(name)
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
            ("other", 1) when Type.IsRelation => Other(arguments[0]),
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

    private DislElement? Other(object? end) =>
        ReferenceEquals(end, Source) ? Target
        : ReferenceEquals(end, Target) ? Source
        : throw new CelException($"other() was given an element that is not an end of {this}.");

    private static List<object?> List(IEnumerable<DislElement> elements) => [.. elements];

    internal static string Text(object? value) => value as string ?? throw new CelException("A type name was expected.");
}
