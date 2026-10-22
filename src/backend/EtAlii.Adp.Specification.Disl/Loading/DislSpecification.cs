using System.Text.Json;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Json;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// A loaded DISL specification (DISL §3.1): a typed view over the JSON, inheritance flattened, every
/// expression compiled in its context, and the parts the derivation and the behaviour read typed at
/// load. What this view does not type stays readable as JSON through <see cref="Json"/> and each
/// part's <c>Json</c>, and every <c>x-</c> property is kept unread and unchanged (§2.8) in the
/// <c>Extensions</c> of the object that holds it.
/// </summary>
/// <remarks>Made by <see cref="DislLoader"/>, which refuses a specification with errors rather than giving a view of it.</remarks>
public sealed class DislSpecification
{
    private readonly CelEnvironment _functions;
    private readonly Dictionary<string, DislExpression> _expressions = new(StringComparer.Ordinal);

    /// <summary>The positions the loader walked, compiled or not, while the parts are read; null once they are.</summary>
    private readonly IReadOnlySet<string>? _walked;

    /// <summary>Where a part read while loading reports what it cannot read; null once the parts are read.</summary>
    private readonly List<LoadDiagnostic>? _diagnostics;

    internal DislSpecification(
        JsonElement root,
        DislMetamodel metamodel,
        IReadOnlyList<DislFunctionDeclaration> functions,
        CelEnvironment environment,
        CelProgramCache<DislProgramKey> programs,
        IReadOnlyList<DislExpression> expressions,
        IReadOnlySet<string> walked,
        List<LoadDiagnostic> diagnostics)
    {
        Json = root;
        Metamodel = metamodel;
        Functions = functions;
        _functions = environment;
        Programs = programs;
        Expressions = expressions;
        foreach (var expression in expressions) _expressions.TryAdd(expression.Pointer, expression);
        _walked = walked;
        _diagnostics = diagnostics;

        Disl = JsonProperties.String(root, "disl") ?? JsonProperties.String(root, "dedl") ?? "";
        var language = root.GetProperty("language");
        LanguageId = JsonProperties.String(language, "id") ?? "";
        Origin = JsonProperties.String(language, "origin") ?? JsonProperties.String(language, "x-adp-origin");
        Extensions = DislJson.Extensions(root);

        var persistence = root.TryGetProperty("persistence", out var declaredPersistence) ? declaredPersistence : default;
        PersistenceNotAnObject = persistence.ValueKind == JsonValueKind.Undefined ? null : DislFieldReader.NotAnObject(persistence);
        var ids = persistence.ValueKind == JsonValueKind.Object && persistence.TryGetProperty("ids", out var declared)
            ? declared
            : default;
        Ids = IdRuleOf(ids);
        IdEncoding = ids.ValueKind == JsonValueKind.Object ? JsonProperties.String(ids, "encoding") ?? "hex" : "hex";
        IdTypeNames = [.. DislJson.Members(ids, "types").Select(type => type.Name)];
        foreach (var type in DislJson.Members(ids, "types")) IdTypes[type.Name] = IdRuleOf(type.Value);
        Ephemerality = DislEphemerality.Read(this, ids, "/persistence/ids");
        foreach (var type in DislJson.Members(ids, "types"))
        {
            EphemeralityByType[type.Name] = DislEphemerality.Read(this, type.Value, JsonPointer.Append("/persistence/ids/types", type.Name));
        }
        if (ids.ValueKind == JsonValueKind.Object && ids.TryGetProperty("prefix", out var prefix) && prefix.ValueKind == JsonValueKind.Object)
        {
            foreach (var type in prefix.EnumerateObject().Where(type => type.Value.ValueKind == JsonValueKind.String)) IdPrefixes[type.Name] = type.Value.GetString()!;
        }
        IdComparer = JsonProperties.String(ids, "compare") == "ignore-case" ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        TypeMap = DislTypeMapReader.Read(persistence);
        DislDerivedReader.Read(this);

        Operations = DislOperationReader.Read(this, root);
        Toolbox = DislToolboxReader.Read(this, root);
        Forms = DislFormReader.Read(this, root);
        Constraints = DislConstraintReader.Read(this, root);
        Behavior = DislBehaviorReader.Read(this, root);
        Notation = DislNotationReader.Read(this, root);
        Viewpoints = DislNotationReader.Viewpoints(this, root);

        _walked = null;
        _diagnostics = null;
    }

    /// <summary>
    /// The specification's JSON, as it was read, for what this view does not type: a module's own readings. DISL's
    /// derivation and behaviour read the parts typed at load, never this.
    /// </summary>
    public JsonElement Json { get; }

    /// <summary>The DISL version it targets (§2.9): <c>0.1</c>, <c>0.2</c> or <c>0.3</c>.</summary>
    public string Disl { get; }

    public string LanguageId { get; }

    /// <summary>
    /// The tool type the specification is the definition of, as <c>&lt;vendor&gt;/&lt;type&gt;</c>
    /// (<c>language.origin</c>, §3.2), else the extension property it supersedes, <c>x-adp-origin</c>;
    /// null when it declares neither.
    /// </summary>
    public string? Origin { get; }

    /// <summary>The top-level <c>x-</c> properties, unread and unchanged.</summary>
    public IReadOnlyDictionary<string, JsonElement> Extensions { get; }

    /// <summary>The metamodel (§4), inheritance flattened.</summary>
    public DislMetamodel Metamodel { get; }

    /// <summary>The user functions (§3.4), in declaration order.</summary>
    internal IReadOnlyList<DislFunctionDeclaration> Functions { get; }

    /// <summary>Every compiled expression outside the functions, in document order.</summary>
    public IReadOnlyList<DislExpression> Expressions { get; }

    /// <summary>The top-level id rule (§11.5).</summary>
    private DislIdRule Ids { get; }

    /// <summary>The per-type id rules (§11.5.2), by type name.</summary>
    private Dictionary<string, DislIdRule> IdTypes { get; } = new(StringComparer.Ordinal);

    /// <summary>The top-level id rule's <c>ephemeral</c> and <c>reason</c> (§11.5.3).</summary>
    private DislEphemerality Ephemerality { get; }

    /// <summary>The per-type id rules' <c>ephemeral</c> and <c>reason</c> (§11.5.3), by type name.</summary>
    private Dictionary<string, DislEphemerality> EphemeralityByType { get; } = new(StringComparer.Ordinal);

    /// <summary>The top level's <c>prefix</c> when it is a map (§11.5), by type name.</summary>
    private Dictionary<string, string> IdPrefixes { get; } = new(StringComparer.Ordinal);

    /// <summary>The type names of the per-type id rules (§11.5.2), in declaration order.</summary>
    internal IReadOnlyList<string> IdTypeNames { get; }

    /// <summary>The top-level id strategy (§11.5.1), <c>uuid-v7</c> when none is declared.</summary>
    internal string IdStrategy => Ids.Strategy;

    /// <summary>The <c>encoding</c> of the ids a runtime mints (§11.5.1), <c>hex</c> when none is declared.</summary>
    internal string IdEncoding { get; }

    /// <summary>The message System.Text.Json threw reading a member of a <c>persistence</c> that is not an object, null for one that is.</summary>
    internal string? PersistenceNotAnObject { get; }

    /// <summary>
    /// How the model's ids compare (§11.5.4, <c>persistence.ids.compare</c>) for lookups, references and
    /// duplicate detection: as code-point sequences, or with <c>ignore-case</c> without regard to case.
    /// .NET's ordinal case-insensitive comparison (simple, one-to-one case mapping) stands in for Unicode
    /// default case folding: they agree on every letter whose folding is a single character, and differ
    /// only where full folding expands one (<c>ß</c> folds to <c>ss</c>, which this does not equal).
    /// </summary>
    public StringComparer IdComparer { get; }

    /// <summary>The per-evaluation cost limit (§2.5): <c>language.limits.celCost</c>, one million by default.</summary>
    public long CostLimit => _functions.Budget;

    /// <summary>The <c>persistence.typeMap</c> (DISL 0.3), or the identity mapping, read at load.</summary>
    internal DislTypeMap TypeMap { get; }

    /// <summary>The operations (§9.3) and the edit gate (§9.1), typed at load.</summary>
    internal DislOperations Operations { get; }

    /// <summary>The toolbox (§7.1 to §7.3): the palette, the library and the context menus, typed at load.</summary>
    internal DislToolbox Toolbox { get; }

    /// <summary>The forms (§7.5), typed at load.</summary>
    internal DislForms Forms { get; }

    /// <summary>The constraints (§8): the rules, their defaults and order, and the built-ins' settings, typed at load.</summary>
    internal DislConstraints Constraints { get; }

    /// <summary>The hooks, the messages and reasons, and the deletion and retype policies (§9), typed at load.</summary>
    internal DislBehavior Behavior { get; }

    /// <summary>What the derivation reads of the notation (§6), typed at load.</summary>
    internal DislNotation Notation { get; }

    /// <summary>The viewpoints (§3.5), typed at load.</summary>
    internal DislViewpoints Viewpoints { get; }

    /// <summary>The compiled expression at <paramref name="pointer"/>, or null when there is none.</summary>
    public DislExpression? ExpressionAt(string pointer) => _expressions.GetValueOrDefault(pointer);

    /// <summary>
    /// The program the loader compiled at <paramref name="pointer"/>, for a part read at load that reads the position as
    /// an Expression whose CEL is <paramref name="source"/>. A position the loader did not walk has none, and is an error
    /// at load (FR-079): the runtime evaluates only what the loader compiled.
    /// </summary>
    internal CelProgram? CompiledAt(string pointer, string source)
    {
        var program = ExpressionAt(pointer)?.Program;
        if (program is null && _walked is not null && !_walked.Contains(pointer))
        {
            _diagnostics?.Add(DislLoader.Error(pointer, $"'{source}' is read here as an expression, but the loader compiles none at this position (DISL §2.5, step 8 of §14.1)."));
        }
        return program;
    }

    /// <summary>
    /// Every program of this specification, by what decides it: those the loader compiled, which
    /// <see cref="Expressions"/> holds by position, and those compiled outside the walk: a label's text, and an id rule
    /// the model computes on first use. They live as
    /// long as the specification does.
    /// </summary>
    internal CelProgramCache<DislProgramKey> Programs { get; }

    /// <summary>
    /// An environment for <paramref name="context"/> (§12.3): the library, the user functions and the
    /// context's variables, plus <paramref name="bindings"/>; for compiling what a runtime builds itself.
    /// </summary>
    public CelEnvironment Environment(DislContext context, params IEnumerable<string> bindings) =>
        _functions.Clone().DeclareVariables(DislContexts.VariablesOf(context)).DeclareVariables(bindings);

    /// <summary>
    /// The id rule that applies to <paramref name="type"/> (§11.5.2): its own, the nearest in its
    /// linearisation, or the top-level one; with the prefix it gives the type (§11.5): the rule's own,
    /// else the top level's for the nearest type in the linearisation that the map names.
    /// </summary>
    internal DislIdRule IdRuleOf(string type)
    {
        var linearisation = Metamodel.TypeOf(type)?.Linearisation ?? [type];
        var rule = linearisation.Select(candidate => IdTypes.GetValueOrDefault(candidate)).FirstOrDefault(found => found is not null) ?? Ids;
        if (rule.Prefix is not null) return rule;
        var prefix = linearisation.Select(candidate => IdPrefixes.GetValueOrDefault(candidate)).FirstOrDefault(found => found is not null) ?? Ids.Prefix;
        return prefix is null ? rule : rule with { Prefix = prefix };
    }

    /// <summary>
    /// Whether <paramref name="element"/>'s id is ephemeral (§11.5.3): the id rule that applies to its type - its own,
    /// the nearest in its linearisation, or the top-level one - states <c>ephemeral: true</c>, or an Expression that holds
    /// for it. Nothing may be stored for an ephemeral id, and a gesture that would store one is refused with
    /// <see cref="EphemeralReason"/>.
    /// </summary>
    public bool IsEphemeral(DislElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return EphemeralityOf(element.Type.Name).Holds(element);
    }

    /// <summary>
    /// Why a gesture that would store <paramref name="element"/>'s id is refused (§11.5.3): the <c>reason</c> of the id
    /// rule that makes it ephemeral, evaluated for it; null when it is not ephemeral or that rule states none.
    /// </summary>
    public string? EphemeralReason(DislElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var rule = EphemeralityOf(element.Type.Name);
        return rule.Holds(element) ? rule.Reason?.Text(this, DislEphemerality.Variables(element)) : null;
    }

    private DislEphemerality EphemeralityOf(string type)
    {
        var linearisation = Metamodel.TypeOf(type)?.Linearisation ?? [type];
        return linearisation.Select(candidate => EphemeralityByType.GetValueOrDefault(candidate)).FirstOrDefault(found => found is not null) ?? Ephemerality;
    }

    /// <summary>A rule as declared; its prefix only when it is one string, which a map (top level only) is not.</summary>
    private static DislIdRule IdRuleOf(JsonElement json) =>
        new(JsonProperties.String(json, "strategy") ?? "uuid-v7", JsonProperties.String(json, "expression"), JsonProperties.String(json, "prefix"));
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
