using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// The SPARQL query diagram, cataloged in docs/diagrams.md as <c>w3c/sparql</c>.
/// </summary>
/// <remarks>
/// Deliberately a sibling of the RDF family rather than a member: a <c>.rq</c> file is a
/// question, not a serialization of a graph, so this module shares no engine with
/// <c>w3c/rdf</c> - its own parser, its own model, its own projection. <c>.rq</c> means SPARQL
/// everywhere, so a bare query file routes here on sight; no alternate extension and no shared
/// stance, because nothing else claims it.
/// </remarks>
public static class Diagram
{
    /// <summary>The query: what a <c>.rq</c> file asks, drawn as the joins it is made of.</summary>
    public static DiagramDefinition Sparql { get; } = new(
        ServiceCollectionAddSparqlExtension.SparqlOrigin,
        "SPARQL Query",
        "What a query asks: its graph pattern as nodes and edges, its variables as the joins they are, and its form and modifiers as the frame around them.",
        Icon: "mdi-help-network-outline",
        Extension: ".rq",
        Build: builder => builder.Services.AddSparql());

    /// <summary>What discovery reads.</summary>
    public static DiagramDefinition[] Definitions { get; } =
    [
        Sparql,
    ];
}
