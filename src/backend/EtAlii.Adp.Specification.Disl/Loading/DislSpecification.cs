using System.Text.Json;
using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// A loaded DISL specification (DISL §3.1): a typed view over the JSON, inheritance flattened, and
/// every expression compiled in its context. What this view does not type stays readable as JSON
/// through <see cref="Root"/> and each part's <c>Json</c>, and every <c>x-</c> property is kept
/// unread and unchanged (§2.8) in the <c>Extensions</c> of the object that holds it.
/// </summary>
/// <remarks>Made by <see cref="DislLoader"/>, which refuses a specification with errors rather than giving a view of it.</remarks>
public sealed class DislSpecification
{
    private readonly CelEnvironment _functions;
    private readonly Dictionary<string, DislExpression> _expressions;

    internal DislSpecification(
        JsonElement root,
        DislMetamodel metamodel,
        IReadOnlyList<DislFunctionDeclaration> functions,
        CelEnvironment environment,
        IReadOnlyList<DislExpression> expressions)
    {
        Root = root;
        Metamodel = metamodel;
        Functions = functions;
        _functions = environment;
        Expressions = expressions;
        _expressions = expressions.ToDictionary(expression => expression.Pointer, StringComparer.Ordinal);
        var ids = root.TryGetProperty("persistence", out var persistence) && persistence.ValueKind == JsonValueKind.Object && persistence.TryGetProperty("ids", out var declared)
            ? declared
            : default;
        Ids = IdRuleOf(ids);
        IdTypes = DislJson.Members(ids, "types").ToDictionary(type => type.Name, type => IdRuleOf(type.Value), StringComparer.Ordinal);
    }

    /// <summary>The specification's JSON, as it was read.</summary>
    public JsonElement Root { get; }

    /// <summary>The DISL version it targets (§2.9): <c>0.1</c>, <c>0.2</c> or <c>0.3</c>.</summary>
    public string Disl => DislJson.String(Root, "disl") ?? DislJson.String(Root, "dedl") ?? "";

    public string LanguageId => DislJson.String(Language, "id") ?? "";

    public string LanguageVersion => DislJson.String(Language, "version") ?? "";

    /// <summary>The <c>&lt;vendor&gt;/&lt;type&gt;</c> origin of the tool type, or null.</summary>
    public string? Origin => DislJson.String(Language, "origin");

    public JsonElement Language => Root.GetProperty("language");

    /// <summary>The top-level <c>x-</c> properties, unread and unchanged.</summary>
    public IReadOnlyDictionary<string, JsonElement> Extensions => DislJson.Extensions(Root);

    /// <summary>The metamodel (§4), inheritance flattened.</summary>
    public DislMetamodel Metamodel { get; }

    /// <summary>The user functions (§3.4), in declaration order.</summary>
    public IReadOnlyList<DislFunctionDeclaration> Functions { get; }

    /// <summary>Every compiled expression outside the functions, in document order.</summary>
    public IReadOnlyList<DislExpression> Expressions { get; }

    /// <summary>The top-level id rule (§11.5).</summary>
    public DislIdRule Ids { get; }

    /// <summary>The per-type id rules (§11.5.2), by type name.</summary>
    public IReadOnlyDictionary<string, DislIdRule> IdTypes { get; }

    /// <summary>The per-evaluation cost limit (§2.5): <c>language.limits.celCost</c>, one million by default.</summary>
    public long CostLimit => _functions.Budget;

    /// <summary>The compiled expression at <paramref name="pointer"/>, or null when there is none.</summary>
    public DislExpression? ExpressionAt(string pointer) => _expressions.GetValueOrDefault(pointer);

    /// <summary>
    /// An environment for <paramref name="context"/> (§12.3): the library, the user functions and the
    /// context's variables, plus <paramref name="bindings"/>; for compiling what a runtime builds itself.
    /// </summary>
    public CelEnvironment Environment(string context, params IEnumerable<string> bindings) =>
        _functions.Clone().DeclareVariables(DislContexts.VariablesOf(context)).DeclareVariables(bindings);

    /// <summary>The id rule that applies to <paramref name="type"/> (§11.5.2): its own, the nearest in its linearisation, or the top-level one.</summary>
    public DislIdRule IdRuleOf(string type)
    {
        var linearisation = Metamodel.TypeOf(type)?.Linearisation ?? [type];
        foreach (var candidate in linearisation)
        {
            if (IdTypes.TryGetValue(candidate, out var rule)) return rule;
        }
        return Ids;
    }

    private static DislIdRule IdRuleOf(JsonElement json) =>
        new(DislJson.String(json, "strategy") ?? "uuid-v7", DislJson.String(json, "expression"), json);
}

/// <summary>The metamodel of a specification (DISL §4), inheritance flattened.</summary>
public sealed class DislMetamodel
{
    internal DislMetamodel(
        IReadOnlyDictionary<string, DislAttribute> diagramAttributes,
        IReadOnlyDictionary<string, DislEnum> enums,
        IReadOnlyDictionary<string, DislType> types,
        IReadOnlyDictionary<string, DislType> relations,
        IReadOnlySet<string> dataTypes)
    {
        DiagramAttributes = diagramAttributes;
        Enums = enums;
        Types = types;
        Relations = relations;
        DataTypes = dataTypes;
    }

    /// <summary>The attributes of the diagram root element.</summary>
    public IReadOnlyDictionary<string, DislAttribute> DiagramAttributes { get; }

    /// <summary>The enumerations, by name.</summary>
    public IReadOnlyDictionary<string, DislEnum> Enums { get; }

    /// <summary>The node types, in declaration order.</summary>
    public IReadOnlyDictionary<string, DislType> Types { get; }

    /// <summary>The relation types, in declaration order.</summary>
    public IReadOnlyDictionary<string, DislType> Relations { get; }

    /// <summary>The names of the data types.</summary>
    public IReadOnlySet<string> DataTypes { get; }

    /// <summary>The node or relation type named <paramref name="name"/>, or null.</summary>
    public DislType? TypeOf(string name) => Types.GetValueOrDefault(name) ?? Relations.GetValueOrDefault(name);

    /// <summary>Whether <paramref name="type"/> is <paramref name="ancestor"/> or one of its subtypes (§2.7, §12.2).</summary>
    public bool IsA(string type, string ancestor) => TypeOf(type) is { } known ? known.Linearisation.Contains(ancestor) : type == ancestor;

    /// <summary>The types that are <paramref name="ancestor"/> or a subtype of it, in declaration order.</summary>
    public IEnumerable<DislType> SubtypesOf(string ancestor) =>
        Types.Values.Concat(Relations.Values).Where(type => type.Linearisation.Contains(ancestor));
}
