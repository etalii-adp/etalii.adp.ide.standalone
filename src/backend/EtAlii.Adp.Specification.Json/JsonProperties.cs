using System.Text.Json;

namespace EtAlii.Adp.Specification.Json;

/// <summary>
/// The readings of an object's members that both loaders make. Each reads what the member holds when
/// it has the expected kind and gives the absent value otherwise: what is wrong with the document
/// is the schema's to say, not these helpers'.
/// </summary>
public static class JsonProperties
{
    /// <summary>The member <paramref name="name"/> when it is a string; null otherwise.</summary>
    public static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>Whether the member <paramref name="name"/> is <c>true</c>.</summary>
    public static bool Bool(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>The strings of the member <paramref name="name"/> when it is an array; empty otherwise, a single string included.</summary>
    public static IReadOnlyList<string> Strings(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? Strings(value)
            : [];

    /// <summary>The member <paramref name="name"/> as one string or an array of them; empty when absent.</summary>
    public static IReadOnlyList<string> StringOrStrings(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? Strings(value) : [];

    /// <summary><paramref name="value"/> as one string or the strings of an array; anything else is empty.</summary>
    public static IReadOnlyList<string> Strings(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => [value.GetString()!],
        JsonValueKind.Array => [.. value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)],
        _ => [],
    };
}
