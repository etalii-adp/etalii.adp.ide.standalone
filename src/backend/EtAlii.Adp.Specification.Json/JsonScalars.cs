using System.Text.Json;

namespace EtAlii.Adp.Specification.Json;

/// <summary>JSON scalars as the values CEL evaluates (CEL's string, int, double and bool).</summary>
public static class JsonScalars
{
    /// <summary>
    /// A string as a string; a number written whole, within an int's range, as an int (<see cref="long"/>) and any
    /// other number as a double; a boolean as a bool; null for null, an object or an array.
    /// </summary>
    /// <remarks>
    /// A whole number is an int as a body's reader reads one, so that a binding's <c>"default": 0</c> and a written
    /// <c>0</c> are one CEL type, and a specification's literal <c>7</c> is taken by <c>%</c> and divides whole
    /// (behaviour change B-38). The int is boxed as itself: a conditional of long and double would be a double.
    /// </remarks>
    public static object? ToCel(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.TryGetInt64(out var integer) ? (object)integer : value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };
}
