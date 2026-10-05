using System.Collections;
using System.Globalization;
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

    /// <summary>
    /// A value a format binding read (FBL: text, <see cref="long"/>, <see cref="double"/>,
    /// <see cref="bool"/>, lists, or null for an empty scalar) as <paramref name="attribute"/>'s type
    /// has it in CEL; false when it does not fit, so the attribute stays unset and reads as its default.
    /// </summary>
    /// <remarks>
    /// A scalar is read through its text, as written: <c>int</c> and <c>number</c> parse it,
    /// <c>yearMonth</c> reads the <c>±YYYY-MM</c> form (§4.2), the text types take it as it is, and an
    /// empty scalar is the empty text. An enum maps a stored form to its key; a form the enum lacks is
    /// kept as written (§4.5), for <c>std.facets</c> and the rules to report. A <c>many</c> attribute
    /// takes a list and keeps the items that fit.
    /// </remarks>
    public static bool TryFromRead(object? raw, DislAttribute attribute, DislMetamodel metamodel, out object? value)
    {
        value = null;
        if (attribute.Many)
        {
            if (raw is string or null || raw is not IEnumerable items) return false;
            var list = new List<object?>();
            foreach (var item in items)
            {
                if (OneFromRead(item, attribute.Type, metamodel, out var one)) list.Add(one);
            }
            value = list;
            return true;
        }
        return OneFromRead(raw, attribute.Type, metamodel, out value);
    }

    private static bool OneFromRead(object? raw, string type, DislMetamodel metamodel, out object? value)
    {
        value = null;
        if (type == "json" || metamodel.DataTypes.Contains(type))
        {
            value = raw;
            return true;
        }
        if (raw is IEnumerable and not string) return false;
        var text = raw switch
        {
            null => "",
            string written => written,
            bool flag => flag ? "true" : "false",
            IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
            _ => raw.ToString() ?? "",
        };
        switch (type)
        {
            case "int":
                if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) return false;
                value = integer;
                return true;
            case "number":
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var real)) return false;
                value = real;
                return true;
            case "bool":
                if (text is not ("true" or "false")) return false;
                value = text == "true";
                return true;
            case "yearMonth":
                value = YearMonth.Parse(text);
                return value is not null;
            case "date" or "datetime" or "time" or "duration" or "binary":
                value = text;
                return true;
        }
        value = metamodel.Enums.TryGetValue(type, out var enumeration) ? enumeration.StoredAs(text)?.Key ?? text : text;
        return true;
    }

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
