using System.Text.Json;

namespace EtAlii.Adp.Specification.Fbl.Tests.Support;

/// <summary>
/// What the reading of a requirement document takes from the vendored <c>definitions/designers/hierarchical-requirement.des</c>,
/// standing in for DESL as <see cref="KnowledgeTypes"/> does for the Knowledge designer: the attributes' types and which
/// hold many values, the binding types it maps as unreadable, its enumerations, and the initial members of its lists.
/// </summary>
internal static class HierarchicalRequirementSpecification
{
    private static readonly Lazy<JsonDocument> _document = new(() =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Repository.Conformance, "etalii.adp", "definitions", "designers", "hierarchical-requirement.des"))));

    private static JsonElement Root => _document.Value.RootElement;

    /// <summary>The reading's options for a body named <paramref name="fileName"/>: the types, the many attributes and the unreadable types.</summary>
    public static FblOptions Options(string fileName) => new()
    {
        FileName = fileName,
        AttributeType = TypeOf,
        Many = Many,
        UnreadableTypes = UnreadableTypes(),
    };

    public static string? TypeOf(string type, string attribute) =>
        Attribute(type, attribute) is { } definition && definition.TryGetProperty("type", out var name) ? name.GetString() : null;

    public static bool Many(string type, string attribute) =>
        Attribute(type, attribute) is { } definition && definition.TryGetProperty("many", out var many) && many.GetBoolean();

    /// <summary>The attributes the specification gives <paramref name="type"/>, in its order.</summary>
    public static IEnumerable<string> AttributesOf(string type) =>
        Root.GetProperty("metamodel").GetProperty("types").TryGetProperty(type, out var definition) && definition.TryGetProperty("attributes", out var attributes)
            ? attributes.EnumerateObject().Select(attribute => attribute.Name).ToList()
            : [];

    /// <summary>The binding types <c>persistence.typeMap</c> maps as unreadable.</summary>
    public static HashSet<string> UnreadableTypes() =>
        Root.GetProperty("persistence").GetProperty("typeMap").EnumerateObject()
            .Where(entry => entry.Value.GetProperty("as").GetString() == "unreadable")
            .Select(entry => entry.Name)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Whether <paramref name="type"/> is one of the specification's enumerations.</summary>
    public static bool IsEnumeration(string type) => Root.GetProperty("metamodel").GetProperty("enums").TryGetProperty(type, out _);

    /// <summary>The key of the value of <paramref name="enumeration"/> stored as <paramref name="stored"/>, as the model holds it; the stored form itself when none is.</summary>
    public static string Key(string enumeration, string stored)
    {
        foreach (var value in Values(enumeration))
        {
            if (Stored(value) == stored) return value.Name;
        }
        return stored;
    }

    /// <summary>The form the file stores the value <paramref name="key"/> of <paramref name="enumeration"/> in.</summary>
    public static string Stored(string enumeration, string key)
    {
        foreach (var value in Values(enumeration))
        {
            if (value.Name == key) return Stored(value);
        }
        return key;
    }

    /// <summary>The initial members of the list <paramref name="settings"/> keeps, by their name and value, in order.</summary>
    public static List<KeyValuePair<string, string>> Initial(string settings)
    {
        var setting = Root.GetProperty("persistence").GetProperty("settings").GetProperty(settings);
        var key = setting.GetProperty("key").GetString()!;
        var value = setting.GetProperty("value").GetString()!;
        return setting.GetProperty("initial").EnumerateArray()
            .Select(member => new KeyValuePair<string, string>(member.GetProperty(key).GetString()!, member.GetProperty(value).GetString()!))
            .ToList();
    }

    private static IEnumerable<JsonProperty> Values(string enumeration) =>
        Root.GetProperty("metamodel").GetProperty("enums").GetProperty(enumeration).GetProperty("values").EnumerateObject();

    private static string Stored(JsonProperty value) => value.Value.TryGetProperty("value", out var stored) ? stored.GetString()! : value.Name;

    private static JsonElement? Attribute(string type, string attribute) =>
        Root.GetProperty("metamodel").GetProperty("types").TryGetProperty(type, out var definition)
        && definition.TryGetProperty("attributes", out var attributes)
        && attributes.TryGetProperty(attribute, out var found)
            ? found
            : null;
}
