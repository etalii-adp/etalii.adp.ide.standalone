using System.Text.Json;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>Small readings of a specification's JSON that every part of the loader shares.</summary>
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

    public static bool Bool(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    public static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>A property that is one string or a list of them (a TypeRef or TypeRef[], DISL §2.7); empty when absent.</summary>
    public static IReadOnlyList<string> Strings(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? Strings(value) : [];

    public static IReadOnlyList<string> Strings(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => [value.GetString()!],
        JsonValueKind.Array => [.. value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)],
        _ => [],
    };

    /// <summary>The members of an object property, in order; none when it is absent or not an object.</summary>
    public static IEnumerable<JsonProperty> Members(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value.EnumerateObject()
            : [];

    /// <summary>One reference token of a JSON Pointer (RFC 6901): <c>~</c> and <c>/</c> escaped.</summary>
    public static string Token(string name) => name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    public static string Pointer(string parent, string name) => parent + "/" + Token(name);

    public static string Pointer(string parent, int index) => parent + "/" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
