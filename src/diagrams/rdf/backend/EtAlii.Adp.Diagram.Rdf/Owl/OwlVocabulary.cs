namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>The OWL 2 and RDFS IRIs the ontology reading speaks of by name.</summary>
public static class OwlVocabulary
{
    public const string Class = "http://www.w3.org/2002/07/owl#Class";
    public const string RdfsClass = "http://www.w3.org/2000/01/rdf-schema#Class";
    public const string ObjectProperty = "http://www.w3.org/2002/07/owl#ObjectProperty";
    public const string DatatypeProperty = "http://www.w3.org/2002/07/owl#DatatypeProperty";
    public const string AnnotationProperty = "http://www.w3.org/2002/07/owl#AnnotationProperty";
    public const string NamedIndividual = "http://www.w3.org/2002/07/owl#NamedIndividual";
    public const string Ontology = "http://www.w3.org/2002/07/owl#Ontology";
    public const string Thing = "http://www.w3.org/2002/07/owl#Thing";
    public const string Nothing = "http://www.w3.org/2002/07/owl#Nothing";
    public const string RdfsDatatype = "http://www.w3.org/2000/01/rdf-schema#Datatype";
    public const string RdfsLiteral = "http://www.w3.org/2000/01/rdf-schema#Literal";

    public const string SubClassOf = "http://www.w3.org/2000/01/rdf-schema#subClassOf";
    public const string Domain = "http://www.w3.org/2000/01/rdf-schema#domain";
    public const string Range = "http://www.w3.org/2000/01/rdf-schema#range";
    public const string EquivalentClass = "http://www.w3.org/2002/07/owl#equivalentClass";
    public const string DisjointWith = "http://www.w3.org/2002/07/owl#disjointWith";
    public const string AllDisjointClasses = "http://www.w3.org/2002/07/owl#AllDisjointClasses";
    public const string Members = "http://www.w3.org/2002/07/owl#members";
    public const string InverseOf = "http://www.w3.org/2002/07/owl#inverseOf";

    public const string FunctionalProperty = "http://www.w3.org/2002/07/owl#FunctionalProperty";
    public const string InverseFunctionalProperty = "http://www.w3.org/2002/07/owl#InverseFunctionalProperty";
    public const string TransitiveProperty = "http://www.w3.org/2002/07/owl#TransitiveProperty";
    public const string SymmetricProperty = "http://www.w3.org/2002/07/owl#SymmetricProperty";
    public const string AsymmetricProperty = "http://www.w3.org/2002/07/owl#AsymmetricProperty";
    public const string ReflexiveProperty = "http://www.w3.org/2002/07/owl#ReflexiveProperty";
    public const string IrreflexiveProperty = "http://www.w3.org/2002/07/owl#IrreflexiveProperty";

    public const string OnProperty = "http://www.w3.org/2002/07/owl#onProperty";
    public const string SomeValuesFrom = "http://www.w3.org/2002/07/owl#someValuesFrom";
    public const string AllValuesFrom = "http://www.w3.org/2002/07/owl#allValuesFrom";
    public const string HasValue = "http://www.w3.org/2002/07/owl#hasValue";
    public const string Cardinality = "http://www.w3.org/2002/07/owl#cardinality";
    public const string MinCardinality = "http://www.w3.org/2002/07/owl#minCardinality";
    public const string MaxCardinality = "http://www.w3.org/2002/07/owl#maxCardinality";
    public const string QualifiedCardinality = "http://www.w3.org/2002/07/owl#qualifiedCardinality";
    public const string MinQualifiedCardinality = "http://www.w3.org/2002/07/owl#minQualifiedCardinality";
    public const string MaxQualifiedCardinality = "http://www.w3.org/2002/07/owl#maxQualifiedCardinality";
    public const string OnClass = "http://www.w3.org/2002/07/owl#onClass";
    public const string OnDataRange = "http://www.w3.org/2002/07/owl#onDataRange";

    public const string UnionOf = "http://www.w3.org/2002/07/owl#unionOf";
    public const string IntersectionOf = "http://www.w3.org/2002/07/owl#intersectionOf";
    public const string ComplementOf = "http://www.w3.org/2002/07/owl#complementOf";
    public const string OneOf = "http://www.w3.org/2002/07/owl#oneOf";

    public const string Deprecated = "http://www.w3.org/2002/07/owl#deprecated";
    public const string Imports = "http://www.w3.org/2002/07/owl#imports";

    public const string XsdNamespace = "http://www.w3.org/2001/XMLSchema#";

    /// <summary>The namespaces whose terms are vocabulary rather than this ontology's own entities.</summary>
    public static bool IsBuiltIn(string iri) =>
        iri.StartsWith("http://www.w3.org/2002/07/owl#", StringComparison.Ordinal)
        || iri.StartsWith("http://www.w3.org/2000/01/rdf-schema#", StringComparison.Ordinal)
        || iri.StartsWith("http://www.w3.org/1999/02/22-rdf-syntax-ns#", StringComparison.Ordinal)
        || iri.StartsWith(XsdNamespace, StringComparison.Ordinal);
}
