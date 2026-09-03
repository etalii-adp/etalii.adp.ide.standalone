namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>
/// One discovered shape: the term that names it, where the document first makes it a shape, and
/// the two facts every consumer branches on - whether it is a property shape (it has a
/// <c>sh:path</c>, the recommendation's own distinction) and whether it targets itself as a class
/// (the implicit class target).
/// </summary>
/// <param name="Term">The shape's term: an IRI or a blank node, never a literal.</param>
/// <param name="Key">The identity key: <c>i:{full-iri}</c> or <c>b:{ordinal}</c> - IRI spelling variants collapse, blank ordinals hold for this parse only.</param>
/// <param name="FirstTripleIndex">Index in <see cref="RdfModel.Triples"/> of the triple that first makes this a shape - the deterministic ordering every projection consumes.</param>
/// <param name="FirstSpan">That triple's span, for anything that reasons about lines.</param>
/// <param name="IsPropertyShape">Whether the shape is the subject of a <c>sh:path</c> triple.</param>
/// <param name="ImplicitClassTarget">Whether the shape is also a class as this file states it - typed <c>rdfs:Class</c>, or typed with a class the file's own <c>rdfs:subClassOf</c> triples reach <c>rdfs:Class</c> from - and so targets its own instances.</param>
public sealed record ShaclShape(
    RdfTerm Term,
    string Key,
    int FirstTripleIndex,
    SourceSpan FirstSpan,
    bool IsPropertyShape,
    bool ImplicitClassTarget);
