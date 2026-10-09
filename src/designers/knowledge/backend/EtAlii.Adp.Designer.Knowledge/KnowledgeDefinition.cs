using System.Security.Cryptography;
using System.Text.Json;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// The designer's definition as bundled from etalii-adp/etalii.adp: the bindings a knowledge
/// file is read and written with (<c>knowledge.fbl</c>), and what the specification
/// (<c>knowledge.des</c>) says that a binding does not carry - the type of each attribute, and
/// how each element's id is derived when the file does not store one.
/// </summary>
/// <remarks>
/// Loaded once. A definition that does not load is a broken build of this module, not a state a
/// user can be in, so it throws where it is first asked for rather than being reported.
/// </remarks>
internal static class KnowledgeDefinition
{
    private static readonly Lazy<IReadOnlyDictionary<string, FblBinding>> _bindings = new(LoadBindings);

    private static readonly Lazy<IReadOnlyDictionary<(string Type, string Attribute), string>> _attributeTypes = new(LoadAttributeTypes);

    private static readonly Lazy<Dictionary<string, (string Label, IReadOnlyList<string> Comparisons)?>> _valueTypes = new(LoadValueTypes);

    /// <summary>The binding a body of this extension is read and written with, or null for an extension no binding has.</summary>
    public static FblBinding? BindingFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".yaml" or ".yml" => _bindings.Value["yaml"],
        ".json" => _bindings.Value["json"],
        ".xml" => _bindings.Value["xml"],
        _ => null,
    };

    /// <summary>
    /// Whether a body of this extension gets a new entry after the last entry its own rule reads,
    /// rather than at the end of the list: the YAML family does, the JSON and XML families do not.
    /// Only a list two rules read shows the difference - a filter's conditions and groups - and the
    /// designer evens it out there, so a filter reads the same in every format.
    /// </summary>
    public static bool AddsBesideItsRule(string extension) => extension.ToLowerInvariant() is ".yaml" or ".yml";

    /// <summary>The type the specification gives an attribute of an element type, or null where it gives none.</summary>
    public static string? AttributeType(string type, string attribute) => _attributeTypes.Value.GetValueOrDefault((type, attribute));

    /// <summary>The name the specification gives a value type, or null for a type it does not have.</summary>
    public static string? ValueTypeLabel(string valueType) => _valueTypes.Value.GetValueOrDefault(valueType)?.Label;

    /// <summary>The comparisons the specification gives a value type, in its order; none for a type it does not have.</summary>
    public static IReadOnlyList<string> Comparisons(string valueType) => _valueTypes.Value.GetValueOrDefault(valueType)?.Comparisons ?? [];

    /// <summary>The value types the specification has, in its order.</summary>
    public static IReadOnlyList<string> ValueTypes => [.. _valueTypes.Value.Keys];

    /// <summary>How many attributes the specification types: none would mean it was not read.</summary>
    public static int TypedAttributeCount => _attributeTypes.Value.Count;

    /// <summary>
    /// The id of an element whose rule stores none, as <c>persistence.ids</c> of the specification
    /// derives it: a column and a sort from their view and property, a cell from its row and
    /// property, a cell's item from its cell and what it names, a group setting from its view,
    /// slot and key, and a filter's conditions and groups from their place.
    /// </summary>
    public static string? DeriveId(IdRequest request) => request.Rule switch
    {
        "table" => "table",
        "column" => $"{request.ParentId}/columns/{Text(request, "property")}",
        "sort" => $"{request.ParentId}/sorts/{Text(request, "property")}",
        "condition1" or "filterGroup1" => FormattableString.Invariant($"{request.ParentId}/filter/{request.PositionInSlot}"),
        "condition2" or "filterGroup2" or "condition3" => FormattableString.Invariant($"{request.ParentId}/{request.PositionInSlot}"),
        "groupOrder" or "hiddenGroup" or "collapsedGroup" => $"{request.ParentId}/{request.ParentSlot}/{Text(request, "key")}",
        "cell" => $"{request.ParentId}/{Text(request, "property")}",
        "cellOption" => $"{request.ParentId}/{Text(request, "option")}",
        "cellRow" => $"{request.ParentId}/{Text(request, "row")}",
        _ => null,
    };

    /// <summary>The bytes of a bundled file, by the name it is embedded under.</summary>
    public static byte[] Resource(string name)
    {
        using var stream = typeof(KnowledgeDefinition).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The Knowledge module was built without its '{name}'.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>The sha256 of a bundled file, as its provenance records it: lower-case hexadecimal.</summary>
    public static string Sha256Of(string name) => Convert.ToHexStringLower(SHA256.HashData(Resource(name)));

    private static string Text(IdRequest request, string name) => KnowledgeValues.Text(request.Attributes.GetValueOrDefault(name));

    private static IReadOnlyDictionary<string, FblBinding> LoadBindings()
    {
        var problems = FblDocumentLoader.Load(Resource("knowledge.fbl"), out var document);
        if (document is null || problems.Any(problem => problem.Severity == ProblemSeverity.Error))
        {
            throw new InvalidOperationException($"The bundled knowledge.fbl does not load: {string.Join("; ", problems)}");
        }

        return document.Bindings;
    }

    private static Dictionary<string, (string Label, IReadOnlyList<string> Comparisons)?> LoadValueTypes()
    {
        using var specification = JsonDocument.Parse(Resource("knowledge.des"));
        var surface = specification.RootElement.GetProperty("surface").GetProperty("valueTypes");
        var types = new Dictionary<string, (string Label, IReadOnlyList<string> Comparisons)?>(StringComparer.Ordinal);
        foreach (var type in specification.RootElement.GetProperty("metamodel").GetProperty("enums").GetProperty("ValueType").GetProperty("values").EnumerateObject())
        {
            IReadOnlyList<string> comparisons = surface.TryGetProperty(type.Name, out var shown) && shown.TryGetProperty("comparisons", out var listed)
                ? [.. listed.EnumerateArray().Select(comparison => comparison.GetString()!)]
                : [];
            types[type.Name] = (type.Value.GetProperty("label").GetString()!, comparisons);
        }

        return types;
    }

    private static IReadOnlyDictionary<(string Type, string Attribute), string> LoadAttributeTypes()
    {
        using var specification = JsonDocument.Parse(Resource("knowledge.des"));
        var types = new Dictionary<(string Type, string Attribute), string>();
        foreach (var type in specification.RootElement.GetProperty("metamodel").GetProperty("types").EnumerateObject())
        {
            if (!type.Value.TryGetProperty("attributes", out var attributes))
            {
                continue;
            }

            foreach (var attribute in attributes.EnumerateObject())
            {
                if (attribute.Value.TryGetProperty("type", out var declared) && declared.ValueKind == JsonValueKind.String)
                {
                    types[(type.Name, attribute.Name)] = declared.GetString()!;
                }
            }
        }

        return types;
    }
}
