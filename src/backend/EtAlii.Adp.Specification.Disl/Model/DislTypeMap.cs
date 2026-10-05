using System.Text.Json;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>How one binding type becomes part of the model: the metamodel type it is read as, its attributes renamed, and those kept for the host.</summary>
/// <param name="As">The metamodel type, or <c>header</c>, <c>diagram</c> or <c>unreadable</c>.</param>
/// <param name="Attributes">The binding's attribute names renamed to the metamodel's.</param>
/// <param name="HostAttributes">The binding's attributes that are the host's, not the model's.</param>
internal sealed record DislTypeMapping(string As, IReadOnlyDictionary<string, string> Attributes, IReadOnlySet<string> HostAttributes);

/// <summary>
/// The specification's <c>persistence.x-persistence.typeMap</c>, or the identity mapping: read from
/// the binding to the model by <see cref="DislModelBuilder"/>, and back by <see cref="DislWrite"/>.
/// </summary>
internal sealed class DislTypeMap(IReadOnlyDictionary<string, DislTypeMapping> mappings)
{
    private static readonly IReadOnlyDictionary<string, string> NoRenames = new Dictionary<string, string>();

    public static DislTypeMap Of(DislSpecification specification)
    {
        var mappings = new Dictionary<string, DislTypeMapping>(StringComparer.Ordinal);
        if (specification.Root.TryGetProperty("persistence", out var persistence) && persistence.TryGetProperty("x-persistence.typeMap", out var map) && map.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in map.EnumerateObject())
            {
                var renames = DislJson.Members(entry.Value, "attributes")
                    .Where(member => member.Value.ValueKind == JsonValueKind.String)
                    .ToDictionary(member => member.Name, member => member.Value.GetString()!, StringComparer.Ordinal);
                mappings[entry.Name] = new DislTypeMapping(
                    DislJson.String(entry.Value, "as") ?? entry.Name,
                    renames,
                    DislJson.Strings(entry.Value, "hostAttributes").ToHashSet(StringComparer.Ordinal));
            }
        }
        return new DislTypeMap(mappings);
    }

    /// <summary>The mapping of binding type <paramref name="type"/>.</summary>
    public DislTypeMapping For(string type) => mappings.GetValueOrDefault(type) ?? new DislTypeMapping(type, NoRenames, new HashSet<string>());

    /// <summary>The binding type metamodel type <paramref name="type"/> is written as: the first mapped to it, else its own name.</summary>
    public string BindingTypeOf(string type) => mappings.FirstOrDefault(mapping => mapping.Value.As == type).Key ?? type;

    /// <summary>The binding's name for <paramref name="attribute"/> of metamodel type <paramref name="type"/>.</summary>
    public string BindingAttributeOf(string type, string attribute) =>
        For(BindingTypeOf(type)).Attributes.FirstOrDefault(rename => rename.Value == attribute).Key ?? attribute;
}
