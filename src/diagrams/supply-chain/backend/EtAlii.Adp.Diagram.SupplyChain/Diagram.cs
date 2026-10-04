using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `etalii/supply-chain`.</summary>
public static class Diagram
{
    /// <summary>The body of a supply chain diagram lives in a `.supply` file, ADP's own schema.</summary>
    public const string DocumentExtension = ".supply";

    /// <summary>Whether <paramref name="path"/> is a body this module owns.</summary>
    public static bool IsBody(string path) =>
        path is { Length: > 0 } &&
        path.EndsWith(DocumentExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>The supply chain diagram.</summary>
    public static DiagramDefinition SupplyChain { get; } = new(
        new DiagramOrigin("etalii", "supply-chain"),
        "Supply chain diagram",
        "Where goods come from and where they go: mines, suppliers, plants, distributors and consumers in their regions, the flows between them with their volumes, and the whole chain up and down from whatever is selected.",
        Icon: "mdi-truck-delivery-outline",
        Extension: DocumentExtension,
        // A notation defined by this repository's user, so ADP owns both the schema and the
        // etalii/ origin. Positions are computed by a layered layout unless a node states its own.
        Build: builder => builder.Services.AddSupplyChain());

    /// <summary>What discovery reads. One entry: this module carries one notation.</summary>
    public static DiagramDefinition[] Definitions { get; } = [SupplyChain];
}
