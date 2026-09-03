namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// One node of a triple. A closed set, deliberately: RDF terms are IRIs, blank nodes or literals
/// and nothing else, so the projections and writers switch over exactly three cases.
/// </summary>
public abstract record RdfTerm;
