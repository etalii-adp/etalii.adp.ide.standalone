using System.Text.Json;

namespace EtAlii.Adp.Specification.Fbl.Tests.Support;

/// <summary>
/// The attribute types of the timeline's specification, read from the vendored
/// <c>definitions/diagrams/timeline.dis</c> as <see cref="KnowledgeTypes"/> reads the Knowledge
/// designer's, following <c>extends</c>: <c>label</c>, <c>begin</c> and <c>row</c> are declared on
/// the abstract <c>Element</c> that <c>Period</c> and <c>Moment</c> extend.
/// </summary>
internal static class TimelineTypes
{
    private static readonly Lazy<Dictionary<(string Element, string Attribute), string>> _types = new(Read);

    /// <summary>How many attributes the specification types, inherited ones included: none means the reading of it broke.</summary>
    public static int Count => _types.Value.Count;

    public static string? Of(string element, string attribute) => _types.Value.GetValueOrDefault((element, attribute));

    private static Dictionary<(string Element, string Attribute), string> Read()
    {
        var path = Path.Combine(Repository.Conformance, "etalii.adp", "definitions", "diagrams", "timeline.dis");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var metamodel = document.RootElement.GetProperty("metamodel");
        var declared = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var group in new[] { "types", "relations" })
        {
            if (!metamodel.TryGetProperty(group, out var types)) continue;
            foreach (var type in types.EnumerateObject()) declared[type.Name] = type.Value.Clone();
        }
        var result = new Dictionary<(string Element, string Attribute), string>();
        foreach (var name in declared.Keys)
        {
            // The type's own attributes first, then each type it extends, nearest first.
            for (string? type = name; type is not null && declared.TryGetValue(type, out var definition); type = definition.TryGetProperty("extends", out var extends) ? extends.GetString() : null)
            {
                if (!definition.TryGetProperty("attributes", out var attributes)) continue;
                foreach (var attribute in attributes.EnumerateObject())
                {
                    if (attribute.Value.TryGetProperty("type", out var kind) && kind.ValueKind == JsonValueKind.String) result.TryAdd((name, attribute.Name), kind.GetString()!);
                }
            }
        }
        return result;
    }
}
