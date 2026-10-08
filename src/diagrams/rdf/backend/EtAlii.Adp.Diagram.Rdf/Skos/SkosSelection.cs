namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// What the family's element ids mean under the scheme reading - built on
/// <see cref="RdfSelection"/>'s shapes, never re-parsing them, and answering from the file's own
/// assertions: an element is a concept because the file types it so, which is what lets the one
/// family provider trio serve every reading without knowing which registration a selection came
/// through (skos-diagram Requirement 6, the data-driven half of the design's delegation).
/// </summary>
internal static class SkosSelection
{
    /// <summary>The IRI of an element the file asserts to be a <c>skos:Concept</c>, or null.</summary>
    public static string? ConceptOf(RdfDocumentEntry entry, string? elementId)
    {
        var iri = RdfSelection.ResourceOf(entry, elementId);
        return iri is not null && IsTyped(entry, iri, SkosVocabulary.Concept) ? iri : null;
    }

    /// <summary>The IRI of an element the file asserts to be a scheme or collection, or null.</summary>
    public static string? SchemeOrCollectionOf(RdfDocumentEntry entry, string? elementId)
    {
        var iri = RdfSelection.ResourceOf(entry, elementId);
        return iri is not null
            && (IsTyped(entry, iri, SkosVocabulary.ConceptScheme)
                || IsTyped(entry, iri, SkosVocabulary.Collection)
                || IsTyped(entry, iri, SkosVocabulary.OrderedCollection))
            ? iri
            : null;
    }

    /// <summary>Whether the file asserts any concept scheme - what makes the concept toolbox entry apply.</summary>
    public static bool HasScheme(RdfDocumentEntry entry) =>
        entry.Model.Triples.Any(t =>
            t is { Predicate.Iri: RdfVocabulary.Type, Object: IriTerm { Iri: SkosVocabulary.ConceptScheme } });

    /// <summary>The first asserted scheme IRI, document order - where a dropped concept is filed.</summary>
    public static string? FirstScheme(RdfDocumentEntry entry) =>
        entry.Model.Triples
            .Where(t => t is { Predicate.Iri: RdfVocabulary.Type, Object: IriTerm { Iri: SkosVocabulary.ConceptScheme } })
            .Select(t => t.Subject)
            .OfType<IriTerm>()
            .Select(s => s.Iri)
            .FirstOrDefault();

    /// <summary>
    /// The hierarchy or related edge a skos edge id names, when the drawn pair still has
    /// asserted triples - resolved by values in either direction, because the canonical id names
    /// the broader-direction triple whichever direction the file states.
    /// </summary>
    public static (string AIri, string BIri, SkosEdgeKind Kind, IReadOnlyList<RdfTriple> Triples)? PairOf(
        RdfDocumentEntry entry, string? elementId)
    {
        if (elementId is null || !elementId.StartsWith("edge:", StringComparison.Ordinal))
        {
            return null;
        }

        var parts = elementId["edge:".Length..].Split('|');
        if (parts.Length != 3
            || !parts[0].StartsWith("res:", StringComparison.Ordinal)
            || !parts[2].StartsWith("res:", StringComparison.Ordinal))
        {
            return null;
        }

        var a = parts[0]["res:".Length..];
        var b = parts[2]["res:".Length..];
        var kind = parts[1] switch
        {
            SkosVocabulary.Broader => SkosEdgeKind.Hierarchy,
            SkosVocabulary.Related => SkosEdgeKind.Related,
            _ => (SkosEdgeKind?)null,
        } ?? default;
        if (parts[1] != SkosVocabulary.Broader && parts[1] != SkosVocabulary.Related)
        {
            return null;
        }

        var triples = kind == SkosEdgeKind.Hierarchy
            ? HierarchyTriplesBetween(entry, a, b)
            : RelatedTriplesBetween(entry, a, b);
        return triples.Count == 0 ? null : (a, b, kind, triples);
    }

    /// <summary>Every asserted triple stating the hierarchy pair, in either direction. For the canonical id, <paramref name="narrowerIri"/> is the id's first IRI.</summary>
    private static IReadOnlyList<RdfTriple> HierarchyTriplesBetween(RdfDocumentEntry entry, string narrowerIri, string broaderIri) =>
        entry.Model.Triples
            .Where(t =>
                (Matches(t, narrowerIri, SkosVocabulary.Broader, broaderIri))
                || (Matches(t, broaderIri, SkosVocabulary.Narrower, narrowerIri)))
            .ToList();

    /// <summary>Every asserted triple stating the related pair, in either direction.</summary>
    private static IReadOnlyList<RdfTriple> RelatedTriplesBetween(RdfDocumentEntry entry, string aIri, string bIri) =>
        entry.Model.Triples
            .Where(t =>
                Matches(t, aIri, SkosVocabulary.Related, bIri)
                || Matches(t, bIri, SkosVocabulary.Related, aIri))
            .ToList();

    /// <summary>Whether the concept's labels are stated through SKOS-XL - the edit-refusal case of Requirement 3.6.</summary>
    public static bool HasXlLabels(RdfDocumentEntry entry, string iri) =>
        entry.Model.Triples.Any(t =>
            t.Subject is IriTerm s && s.Iri == iri && SkosVocabulary.XlLabelProperties.Contains(t.Predicate.Iri));

    private static bool Matches(RdfTriple triple, string subjectIri, string predicateIri, string objectIri) =>
        triple.Subject is IriTerm s && s.Iri == subjectIri
        && triple.Predicate.Iri == predicateIri
        && triple.Object is IriTerm o && o.Iri == objectIri;

    private static bool IsTyped(RdfDocumentEntry entry, string iri, string typeIri) =>
        entry.Model.Triples.Any(t =>
            t.Subject is IriTerm s && s.Iri == iri
            && t.Predicate.Iri == RdfVocabulary.Type
            && t.Object is IriTerm o && o.Iri == typeIri);
}
