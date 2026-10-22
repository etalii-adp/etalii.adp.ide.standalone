using System.Text.Json;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Json;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// One attribute of a node type, a relation type or the diagram (DISL §4.3), after inheritance is
/// flattened: the declaration nearest in the type's linearisation (§4.7).
/// </summary>
/// <param name="Name">The attribute's name, a simple identifier.</param>
/// <param name="Type">Its type as written: a primitive (§4.2), an enum, a data type, or a node or relation type.</param>
/// <param name="Many">Whether it holds a list.</param>
/// <param name="Default">Its literal default, when it declares one that is not CEL.</param>
/// <param name="DeclaredBy">The type whose declaration this is: the type itself, or the supertype it inherits it from.</param>
/// <param name="Json">The declaration itself, for what this view does not type.</param>
public sealed record DislAttribute(string Name, string Type, bool Many, JsonElement? Default, string DeclaredBy, JsonElement Json)
{
    /// <summary>The primitive types of DISL §4.2.</summary>
    public static IReadOnlySet<string> Primitives { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "string", "text", "int", "number", "bool", "date", "datetime", "yearMonth", "time", "duration", "color", "uri", "expression", "json", "binary",
    };

    /// <summary>Its <c>label</c>, read at load; null when it states none.</summary>
    internal string? Label { get; } = JsonProperties.String(Json, "label");

    /// <summary>Its <c>min</c> (§4.3), read at load: the least number it may hold; null when it states none or one that is not a number.</summary>
    internal double? Min { get; } = Bound(Json, "min");

    /// <summary>Its <c>max</c> (§4.3), read at load: the greatest number it may hold; null when it states none or one that is not a number.</summary>
    internal double? Max { get; } = Bound(Json, "max");

    /// <summary>Whether it is a <c>key</c> (§4.3), read at load: a part of its type's natural id.</summary>
    internal bool Key { get; } = JsonProperties.Bool(Json, "key");

    /// <summary>Whether it declares <c>derived</c> (§4.3), whatever that holds, read at load: its value is computed, never stored.</summary>
    internal bool IsDerived { get; } = Json.ValueKind == JsonValueKind.Object && Json.TryGetProperty("derived", out _);

    /// <summary>
    /// Its literal <see cref="Default"/> as CEL sees it, converted at load; null without one, or when it is not a value of
    /// the attribute's type. A reading gets a copy of its own (<see cref="DislValues.Copy"/>).
    /// </summary>
    internal object? DefaultValue { get; init; }

    /// <summary>
    /// Whether it declares <c>fixed</c> (§4.3), read at load: it always has <see cref="FixedValue"/>, is read-only, and is
    /// never stored, so a reader ignores a stored value and a writer never writes one.
    /// </summary>
    internal bool IsFixed { get; } = Json.ValueKind == JsonValueKind.Object && Json.TryGetProperty("fixed", out _);

    /// <summary>
    /// Its <c>fixed</c> value as CEL sees it, converted at load; null without one, or when it is not a value of the
    /// attribute's type. A reading gets a copy of its own (<see cref="DislValues.Copy"/>).
    /// </summary>
    internal object? FixedValue { get; init; }

    /// <summary>
    /// Its own <c>readOnlyReasons</c> (§4.3), in priority order, set at load with the programs the loader compiled in the
    /// <c>element</c> context; none when it states none.
    /// </summary>
    internal IReadOnlyList<DislReason> ReadOnlyReasons { get; set; } = [];

    /// <summary>
    /// The program the loader compiled at a derived attribute's <c>derived</c> (§4.3), set at load; null when it compiled
    /// none there, and the attribute then reads as unset.
    /// </summary>
    internal CelProgram? DerivedProgram { get; set; }

    private static double? Bound(JsonElement json, string name) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var bound) && bound.ValueKind == JsonValueKind.Number ? bound.GetDouble() : null;
}

/// <summary>One value of an enumeration (DISL §4.5): its key, which CEL sees, and its stored form, which is written.</summary>
public sealed record DislEnumValue(string Key, string Stored, string? Label);

/// <summary>An enumeration (DISL §4.5), its values in declaration order, which is the display order.</summary>
public sealed record DislEnum(string Name, IReadOnlyList<DislEnumValue> Values, bool Ordered)
{
    /// <summary>The value whose key is <paramref name="key"/>, or null.</summary>
    public DislEnumValue? ValueOf(string key) => Values.FirstOrDefault(value => value.Key == key);

    /// <summary>The value whose stored form is <paramref name="stored"/>, or null.</summary>
    public DislEnumValue? StoredAs(string stored) => Values.FirstOrDefault(value => value.Stored == stored);
}

/// <summary>One end of a relation type (DISL §4.9): the types it accepts, those it excludes, and whether it may be missing.</summary>
/// <param name="Types">The types it accepts; none for every type.</param>
/// <param name="Exclude">The subtypes it excludes from <paramref name="Types"/>.</param>
/// <param name="Optional">Whether a relation may be stored without it (a <c>target</c> end only).</param>
/// <param name="Min">Its <c>min</c>, when it states one as an int; null otherwise.</param>
/// <param name="Max">Its <c>max</c>, when it states one as an int; null, unbounded, otherwise.</param>
/// <param name="DeclaredBy">The relation type whose declaration this end is: the type itself, or the supertype it inherits it from.</param>
internal sealed record DislRelationEnd(IReadOnlyList<string> Types, IReadOnlyList<string> Exclude, bool Optional, long? Min = null, long? Max = null, string? DeclaredBy = null);

/// <summary>
/// A node type (DISL §4.6) or a relation type (§4.9), inheritance flattened (§4.7): its C3
/// linearisation, every attribute it has, and what its own declaration says.
/// </summary>
public sealed class DislType
{
    internal DislType(string name, bool isRelation, JsonElement json)
    {
        Name = name;
        IsRelation = isRelation;
        Json = json;
        Abstract = JsonProperties.Bool(json, "abstract");
        Label = JsonProperties.String(json, "label");
        Icon = JsonProperties.String(json, "icon");
        AllowSelfLoops = Flag(json, "allowSelfLoops");
        AllowParallel = Flag(json, "allowParallel");
        Acyclic = Flag(json, "acyclic") ?? false;
        Directed = Flag(json, "directed");
        var derived = json.ValueKind == JsonValueKind.Object && json.TryGetProperty("derived", out var declared) && declared.ValueKind == JsonValueKind.Object ? declared : default;
        ConnectEdit = derived.ValueKind == JsonValueKind.Object && derived.TryGetProperty("edits", out var edits) ? JsonProperties.String(edits, "connect") : null;
        DerivedReason = JsonProperties.String(derived, "reason");
    }

    public string Name { get; }

    /// <summary>Whether this is a relation type, drawn as an edge, rather than a node type.</summary>
    public bool IsRelation { get; }

    /// <summary>The declaration itself, for what this view does not type.</summary>
    public JsonElement Json { get; }

    public bool Abstract { get; }

    /// <summary>Its <c>label</c>, read at load; null when it states none.</summary>
    internal string? Label { get; }

    /// <summary>Its <c>icon</c>, read at load; null when it states none.</summary>
    internal string? Icon { get; }

    /// <summary>A relation type's <c>allowSelfLoops</c> (§4.9) when it states it as a boolean; null otherwise.</summary>
    internal bool? AllowSelfLoops { get; }

    /// <summary>A relation type's <c>allowParallel</c> (§4.9) when it states it as a boolean; null otherwise.</summary>
    internal bool? AllowParallel { get; }

    /// <summary>Whether a relation type's own declaration states <c>acyclic: true</c> (§4.9); a subtype of such a type is acyclic through it.</summary>
    internal bool Acyclic { get; }

    /// <summary>A relation type's <c>directed</c> (§4.9) when it states it as a boolean; null otherwise.</summary>
    internal bool? Directed { get; }

    /// <summary>The supertypes it names, in the order it names them.</summary>
    public IReadOnlyList<string> Extends { get; internal set; } = [];

    /// <summary>The type and its supertypes, most specific first: its C3 linearisation (§4.7).</summary>
    public IReadOnlyList<string> Linearisation { get; internal set; } = [];

    /// <summary>Every attribute, inherited ones included, the most general type's first and each in declaration order.</summary>
    public IReadOnlyDictionary<string, DislAttribute> Attributes { get; internal set; } = new Dictionary<string, DislAttribute>();

    /// <summary>The types a node of this type may contain (§4.8), nearest declaration in the linearisation; empty when it contains none.</summary>
    public IReadOnlyList<string> ChildTypes { get; internal set; } = [];

    /// <summary>The attribute used as the element's name (§4.6): declared, or the default rule's choice; null when it has none.</summary>
    public string? LabelAttribute { get; internal set; }

    /// <summary>A relation type's source end; null for a node type.</summary>
    internal DislRelationEnd? Source { get; set; }

    /// <summary>A relation type's target end; null for a node type.</summary>
    internal DislRelationEnd? Target { get; set; }

    /// <summary>The <c>derived</c> declaration (§4.11): an Expression (a relation's 0.1 form) or a DerivedNode or DerivedRelation object; null for a stored type.</summary>
    public JsonElement? Derived => Json.TryGetProperty("derived", out var derived) ? derived : null;

    /// <summary>
    /// A derived relation type's <c>derived</c> (§4.9, §4.11.3), typed at load with the programs the loader compiled; null
    /// for a stored type, and for one whose <c>derived</c> is neither an Expression nor an object.
    /// </summary>
    internal DislDerivedRelation? DerivedRelation { get; set; }

    /// <summary>The operation a derived relation's <c>edits.connect</c> runs when one is drawn (§4.11), or null when it names none.</summary>
    internal string? ConnectEdit { get; }

    /// <summary>A derived type's <c>reason</c>, why it cannot be edited by hand (§4.11), or null when it states none.</summary>
    internal string? DerivedReason { get; }

    /// <summary>The extension properties (<c>x-</c>) of the declaration, unread and unchanged.</summary>
    public IReadOnlyDictionary<string, JsonElement> Extensions => DislJson.Extensions(Json);

    public override string ToString() => Name;

    private static bool? Flag(JsonElement json, string name) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
}

/// <summary>One parameter of a user function (DISL §3.4).</summary>
internal sealed record DislParameter(string Name, string Type);

/// <summary>A user function's bounded recursion (DISL §3.4): how deep it may call itself, and what a call deeper than that gives.</summary>
internal sealed record DislRecursion(int MaxDepth, string AtMaxDepth);

/// <summary>A user function as declared (DISL §3.4), in declaration order.</summary>
internal sealed record DislFunctionDeclaration(
    string Name,
    IReadOnlyList<DislParameter> Parameters,
    string Returns,
    string Cel,
    IReadOnlyList<string> Uses,
    DislRecursion? Recursion);

/// <summary>An id rule (DISL §11.5): how ids are made, for every type or for one.</summary>
/// <param name="Strategy">The strategy, <c>uuid-v7</c> when none is declared.</param>
/// <param name="Expression">The CEL of the <c>cel</c> and <c>derived</c> strategies; null for the others.</param>
/// <param name="Prefix">
/// The prefix the rule gives the type it was asked for (<c>prefix</c>, a string or, at the top level, a
/// map by type): the type's own rule's, else the top level's for the type or the nearest type in its
/// linearisation; null when none is declared.
/// </param>
internal sealed record DislIdRule(string Strategy, string? Expression, string? Prefix = null);

/// <summary>A compiled expression of the specification: where it is, the context it was compiled in (§12.3; null where this runtime does not know it), and its program.</summary>
public sealed record DislExpression(string Pointer, DislContext? Context, CelProgram Program);
