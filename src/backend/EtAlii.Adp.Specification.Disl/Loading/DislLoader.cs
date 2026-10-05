using System.Text;
using System.Text.Json;
using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>What loading a specification gives: the specification, when it has no errors, and every diagnostic.</summary>
public sealed record DislLoadResult(DislSpecification? Specification, IReadOnlyList<DislDiagnostic> Diagnostics)
{
    public IEnumerable<DislDiagnostic> Errors => Diagnostics.Where(diagnostic => diagnostic.Severity == DislSeverity.Error);
}

/// <summary>
/// Loads a DISL specification as DISL §14.1 says, in its order: <b>1</b> parse, rejecting duplicate
/// keys; <b>2</b> check the version; <b>5</b> resolve names; <b>6</b> flatten inheritance; <b>8</b>
/// compile every expression in its context, the user functions in declaration order; and the
/// semantic checks of step 9 those steps rest on (reserved attribute names, user function rules).
/// </summary>
/// <remarks>
/// <para>
/// <b>Not done here</b>, and not claimed: imports (step 3; a specification that declares any is
/// refused), JSON Schema validation (step 4; <c>validate-examples.py</c> in etalii.adp does it for the
/// bundled definitions), generated built-ins (step 7), static type-checking and cost estimation
/// (step 8; CEL here is checked for names and arity, then evaluated dynamically under the cost limit),
/// and the notation-, coordinate- and form-specific rules of step 9.
/// </para>
/// <para>
/// <b>Name resolution covers the references every derivation reads</b>: supertypes, attribute types,
/// relation ends, containment, label attributes, palette and context tools, operations, hooks,
/// deletion and retype policies, constraint scopes, id rules, forms, node and edge notations and
/// viewpoint type lists. Styles, shapes, markers and icons are not resolved.
/// </para>
/// </remarks>
public static class DislLoader
{
    /// <summary>The DISL major version this runtime reads, and the highest minor it knows.</summary>
    private const int Major = 0;
    private const int Minor = 2;

    /// <summary>The default per-evaluation cost limit of DISL §2.5.</summary>
    private const long DefaultCostLimit = 1_000_000;

    /// <summary>Attribute names that are built-in members of elements in CEL (DISL §2.2).</summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "id", "type", "kind", "parent", "children", "descendants", "ancestors", "incoming", "outgoing", "source", "target",
        "sourcePort", "targetPort", "ports", "owner", "view", "diagram", "self", "value", "item", "index", "env", "old", "event", "detail",
        "in", "as", "break", "const", "continue", "else", "for", "function", "if", "import", "let", "loop", "package", "namespace",
        "return", "var", "void", "while", "true", "false", "null",
    };

    public static DislLoadResult Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return Load(Encoding.UTF8.GetBytes(json));
    }

    public static DislLoadResult Load(ReadOnlyMemory<byte> utf8)
    {
        var diagnostics = new List<DislDiagnostic>();

        // Step 1: parse, rejecting a byte-order mark and duplicate keys (§2.1).
        if (utf8.Span.StartsWith("﻿"u8))
        {
            diagnostics.Add(Error("", "A specification is UTF-8 without a byte-order mark (DISL §2.1)."));
            return new DislLoadResult(null, diagnostics);
        }
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 256 });
        }
        catch (JsonException e)
        {
            diagnostics.Add(Error("", $"The specification is not JSON: {e.Message}"));
            return new DislLoadResult(null, diagnostics);
        }

        // A clone owns its bytes, so the specification outlives the caller's buffer.
        JsonElement root;
        using (document)
        {
            root = document.RootElement.Clone();
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(Error("", "A specification is a JSON object (DISL §2.1)."));
            return new DislLoadResult(null, diagnostics);
        }
        DuplicateKeys(root, "", diagnostics);

        // Step 2: the version (§2.9).
        Version(root, diagnostics);
        if (!root.TryGetProperty("language", out var language) || language.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(Error("/language", "A specification declares its language (DISL §3.1)."));
        }
        else
        {
            if (DislJson.String(language, "id") is null) diagnostics.Add(Error("/language/id", "The language has no id (DISL §3.2)."));
            if (DislJson.String(language, "version") is null) diagnostics.Add(Error("/language/version", "The language has no version (DISL §3.2)."));
        }
        if (!root.TryGetProperty("metamodel", out var metamodelJson) || metamodelJson.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(Error("/metamodel", "A specification declares its metamodel (DISL §3.1)."));
        }
        if (root.TryGetProperty("imports", out var imports) && imports.ValueKind == JsonValueKind.Array && imports.GetArrayLength() > 0)
        {
            diagnostics.Add(Error("/imports", "This runtime does not resolve imports (DISL §3.3, step 3 of §14.1)."));
        }
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DislSeverity.Error)) return new DislLoadResult(null, diagnostics);

        // Steps 5 and 6: the metamodel, names resolved and inheritance flattened (§4.7), and the other references.
        var metamodel = DislInheritance.Build(metamodelJson, diagnostics);
        DislNames.Resolve(root, metamodel, diagnostics);

        // Step 8: the user functions in declaration order, then every expression in its context.
        var functions = Functions(root, diagnostics);
        var environment = CelEnvironment.Standard();
        environment.Budget = CostLimit(root);
        DislCelLibrary.Register(environment, name => metamodel.Enums.GetValueOrDefault(name));
        UserFunctions.Compile(environment, functions, diagnostics);
        var expressions = Compile(root, environment, diagnostics);

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DislSeverity.Error)) return new DislLoadResult(null, diagnostics);
        return new DislLoadResult(new DislSpecification(root, metamodel, functions, environment, expressions), diagnostics);
    }

    internal static DislDiagnostic Error(string pointer, string message) => new(pointer, DislSeverity.Error, message);

    internal static DislDiagnostic Warning(string pointer, string message) => new(pointer, DislSeverity.Warning, message);

    /// <summary>Every object's keys are unique (§2.1); a repeated key is reported at its second occurrence.</summary>
    private static void DuplicateKeys(JsonElement element, string pointer, List<DislDiagnostic> diagnostics)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    var child = DislJson.Pointer(pointer, property.Name);
                    if (!seen.Add(property.Name)) diagnostics.Add(Error(child, $"The key '{property.Name}' appears more than once in one object (DISL §2.1)."));
                    DuplicateKeys(property.Value, child, diagnostics);
                }
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    DuplicateKeys(item, DislJson.Pointer(pointer, index++), diagnostics);
                }
                break;
        }
    }

    /// <summary>The <c>disl</c> version, or its deprecated alias <c>dedl</c> (§2.9, §18): a higher major is refused, a higher minor warned about.</summary>
    private static void Version(JsonElement root, List<DislDiagnostic> diagnostics)
    {
        var disl = DislJson.String(root, "disl");
        var dedl = DislJson.String(root, "dedl");
        if (disl is not null && dedl is not null)
        {
            diagnostics.Add(Error("/dedl", "A specification carries exactly one of 'disl' and its deprecated alias 'dedl' (DISL §18)."));
            return;
        }
        var pointer = disl is null ? "/dedl" : "/disl";
        var version = disl ?? dedl;
        if (version is null)
        {
            diagnostics.Add(Error("/disl", "A specification declares the DISL version it targets (DISL §2.9)."));
            return;
        }
        var parts = version.Split('.');
        if (parts.Length != 2 || !int.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var minor))
        {
            diagnostics.Add(Error(pointer, $"'{version}' is not a DISL version written as major.minor (DISL §2.9)."));
            return;
        }
        if (major > Major)
        {
            diagnostics.Add(Error(pointer, $"This runtime reads DISL {Major}.x; DISL {version} is refused (DISL §2.9)."));
        }
        else if (major == Major && minor > Minor)
        {
            diagnostics.Add(Warning(pointer, $"This runtime knows DISL up to {Major}.{Minor}; what DISL {version} adds is not read (DISL §2.9)."));
        }
        if (dedl is not null)
        {
            diagnostics.Add(Warning(pointer, "'dedl' is the deprecated alias of 'disl' (DISL §18)."));
        }
    }

    private static long CostLimit(JsonElement root) =>
        root.TryGetProperty("language", out var language) && language.TryGetProperty("limits", out var limits) && limits.ValueKind == JsonValueKind.Object
        && limits.TryGetProperty("celCost", out var cost) && cost.TryGetInt64(out var value) && value > 0
            ? value
            : DefaultCostLimit;

    private static List<DislFunctionDeclaration> Functions(JsonElement root, List<DislDiagnostic> diagnostics)
    {
        var functions = new List<DislFunctionDeclaration>();
        foreach (var function in DislJson.Members(root, "functions"))
        {
            var pointer = DislJson.Pointer("/functions", function.Name);
            var json = function.Value;
            var cel = DislJson.String(json, "cel");
            if (cel is null)
            {
                diagnostics.Add(Error(pointer, $"The function '{function.Name}' has no cel body (DISL §3.4)."));
                continue;
            }
            var parameters = json.TryGetProperty("params", out var declared) && declared.ValueKind == JsonValueKind.Array
                ? declared.EnumerateArray().Select(parameter => new DislParameter(DislJson.String(parameter, "name") ?? "", DislJson.String(parameter, "type") ?? "dyn")).ToList()
                : [];
            DislRecursion? recursion = null;
            if (json.TryGetProperty("recursion", out var bounded) && bounded.ValueKind == JsonValueKind.Object)
            {
                recursion = new DislRecursion(
                    bounded.TryGetProperty("maxDepth", out var depth) && depth.TryGetInt32(out var maxDepth) ? maxDepth : 0,
                    DislJson.String(bounded, "atMaxDepth") ?? "null");
            }
            functions.Add(new DislFunctionDeclaration(function.Name, parameters, DislJson.String(json, "returns") ?? "dyn", cel, DislJson.Strings(json, "uses"), recursion));
        }
        return functions;
    }

    /// <summary>Compiles every expression the walker finds, each in its context with the names bound around it.</summary>
    private static List<DislExpression> Compile(JsonElement root, CelEnvironment functions, List<DislDiagnostic> diagnostics)
    {
        var expressions = new List<DislExpression>();
        var everything = DislContexts.All.SelectMany(DislContexts.VariablesOf).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var site in DislExpressionWalker.Sites(root))
        {
            var context = site.Context;
            if (context.Length == 0)
            {
                diagnostics.Add(Warning(site.Pointer, "This runtime does not know the CEL context of this position; it was compiled with every variable of any context."));
            }
            var environment = functions.Clone()
                .DeclareVariables(context.Length == 0 ? everything : DislContexts.VariablesOf(context))
                .DeclareVariables(site.Bindings);
            try
            {
                expressions.Add(new DislExpression(site.Pointer, context, environment.Compile(site.Source)));
            }
            catch (CelException e)
            {
                diagnostics.Add(Error(site.Pointer, $"{e.Message} (context {(context.Length == 0 ? "unknown" : context)})"));
            }
        }
        return expressions;
    }

    /// <summary>Whether <paramref name="name"/> may not be an attribute name (DISL §2.2).</summary>
    internal static bool IsReserved(string name) => Reserved.Contains(name) || name.StartsWith('_') || name.StartsWith('$');
}
