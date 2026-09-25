using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// Shapes the discovered diagram types into the option tree the Add dialog shows: one group
/// per vendor, each diagram type under its vendor by title, and a subtype nested under the
/// type it refines.
/// </summary>
/// <remarks>
/// The origin's <c>Type</c> is deliberately not a grouping level - it *is* the diagram type,
/// so grouping by it would put every title in a group of one. An option's id is the origin
/// key (<c>vendor/type[/subtype]</c>): unique per discovered type, and what the commit step
/// maps straight back to a definition. The client carries nothing else.
/// </remarks>
public static class DiagramOptionTree
{
    /// <param name="definitions">The discovered diagram types.</param>
    /// <param name="annotate">
    /// Optionally, the per-option values a caller decides while the subject is still known -
    /// computed here so that choosing a type costs no round trip. Groups never receive it: a
    /// heading is not something that can be created, so none of the three applies.
    /// <para>
    /// It takes the <see cref="DiagramDefinition"/> rather than its <c>Origin</c> because the
    /// answers depend on the definition itself - whether the type registers a folder is a
    /// property of the definition, and the origin alone cannot be asked.
    /// </para>
    /// </param>
    public static IReadOnlyList<ContextOptionNode> Build(
        IReadOnlyList<DiagramDefinition> definitions,
        Func<DiagramDefinition, ContextOptionAnnotations>? annotate = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        return definitions
            .GroupBy(definition => definition.Origin.Vendor, StringComparer.Ordinal)
            .OrderBy(vendor => vendor.Key, StringComparer.Ordinal)
            .Select(vendor => new ContextOptionNode(
                Id: vendor.Key,
                Label: vendor.Key,
                Selectable: false,
                Children: BuildVendor(vendor, annotate)))
            .ToArray();
    }

    private static IReadOnlyList<ContextOptionNode> BuildVendor(
        IEnumerable<DiagramDefinition> definitions,
        Func<DiagramDefinition, ContextOptionAnnotations>? annotate)
    {
        var byType = definitions
            .GroupBy(definition => definition.Origin.Type, StringComparer.Ordinal)
            .OrderBy(type => type.Key, StringComparer.Ordinal);

        var nodes = new List<ContextOptionNode>();
        foreach (var type in byType)
        {
            var plain = type.FirstOrDefault(definition => definition.Origin.Subtype.Length == 0);
            var subtypes = type
                .Where(definition => definition.Origin.Subtype.Length > 0)
                .OrderBy(definition => definition.Title, StringComparer.Ordinal)
                .Select(definition => Leaf(definition, annotate))
                .ToArray();

            if (plain is not null)
            {
                // The type itself is a choice; any subtypes hang off it.
                nodes.Add(Leaf(plain, annotate) with
                {
                    Children = subtypes.Length == 0 ? null : subtypes,
                });
            }
            else
            {
                // Only subtypes exist, so there is no definition to be the parent: synthesise a
                // non-selectable one labelled by the type, so the subtypes still have a home.
                nodes.Add(new ContextOptionNode(
                    $"{type.First().Origin.Vendor}/{type.Key}",
                    type.Key,
                    Selectable: false,
                    Children: subtypes));
            }
        }

        return nodes
            .OrderBy(node => node.Label, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// One choosable diagram type, with whatever the caller has to say about it. Both leaf
    /// shapes - a subtype and a type that is itself a choice - go through here, so the three
    /// annotations cannot end up applied to one and forgotten on the other.
    /// </summary>
    private static ContextOptionNode Leaf(
        DiagramDefinition definition,
        Func<DiagramDefinition, ContextOptionAnnotations>? annotate)
    {
        var annotations = annotate is null ? ContextOptionAnnotations.None : annotate(definition);
        return new ContextOptionNode(
            definition.Origin.Key,
            definition.Title,
            Selectable: true,
            SuggestedValue: annotations.SuggestedValue,
            Description: definition.Description,
            Icon: definition.Icon,
            NameSuppressedReason: annotations.NameSuppressedReason,
            UnavailableReason: annotations.UnavailableReason);
    }
}
