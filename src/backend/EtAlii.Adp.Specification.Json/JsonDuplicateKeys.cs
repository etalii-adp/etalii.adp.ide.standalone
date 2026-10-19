using System.Text.Json;

namespace EtAlii.Adp.Specification.Json;

/// <summary>
/// The duplicate keys of a parsed document, which both specifications forbid (DISL §2.1, FBL §2.1).
/// A parsed document keeps every occurrence of a key, so the repeats are still there to be found.
/// </summary>
public static class JsonDuplicateKeys
{
    /// <summary>
    /// An error for every key that repeats in one object, at its second and later occurrences, in
    /// document order; <paramref name="section"/> is the section that forbids them, as the error cites it.
    /// </summary>
    public static IEnumerable<LoadDiagnostic> Find(JsonElement root, string section) =>
        Repeats(root, "").Select(repeat => new LoadDiagnostic(repeat.Pointer, LoadSeverity.Error, $"The key '{repeat.Key}' appears more than once in one object ({section})."));

    private static IEnumerable<(string Pointer, string Key)> Repeats(JsonElement element, string pointer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    var child = JsonPointer.Append(pointer, property.Name);
                    if (!seen.Add(property.Name)) yield return (child, property.Name);
                    foreach (var repeat in Repeats(property.Value, child)) yield return repeat;
                }
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var repeat in Repeats(item, JsonPointer.Append(pointer, index++))) yield return repeat;
                }
                break;
        }
    }
}
