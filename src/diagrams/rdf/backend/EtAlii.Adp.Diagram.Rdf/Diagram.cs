namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// The RDF family's anchor type, cataloged in docs/diagrams.md as <c>w3c/rdf</c>. The sibling
/// readings - ontology, scheme, shapes - join this array as their specifications land, sharing
/// the store, the parser and the writer, and differing only in what each projects.
/// </summary>
/// <remarks>
/// <c>.ttl</c> and <c>.nt</c> both belong to this family the way <c>.tml</c> belongs to the
/// timeline, so a bare file of either routes here on sight - the alternate-extension seam
/// carries <c>.nt</c>. An alternate derives no registration sibling, so a registered
/// <c>.nt</c> names its body with an explicit <c>body:</c> header, which Add writes anyway.
/// </remarks>
public static class Diagram
{
    /// <summary>The data graph: what an RDF file states, read straight from its own serialization.</summary>
    public static DiagramDefinition Rdf { get; } = new(
        ServiceCollectionAddRdfExtension.RdfOrigin,
        "RDF Graph",
        "What an RDF file states: its resources, their relationships and their values, straight from Turtle or N-Triples.",
        Icon: "mdi-graph-outline",
        Extension: ".ttl",
        AlternateExtension: ".nt",
        // The family's engine registers once; the siblings joining later add their readings.
        Build: builder => builder.Services.AddRdf());

    /// <summary>
    /// The ontology reading of the same bytes: asserted OWL 2, drawn in the VOWL vocabulary.
    /// Never routed from a bare file - the anchor keeps that - and Add suggests it exactly on
    /// the files that carry the <c>owl:Ontology</c> marker (the routing arrangement).
    /// </summary>
    public static DiagramDefinition Owl { get; } = new(
        ServiceCollectionAddRdfExtension.OwlOrigin,
        "OWL Ontology",
        "An OWL 2 ontology's classes, hierarchy, properties and restrictions - what the file asserts, not what a reasoner would infer.",
        Icon: "mdi-shape-outline",
        Extension: ".ttl",
        AlternateExtension: ".nt",
        SharedExtension: true,
        // A textual marker test, deliberately: it judges whether the reading is worth offering,
        // not whether the file is valid OWL - the parse decides that once the reading opens.
        SuggestsBody: text => text.Contains("owl:Ontology", StringComparison.Ordinal)
            || text.Contains(OwlVocabulary.Ontology, StringComparison.Ordinal),
        // The same family Build as the anchor's; AddRdf registers once and no-ops after.
        Build: builder => builder.Services.AddRdf());

    /// <summary>What discovery reads: the anchor and its readings, the family as it grows.</summary>
    public static DiagramDefinition[] Definitions { get; } =
    [
        Rdf,
        Owl,
    ];
}
