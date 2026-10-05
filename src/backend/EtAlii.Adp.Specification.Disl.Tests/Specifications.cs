using System.Text.Json.Nodes;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>Small specifications for tests: a minimal valid one, with the parts a test states merged into it.</summary>
internal static class Specifications
{
    /// <summary>A minimal valid specification with <paramref name="parts"/> (JSON object members) added or replacing its own.</summary>
    public static string With(string parts = "")
    {
        var root = JsonNode.Parse("""
            {
              "disl": "0.2",
              "language": { "id": "org.example.test", "version": "1.0.0" },
              "metamodel": { "types": { "Thing": { "attributes": { "name": { "type": "string" } } } } }
            }
            """)!.AsObject();
        if (parts.Length > 0)
        {
            foreach (var (key, value) in JsonNode.Parse("{" + parts + "}")!.AsObject().ToList())
            {
                root[key] = value?.DeepClone();
            }
        }
        return root.ToJsonString();
    }

    /// <summary>Loads <paramref name="json"/> and gives the specification, failing with the diagnostics when it does not load.</summary>
    public static DislSpecification Loaded(string json)
    {
        var result = DislLoader.Load(json);
        return result.Specification ?? throw new InvalidOperationException("The specification does not load: " + string.Join("; ", result.Diagnostics));
    }

    /// <summary>The diagnostics of <paramref name="json"/> as text a test can compare.</summary>
    public static IReadOnlyList<string> Diagnostics(string json) => [.. DislLoader.Load(json).Diagnostics.Select(diagnostic => diagnostic.ToString())];
}
