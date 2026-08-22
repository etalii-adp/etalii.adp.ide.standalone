using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Diagram;

namespace EtAlii.Adp.Backend.Hierarchy;

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
    public static IReadOnlyList<ContextOptionNode> Build(IReadOnlyList<DiagramDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        return definitions
            .GroupBy(definition => definition.Origin.Vendor, StringComparer.Ordinal)
            .OrderBy(vendor => vendor.Key, StringComparer.Ordinal)
            .Select(vendor => new ContextOptionNode(
                Id: vendor.Key,
                Label: vendor.Key,
                Selectable: false,
                Children: BuildVendor(vendor)))
            .ToArray();
    }

    private static IReadOnlyList<ContextOptionNode> BuildVendor(IEnumerable<DiagramDefinition> definitions)
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
                .Select(definition => new ContextOptionNode(definition.Origin.Key, definition.Title, Selectable: true))
                .ToArray();

            if (plain is not null)
            {
                // The type itself is a choice; any subtypes hang off it.
                nodes.Add(new ContextOptionNode(
                    plain.Origin.Key,
                    plain.Title,
                    Selectable: true,
                    Children: subtypes.Length == 0 ? null : subtypes));
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
}
