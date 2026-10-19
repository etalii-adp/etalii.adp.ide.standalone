using System.Text.Json;
using EtAlii.Adp.Specification.Json;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// Small readings of a specification's JSON that every part of the loader shares. The readings FBL
/// makes as well live in <see cref="JsonProperties"/> and <see cref="JsonPointer"/>; these names forward to
/// them so that the loader's many callers read as before.
/// </summary>
internal static class DislJson
{
    private static readonly IReadOnlyDictionary<string, JsonElement> None = new Dictionary<string, JsonElement>();

    /// <summary>The <c>x-</c> properties of <paramref name="element"/> (DISL §2.8), in order; none for a value that is not an object.</summary>
    public static IReadOnlyDictionary<string, JsonElement> Extensions(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return None;
        Dictionary<string, JsonElement>? found = null;
        foreach (var property in element.EnumerateObject())
        {
            if (!property.Name.StartsWith("x-", StringComparison.Ordinal)) continue;
            (found ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal))[property.Name] = property.Value;
        }
        return found ?? None;
    }

    public static bool Bool(JsonElement element, string name) => JsonProperties.Bool(element, name);

    public static string? String(JsonElement element, string name) => JsonProperties.String(element, name);

    /// <summary>A property that is one string or a list of them (a TypeRef or TypeRef[], DISL §2.7); empty when absent.</summary>
    public static IReadOnlyList<string> Strings(JsonElement element, string name) => JsonProperties.StringOrStrings(element, name);

    public static IReadOnlyList<string> Strings(JsonElement value) => JsonProperties.Strings(value);

    /// <summary>The members of an object property, in order; none when it is absent or not an object.</summary>
    public static IEnumerable<JsonProperty> Members(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value.EnumerateObject()
            : [];

    public static string Pointer(string parent, string name) => JsonPointer.Append(parent, name);

    public static string Pointer(string parent, int index) => JsonPointer.Append(parent, index);
}
