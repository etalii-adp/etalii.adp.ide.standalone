using System.Text.Json;

namespace EtAlii.Adp.Specification.Json;

/// <summary>JSON scalars as the values CEL evaluates (CEL's string, int, double and bool).</summary>
public static class JsonScalars
{
    /// <summary>A string, a whole number as an int, any other number as a double, a boolean; null for null, an object or an array.</summary>
    public static object? ToCel(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.TryGetInt64(out var integer) ? integer : value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };
}
