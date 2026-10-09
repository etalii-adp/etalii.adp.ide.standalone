using System.Text.Json;

namespace EtAlii.Adp.Specification.Fbl.Tests.Support;

/// <summary>
/// The attribute types of the Knowledge designer's specification, read from the vendored
/// <c>definitions/designers/knowledge.des</c>, standing in for DESL as <see cref="KnowledgeIds"/>
/// does for its id derivations.
/// </summary>
internal static class KnowledgeTypes
{
    private static readonly Lazy<Dictionary<(string Element, string Attribute), string>> _types = new(Read);

    /// <summary>How many attributes the specification types: none means the reading of it broke.</summary>
    public static int Count => _types.Value.Count;

    public static string? Of(string element, string attribute) => _types.Value.GetValueOrDefault((element, attribute));

    private static Dictionary<(string Element, string Attribute), string> Read()
    {
        var path = Path.Combine(Repository.Conformance, "etalii.adp", "definitions", "designers", "knowledge.des");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var types = new Dictionary<(string Element, string Attribute), string>();
        foreach (var element in document.RootElement.GetProperty("metamodel").GetProperty("types").EnumerateObject())
        {
            if (!element.Value.TryGetProperty("attributes", out var attributes)) continue;
            foreach (var attribute in attributes.EnumerateObject())
            {
                if (attribute.Value.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
                {
                    types[(element.Name, attribute.Name)] = type.GetString()!;
                }
            }
        }
        return types;
    }
}
