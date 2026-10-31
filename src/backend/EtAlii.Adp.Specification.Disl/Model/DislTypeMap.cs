namespace EtAlii.Adp.Specification.Disl;

/// <summary>How one binding type becomes part of the model: the metamodel type it is read as, its attributes renamed, and those kept for the host.</summary>
/// <param name="As">The metamodel type, or <c>header</c>, <c>diagram</c> or <c>unreadable</c>.</param>
/// <param name="Attributes">The binding's attribute names renamed to the metamodel's.</param>
/// <param name="HostAttributes">The binding's attributes that are the host's, not the model's.</param>
internal sealed record DislTypeMapping(string As, IReadOnlyDictionary<string, string> Attributes, IReadOnlySet<string> HostAttributes);

/// <summary>
/// The specification's <c>persistence.typeMap</c> (DISL 0.3), or the identity mapping, read at load by
/// <see cref="DislTypeMapReader"/>: consulted from the binding to the model by <see cref="DislModelBuilder"/>, and back
/// by <see cref="DislWrite"/>.
/// </summary>
internal sealed class DislTypeMap(IReadOnlyDictionary<string, DislTypeMapping> mappings)
{
    private static readonly IReadOnlyDictionary<string, string> NoRenames = new Dictionary<string, string>();

    /// <summary>The mapping of binding type <paramref name="type"/>.</summary>
    public DislTypeMapping For(string type) => mappings.GetValueOrDefault(type) ?? new DislTypeMapping(type, NoRenames, new HashSet<string>());

    /// <summary>The binding type mapped as <c>diagram</c> whose <c>attributes</c> name diagram attribute <paramref name="attribute"/>; null for none.</summary>
    public string? DiagramTypeHolding(string attribute) =>
        mappings.FirstOrDefault(mapping => mapping.Value.As == "diagram" && mapping.Value.Attributes.Values.Contains(attribute, StringComparer.Ordinal)).Key;

    /// <summary>The binding types mapped as <c>diagram</c> that the type map lists before <paramref name="type"/>.</summary>
    public IReadOnlyList<string> DiagramTypesBefore(string type) =>
        [.. mappings.TakeWhile(mapping => mapping.Key != type).Where(mapping => mapping.Value.As == "diagram").Select(mapping => mapping.Key)];

    /// <summary>The binding type metamodel type <paramref name="type"/> is written as: the first mapped to it, else its own name.</summary>
    public string BindingTypeOf(string type) => mappings.FirstOrDefault(mapping => mapping.Value.As == type).Key ?? type;

    /// <summary>The binding's name for <paramref name="attribute"/> of metamodel type <paramref name="type"/>.</summary>
    public string BindingAttributeOf(string type, string attribute) =>
        For(BindingTypeOf(type)).Attributes.FirstOrDefault(rename => rename.Value == attribute).Key ?? attribute;
}
