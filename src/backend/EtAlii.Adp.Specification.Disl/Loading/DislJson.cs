using System.Text.Json;
using EtAlii.Adp.Specification.Json;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// The two readings of a specification's JSON that only DISL's loader makes: an object's <c>x-</c> properties and
/// the members of an object property. The readings FBL makes as well are <see cref="JsonProperties"/>'s, and the
/// pointers a diagnostic names are <see cref="JsonPointer"/>'s.
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

    /// <summary>The members of an object property, in order; none when it is absent or not an object.</summary>
    public static IEnumerable<JsonProperty> Members(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value.EnumerateObject()
            : [];
}
