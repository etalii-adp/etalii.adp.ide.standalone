using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;

namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// The designer's definition as bundled from etalii-adp/etalii.adp: the binding a requirement document is read and written
/// with (<c>hierarchical-requirement.fbl</c>, its one binding <c>markdown</c>), and what the specification
/// (<c>hierarchical-requirement.des</c>) says that a binding does not carry: the type of each attribute and which hold
/// many values, the binding types it maps as unreadable, the mentions an attribute's text holds, and the initial members
/// of each list.
/// </summary>
/// <remarks>
/// Loaded once, on first use. A definition that does not load is a broken build of this module, not a state a user can
/// be in, so it throws where it is first asked for rather than being reported.
/// </remarks>
internal static class HierarchicalRequirementDefinition
{
    public const string Origin = "etalii/hierarchical-requirement";

    private static readonly Lazy<FblBinding> _binding = new(() => BundledBinding.Load(typeof(HierarchicalRequirementDefinition).Assembly, "hierarchical-requirement.fbl", "markdown"));

    private static readonly Lazy<Specification> _specification = new(Load);

    /// <summary>The binding of the Markdown file.</summary>
    public static FblBinding Binding => _binding.Value;

    /// <summary>The reading's options for a file named <paramref name="fileName"/>: the specification's types, its many attributes and its unreadable types.</summary>
    public static FblOptions Options(string fileName) => new()
    {
        FileName = fileName,
        AttributeType = AttributeType,
        Many = IsMany,
        UnreadableTypes = _specification.Value.Unreadable,
    };

    /// <summary>The type the specification gives an attribute of an element type, or null where it gives none.</summary>
    private static string? AttributeType(string type, string attribute) => _specification.Value.Types.GetValueOrDefault((type, attribute));

    /// <summary>Whether an attribute holds many values (DESL 0.2 section 4.2), as an item's and a requirement's tags do.</summary>
    public static bool IsMany(string type, string attribute) => _specification.Value.Many.Contains((type, attribute));

    /// <summary>The expression of the mentions an attribute's text holds (DESL 0.2 section 4.3), or null for one that holds none.</summary>
    public static Regex? Mentions(string type, string attribute) => _specification.Value.MentionPatterns.GetValueOrDefault((type, attribute));

    /// <summary>The initial members of the list <paramref name="setting"/> keeps, as name and colour, in their order.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Initial(string setting) => _specification.Value.InitialMembers.GetValueOrDefault(setting) ?? [];

    /// <summary>How many attribute types the specification gives: none would mean it was not read.</summary>
    public static int TypedAttributeCount => _specification.Value.Types.Count;

    /// <summary>The bytes of a bundled file, by the name it is embedded under.</summary>
    public static byte[] Resource(string name)
    {
        using var stream = typeof(HierarchicalRequirementDefinition).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The Hierarchical Requirement module was built without its '{name}'.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>The sha256 of a bundled file, as its provenance records it: lower-case hexadecimal.</summary>
    public static string Sha256Of(string name) => Convert.ToHexStringLower(SHA256.HashData(Resource(name)));

    private static Specification Load()
    {
        using var document = JsonDocument.Parse(Resource("hierarchical-requirement.des"));
        var root = document.RootElement;
        var specification = new Specification();
        foreach (var type in root.GetProperty("metamodel").GetProperty("types").EnumerateObject())
        {
            if (!type.Value.TryGetProperty("attributes", out var attributes)) continue;
            foreach (var attribute in attributes.EnumerateObject())
            {
                var key = (type.Name, attribute.Name);
                if (attribute.Value.TryGetProperty("type", out var declared)) specification.Types[key] = declared.GetString()!;
                if (attribute.Value.TryGetProperty("many", out var many) && many.GetBoolean()) specification.Many.Add(key);
                if (attribute.Value.TryGetProperty("mentions", out var mentions))
                {
                    specification.MentionPatterns[key] = new Regex(mentions.GetProperty("pattern").GetString()!, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
                }
            }
        }
        var persistence = root.GetProperty("persistence");
        foreach (var entry in persistence.GetProperty("typeMap").EnumerateObject())
        {
            if (entry.Value.GetProperty("as").GetString() == "unreadable") specification.Unreadable.Add(entry.Name);
        }
        foreach (var setting in persistence.GetProperty("settings").EnumerateObject())
        {
            if (!setting.Value.TryGetProperty("initial", out var initial)) continue;
            var name = setting.Value.GetProperty("key").GetString()!;
            var value = setting.Value.GetProperty("value").GetString()!;
            specification.InitialMembers[setting.Name] = [.. initial.EnumerateArray().Select(member => new KeyValuePair<string, string>(member.GetProperty(name).GetString()!, member.GetProperty(value).GetString()!))];
        }
        return specification;
    }

    /// <summary>What is read from the specification, by element type and attribute.</summary>
    private sealed class Specification
    {
        public Dictionary<(string Type, string Attribute), string> Types { get; } = [];

        public HashSet<(string Type, string Attribute)> Many { get; } = [];

        public Dictionary<(string Type, string Attribute), Regex> MentionPatterns { get; } = [];

        public HashSet<string> Unreadable { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, List<KeyValuePair<string, string>>> InitialMembers { get; } = new(StringComparer.Ordinal);
    }
}
