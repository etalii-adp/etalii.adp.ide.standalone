using System.Text.Json;
using EtAlii.Adp.Specification.Cel;

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
public sealed record DislRelationEnd(IReadOnlyList<string> Types, IReadOnlyList<string> Exclude, bool Optional);

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
    }

    public string Name { get; }

    /// <summary>Whether this is a relation type, drawn as an edge, rather than a node type.</summary>
    public bool IsRelation { get; }

    /// <summary>The declaration itself, for what this view does not type.</summary>
    public JsonElement Json { get; }

    public bool Abstract => DislJson.Bool(Json, "abstract");

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
    public DislRelationEnd? Source { get; internal set; }

    /// <summary>A relation type's target end; null for a node type.</summary>
    public DislRelationEnd? Target { get; internal set; }

    /// <summary>The <c>derived</c> declaration (§4.11): an Expression (a relation's 0.1 form) or a DerivedNode or DerivedRelation object; null for a stored type.</summary>
    public JsonElement? Derived => Json.TryGetProperty("derived", out var derived) ? derived : null;

    /// <summary>The extension properties (<c>x-</c>) of the declaration, unread and unchanged.</summary>
    public IReadOnlyDictionary<string, JsonElement> Extensions => DislJson.Extensions(Json);

    public override string ToString() => Name;
}

/// <summary>One parameter of a user function (DISL §3.4).</summary>
public sealed record DislParameter(string Name, string Type);

/// <summary>A user function's bounded recursion (DISL §3.4): how deep it may call itself, and what a call deeper than that gives.</summary>
public sealed record DislRecursion(int MaxDepth, string AtMaxDepth);

/// <summary>A user function as declared (DISL §3.4), in declaration order.</summary>
public sealed record DislFunctionDeclaration(
    string Name,
    IReadOnlyList<DislParameter> Parameters,
    string Returns,
    string Cel,
    IReadOnlyList<string> Uses,
    DislRecursion? Recursion);

/// <summary>An id rule (DISL §11.5): how ids are made, for every type or for one.</summary>
/// <param name="Strategy">The strategy, <c>uuid-v7</c> when none is declared.</param>
/// <param name="Expression">The CEL of the <c>cel</c> and <c>derived</c> strategies; null for the others.</param>
public sealed record DislIdRule(string Strategy, string? Expression);

/// <summary>A compiled expression of the specification: where it is, the context it was compiled in (§12.3), and its program.</summary>
public sealed record DislExpression(string Pointer, string Context, CelProgram Program);
