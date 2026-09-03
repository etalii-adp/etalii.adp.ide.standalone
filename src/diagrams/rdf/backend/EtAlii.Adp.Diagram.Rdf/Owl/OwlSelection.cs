namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// The ontology reading's additions to the family selection vocabulary (the anchor's task 3.3
/// seam, extended rather than reinvented): <c>ind:{iri}</c> for a punned individual role,
/// <c>expr:</c> structural ids, and the derived edges - a property edge between a domain and a
/// range does not correspond to one triple, so <see cref="RdfSelection"/> alone cannot name it.
/// Parsed here alone, per the seam's own rule.
/// </summary>
internal static class OwlSelection
{
    /// <summary>The blank-node identity boundary's sentence, answered wherever an expression refuses an edit (Requirement 3.2).</summary>
    public const string ExpressionRefusal =
        "That is an anonymous class expression, whose identity does not survive an edit to the file, so nothing about it can be edited from the diagram. Edit the expression as text.";

    /// <summary>The duplicate-subclass sentence, answered before any splice (Requirement 6.1).</summary>
    public const string DuplicateSubclassRefusal =
        "That subclass relation is already asserted; drawing it again would state nothing new.";

    /// <summary>The IRI an <c>ind:</c> id names, when the model actually states it; null otherwise.</summary>
    public static string? IndividualIriOf(RdfDocumentEntry entry, string? elementId)
    {
        if (elementId is null || !elementId.StartsWith("ind:", StringComparison.Ordinal))
        {
            return null;
        }

        var iri = elementId["ind:".Length..];
        return entry.Model.Triples.Any(t => t.Subject is IriTerm s && s.Iri == iri) ? iri : null;
    }

    /// <summary>Whether the id names an expression node - selectable, describable, edit-refused.</summary>
    public static bool IsExpression(string? elementId) =>
        elementId is not null && elementId.StartsWith("expr:", StringComparison.Ordinal);

    /// <summary>
    /// The drawn ontology node or edge an id names, or null - the projection is the authority,
    /// because a derived edge exists only there.
    /// </summary>
    public static string? Describe(RdfDocumentEntry entry, string? elementId)
    {
        if (elementId is null || !entry.IsUsable)
        {
            return null;
        }

        var isOwlShaped = IsExpression(elementId)
            || elementId.StartsWith("ind:", StringComparison.Ordinal)
            || elementId.StartsWith("thing:", StringComparison.Ordinal)
            || elementId.StartsWith("dt:", StringComparison.Ordinal)
            || elementId.StartsWith("edge:", StringComparison.Ordinal);
        if (!isOwlShaped)
        {
            return null;
        }

        var graph = OwlProjection.Project(entry.Model);
        if (graph.Nodes.FirstOrDefault(n => n.Id == elementId) is { } node)
        {
            return node.Display;
        }

        if (graph.Edges.FirstOrDefault(e => e.Id == elementId) is { } edge)
        {
            var from = graph.Nodes.First(n => n.Id == edge.FromId).Display;
            var to = graph.Nodes.First(n => n.Id == edge.ToId).Display;
            return $"{from} → {to}";
        }

        return null;
    }

    /// <summary>
    /// The expression node an <c>expr:</c> id names, for the grid's uncapped rendering; null
    /// when the id is not (or no longer) in the projection.
    /// </summary>
    public static OwlNode? ExpressionOf(RdfDocumentEntry entry, string? elementId)
    {
        if (!IsExpression(elementId) || !entry.IsUsable)
        {
            return null;
        }

        return OwlProjection.Project(entry.Model).Nodes
            .FirstOrDefault(node => node.Id == elementId && node.ExpressionRoot is not null);
    }

    /// <summary>
    /// The property IRI a derived property edge names - <c>edge:{from}|{propertyIri}|{to}</c>
    /// where the middle part is a declared property with no from-predicate-to triple of its own.
    /// </summary>
    public static string? PropertyEdgeIriOf(RdfDocumentEntry entry, string? elementId)
    {
        if (elementId is null || !elementId.StartsWith("edge:", StringComparison.Ordinal))
        {
            return null;
        }

        var parts = elementId["edge:".Length..].Split('|');
        if (parts.Length < 3)
        {
            return null;
        }

        var candidate = parts[1];
        var declared = entry.Model.Triples.Any(t =>
            t.Subject is IriTerm s && s.Iri == candidate
            && t.Predicate.Iri == RdfVocabulary.Type
            && t.Object is IriTerm { } type
            && type.Iri is OwlVocabulary.ObjectProperty or OwlVocabulary.DatatypeProperty);
        return declared ? candidate : null;
    }

    /// <summary>Whether the IRI is used as a class in this model - what the subclass gesture asks of both ends.</summary>
    public static bool IsClass(RdfDocumentEntry entry, string iri) =>
        entry.Model.Triples.Any(t =>
            (t.Subject is IriTerm s && s.Iri == iri
                && ((t.Predicate.Iri == RdfVocabulary.Type && t.Object is IriTerm { Iri: OwlVocabulary.Class or OwlVocabulary.RdfsClass })
                    || t.Predicate.Iri == OwlVocabulary.SubClassOf))
            || (t.Object is IriTerm o && o.Iri == iri
                && t.Predicate.Iri is OwlVocabulary.SubClassOf or OwlVocabulary.Domain or OwlVocabulary.Range));

    /// <summary>Whether this document carries the ontology marker - what gates the OWL placement entries.</summary>
    public static bool IsOntologyDocument(RdfDocumentEntry entry) =>
        entry.Model.Triples.Any(t =>
            t.Predicate.Iri == RdfVocabulary.Type && t.Object is IriTerm { Iri: OwlVocabulary.Ontology });
}
