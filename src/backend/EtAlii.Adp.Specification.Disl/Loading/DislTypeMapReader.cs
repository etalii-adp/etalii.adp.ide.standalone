using System.Text.Json;
using EtAlii.Adp.Specification.Json;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>Reads a specification's <c>persistence.typeMap</c> (DISL 0.3) into a <see cref="DislTypeMap"/>, once, at load.</summary>
internal static class DislTypeMapReader
{
    /// <summary>The type map of <paramref name="persistence"/>, the specification's <c>persistence</c>; the identity mapping without one.</summary>
    public static DislTypeMap Read(JsonElement persistence)
    {
        var mappings = new Dictionary<string, DislTypeMapping>(StringComparer.Ordinal);
        if (persistence.ValueKind == JsonValueKind.Object && persistence.TryGetProperty("typeMap", out var map) && map.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in map.EnumerateObject())
            {
                var renames = DislJson.Members(entry.Value, "attributes")
                    .Where(member => member.Value.ValueKind == JsonValueKind.String)
                    .ToDictionary(member => member.Name, member => member.Value.GetString()!, StringComparer.Ordinal);
                mappings[entry.Name] = new DislTypeMapping(
                    JsonProperties.String(entry.Value, "as") ?? entry.Name,
                    renames,
                    JsonProperties.StringOrStrings(entry.Value, "hostAttributes").ToHashSet(StringComparer.Ordinal));
            }
        }
        return new DislTypeMap(mappings);
    }
}
