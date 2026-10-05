using System.Text.Json;
using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// Attribute values as CEL sees them (DISL §4.2, §12.2): <c>int</c> and <c>yearMonth</c> as
/// <see cref="long"/>, <c>number</c> as <see cref="double"/>, the text types and enum keys as
/// <see cref="string"/>, <c>many</c> as a list, a data type as a <see cref="CelMap"/>, a reference as
/// the id it stores.
/// </summary>
/// <remarks>
/// This CEL has no <c>timestamp</c>, <c>duration</c> or <c>bytes</c> values, so <c>date</c>,
/// <c>datetime</c>, <c>time</c>, <c>duration</c> and <c>binary</c> keep their JSON text and read as
/// <c>null</c> when unset. Neither bundled definition declares one.
/// </remarks>
internal static class DislValues
{
    /// <summary>What an unset attribute reads as: its default, else its type's zero value (§4.3).</summary>
    public static object? Unset(DislAttribute attribute, DislMetamodel metamodel)
    {
        if (attribute.Default is { } literal && FromJson(literal, attribute, metamodel) is { } value) return value;
        if (attribute.Many) return new List<object?>();
        return attribute.Type switch
        {
            "string" or "text" or "color" or "uri" or "expression" => "",
            "int" or "yearMonth" => 0L,
            "number" => 0.0,
            "bool" => false,
            _ when metamodel.Enums.ContainsKey(attribute.Type) => "",
            _ when metamodel.DataTypes.Contains(attribute.Type) => new CelMap(),
            _ => null,
        };
    }

    /// <summary>A JSON value of <paramref name="attribute"/>'s type as CEL sees it; null when it is not of that type.</summary>
    public static object? FromJson(JsonElement json, DislAttribute attribute, DislMetamodel metamodel)
    {
        if (attribute.Many)
        {
            if (json.ValueKind != JsonValueKind.Array) return null;
            var items = new List<object?>();
            foreach (var item in json.EnumerateArray())
            {
                if (One(item, attribute.Type, metamodel) is not { } value) return null;
                items.Add(value);
            }
            return items;
        }
        return One(json, attribute.Type, metamodel);
    }

    private static object? One(JsonElement json, string type, DislMetamodel metamodel) => (type, json.ValueKind) switch
    {
        ("int", JsonValueKind.Number) => json.TryGetInt64(out var integer) ? integer : null,
        ("number", JsonValueKind.Number) => json.GetDouble(),
        ("bool", JsonValueKind.True or JsonValueKind.False) => json.GetBoolean(),
        ("yearMonth", JsonValueKind.String) => YearMonth.Parse(json.GetString()!),
        ("json", _) => Json(json),
        (_, JsonValueKind.Object) when metamodel.DataTypes.Contains(type) => Json(json),
        ("int" or "number" or "bool" or "yearMonth", _) => null,
        (_, JsonValueKind.String) => json.GetString(),
        _ => null,
    };

    /// <summary>Any JSON as CEL values: objects as maps, arrays as lists, integral numbers as <c>int</c>.</summary>
    public static object? Json(JsonElement json) => json.ValueKind switch
    {
        JsonValueKind.Object => json.EnumerateObject().Aggregate(new CelMap(), (map, property) =>
        {
            map[property.Name] = Json(property.Value);
            return map;
        }),
        JsonValueKind.Array => json.EnumerateArray().Select(Json).ToList(),
        JsonValueKind.String => json.GetString(),
        JsonValueKind.Number => json.TryGetInt64(out var integer) ? integer : json.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };
}
