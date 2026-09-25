using EtAlii.Adp.Diagram.Rdf.Shacl;
using EtAlii.Adp.Documents;

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

    /// <summary>
    /// The scheme reading (skos-diagram Requirement 2): a thesaurus over the same bytes. It
    /// never claims a bare body - the shared-extension stance - so a `.ttl`/`.nt` becomes a
    /// scheme diagram only through Add, where the choice tree offers it for the family's
    /// extensions; a file asserting a <c>skos:ConceptScheme</c> is where that choice belongs.
    /// </summary>
    public static DiagramDefinition Skos { get; } = new(
        ServiceCollectionAddSkosExtension.SkosOrigin,
        "SKOS Concept Scheme",
        "A thesaurus or controlled vocabulary: concepts under their schemes, broader and narrower drawn as a hierarchy, related links across it.",
        Icon: "mdi-file-tree",
        Extension: ".ttl",
        AlternateExtension: ".nt",
        SharedExtension: true,
        // The marker triple Requirement 2.3 names, tested textually rather than by parsing: it
        // judges whether the reading is worth offering, not whether the file is good SKOS - the
        // parse decides that once the reading opens. A file with concepts but no scheme stays
        // registrable by explicit choice, which is what the choice tree is for.
        SuggestsBody: text => text.Contains("skos:ConceptScheme", StringComparison.Ordinal)
            || text.Contains(SkosVocabulary.ConceptScheme, StringComparison.Ordinal),
        // The same family Build as the anchor's; AddRdf registers once and no-ops after.
        Build: builder => builder.Services.AddRdf());

    /// <summary>
    /// The shapes reading (shacl-diagram Requirement 2): the constraints a file states over data
    /// that lives somewhere else. Like its siblings it never claims a bare body - the anchor
    /// keeps that - so a <c>.ttl</c>/<c>.nt</c> becomes a shapes diagram only through Add, where
    /// the choice tree offers it and the marker below says when it is worth offering.
    /// </summary>
    public static DiagramDefinition Shacl { get; } = new(
        ServiceCollectionAddShaclExtension.ShaclOrigin,
        "SHACL Shapes",
        "The constraints a shapes graph states: node shapes as cards, their property constraints as rows, and what each one targets - drawn, never executed.",
        Icon: "mdi-check-decagram-outline",
        Extension: ".ttl",
        AlternateExtension: ".nt",
        SharedExtension: true,
        // A textual marker test, like the ontology reading's: it judges whether the reading is
        // worth offering, not whether the file is valid SHACL - the parse decides that once the
        // reading opens. sh:NodeShape is what the recommendation's own examples lead with, and
        // sh:property catches a shapes file whose shapes are all shapes by use.
        SuggestsBody: text => text.Contains("sh:NodeShape", StringComparison.Ordinal)
            || text.Contains("sh:property", StringComparison.Ordinal)
            || text.Contains(ShaclVocabulary.NodeShape, StringComparison.Ordinal),
        // The same family Build as the anchor's; AddRdf registers once and no-ops after.
        Build: builder => builder.Services.AddRdf());

    /// <summary>What discovery reads: the anchor and its readings, the family as it grows.</summary>
    public static DiagramDefinition[] Definitions { get; } =
    [
        Rdf,
        Owl,
        Skos,
        Shacl,
    ];
}
