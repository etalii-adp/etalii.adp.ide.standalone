using System.Text.Json;
using EtAlii.Adp.Specification.Json;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>Reads a host's wire ids from a specification's top-level <c>x-&lt;prefix&gt;</c> block into a <see cref="WireIdMap"/>.</summary>
internal static class WireIdMapReader
{
    /// <summary>The map in <paramref name="specification"/>'s top-level <paramref name="key"/>; <see cref="WireIdMap.None"/> when it has none, or it is not an object.</summary>
    public static WireIdMap Read(DislSpecification specification, string key)
    {
        if (!specification.Extensions.TryGetValue(key, out var block) || block.ValueKind != JsonValueKind.Object) return WireIdMap.None;

        var tools = DislJson.Members(block, "tools").ToDictionary(
            tool => tool.Name,
            tool => (JsonProperties.String(tool.Value, "id"), JsonProperties.String(tool.Value, "drop")),
            StringComparer.Ordinal);
        return new WireIdMap(tools, Strings(block, "actions"), Strings(block, "properties"), Strings(block, "types"));
    }

    private static Dictionary<string, string> Strings(JsonElement block, string name) =>
        DislJson.Members(block, name)
            .Where(member => member.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(member => member.Name, member => member.Value.GetString()!, StringComparer.Ordinal);
}
