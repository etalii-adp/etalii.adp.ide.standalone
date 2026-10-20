using System.Text.Json;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Json;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// A specification's literal values as CEL sees them (DISL §4.2, §12.2), converted at load: an attribute's default, a
/// tool's initial value, an action's or a plugin argument's literal. What the model, the derivation and the behaviour
/// read while a diagram runs is the converted value, never the JSON.
/// </summary>
internal static class DislLiterals
{
    /// <summary>
    /// A JSON value of an attribute of <paramref name="type"/>, a list of them when <paramref name="many"/>, as CEL
    /// sees it (<see cref="DislValues"/>); null when it is not of that type.
    /// </summary>
    public static object? FromJson(JsonElement json, string type, bool many, IReadOnlySet<string> dataTypes)
    {
        if (many)
        {
            if (json.ValueKind != JsonValueKind.Array) return null;
            var items = new List<object?>();
            foreach (var item in json.EnumerateArray())
            {
                if (One(item, type, dataTypes) is not { } value) return null;
                items.Add(value);
            }
            return items;
        }
        return One(json, type, dataTypes);
    }

    private static object? One(JsonElement json, string type, IReadOnlySet<string> dataTypes) => (type, json.ValueKind) switch
    {
        ("int", JsonValueKind.Number) => json.TryGetInt64(out var integer) ? integer : null,
        ("number", JsonValueKind.Number) => json.GetDouble(),
        ("bool", JsonValueKind.True or JsonValueKind.False) => json.GetBoolean(),
        ("yearMonth", JsonValueKind.String) => YearMonth.Parse(json.GetString()!),
        ("json", _) => Json(json),
        (_, JsonValueKind.Object) when dataTypes.Contains(type) => Json(json),
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
        _ => JsonScalars.ToCel(json),
    };
}
