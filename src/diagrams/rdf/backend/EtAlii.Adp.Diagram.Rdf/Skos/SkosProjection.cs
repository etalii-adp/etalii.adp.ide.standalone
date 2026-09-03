namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// The scheme reading: what a model states as a vocabulary. A pure function over the anchor's
/// triples - no parsing, no completion, nothing written anywhere (skos-diagram Requirement 1).
/// </summary>
/// <remarks>
/// <para>
/// The positions taken, in one place: everything is by assertion. A concept is whatever the file
/// types as <c>skos:Concept</c>; membership is <c>inScheme</c>/<c>topConceptOf</c>/
/// <c>hasTopConcept</c> as written, and a concept asserted into no scheme goes to the unfiled
/// band however its hierarchy hangs. One hierarchy edge exists per pair wherever
/// <c>broader</c> or <c>narrower</c> is asserted in either direction - the projection of the
/// asserted triples, never their entailment: the inverse is not synthesized, not handed to the
/// store, and never written to the file.
/// </para>
/// <para>
/// The budget cut is hierarchy-aware (Requirement 8.2): schemes by IRI, then each scheme's top
/// concepts and their descendants breadth-first by layer with ordinal ties, then a scheme's
/// unreached members, then unfiled concepts, then collections - so a truncated view is the top
/// of the vocabulary, not a triple-order slice.
/// </para>
/// </remarks>
public static class SkosProjection
{
    /// <summary>What <paramref name="model"/> states, cut to <paramref name="budget"/> drawn elements.</summary>
    public static SkosProjectionResult Project(RdfModel model, int budget = RdfProjection.DefaultBudget)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentOutOfRangeException.ThrowIfLessThan(budget, 1);

        var conceptIds = TypedIds(model, SkosVocabulary.Concept);
        var schemeIds = TypedIds(model, SkosVocabulary.ConceptScheme);
        var orderedCollectionIds = TypedIds(model, SkosVocabulary.OrderedCollection);
        var collectionIds = TypedIds(model, SkosVocabulary.Collection).Concat(orderedCollectionIds)
            .Distinct(StringComparer.Ordinal).ToList();

        var labels = new Dictionary<string, List<SkosLabel>>(StringComparer.Ordinal);
        var notations = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var memberships = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var topsByScheme = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var hierarchyPairs = new Dictionary<(string BroaderId, string NarrowerId), (List<RdfTriple> Triples, bool Both)>();
        var relatedPairs = new Dictionary<(string A, string B), (List<RdfTriple> Triples, bool Both)>();
        var mappingEdges = new List<SkosEdge>();
        var outOfFileMappings = new List<RdfTriple>();
        var membersByCollection = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var (firsts, rests) = ConsCells(model);

        foreach (var triple in model.Triples)
        {
            var subjectId = ElementId(triple.Subject);
            var objectId = ElementId(triple.Object);
            switch (triple.Predicate.Iri)
            {
                case SkosVocabulary.PrefLabel or SkosVocabulary.AltLabel or SkosVocabulary.HiddenLabel
                    when subjectId is not null && triple.Object is LiteralTerm literal:
                    Add(labels, subjectId, new SkosLabel(
                        triple.Predicate.Iri switch
                        {
                            SkosVocabulary.PrefLabel => SkosLabelSource.Preferred,
                            SkosVocabulary.AltLabel => SkosLabelSource.Alternate,
                            _ => SkosLabelSource.Hidden,
                        },
                        literal.Lexical,
                        literal.Language?.ToLowerInvariant(),
                        triple));
                    break;

                case SkosVocabulary.Notation when subjectId is not null && triple.Object is LiteralTerm notation:
                    Add(notations, subjectId, notation.Lexical);
                    break;

                case SkosVocabulary.InScheme when subjectId is not null && objectId is not null:
                    Add(memberships, subjectId, objectId);
                    break;

                case SkosVocabulary.TopConceptOf when subjectId is not null && objectId is not null:
                    Add(memberships, subjectId, objectId);
                    Add(topsByScheme, objectId, subjectId);
                    break;

                case SkosVocabulary.HasTopConcept when subjectId is not null && objectId is not null:
                    Add(memberships, objectId, subjectId);
                    Add(topsByScheme, subjectId, objectId);
                    break;

                case SkosVocabulary.Broader when subjectId is not null && objectId is not null:
                    // subject skos:broader object - the object is the broader one.
                    Pair(hierarchyPairs, (objectId, subjectId), triple);
                    break;

                case SkosVocabulary.Narrower when subjectId is not null && objectId is not null:
                    // subject skos:narrower object - the subject is the broader one.
                    Pair(hierarchyPairs, (subjectId, objectId), triple);
                    break;

                case SkosVocabulary.Related when subjectId is not null && objectId is not null:
                    var key = string.CompareOrdinal(subjectId, objectId) <= 0 ? (subjectId, objectId) : (objectId, subjectId);
                    Pair(relatedPairs, key, triple);
                    break;

                case SkosVocabulary.Member when subjectId is not null && objectId is not null:
                    if (!orderedCollectionIds.Contains(subjectId))
                    {
                        Add(membersByCollection, subjectId, objectId);
                    }

                    break;

                case SkosVocabulary.MemberList when subjectId is not null:
                {
                    var cursor = triple.Object;
                    while (cursor is BlankTerm cell && firsts.TryGetValue(cell, out var member))
                    {
                        if (ElementId(member) is { } memberId)
                        {
                            Add(membersByCollection, subjectId, memberId);
                        }

                        cursor = rests.TryGetValue(cell, out var next) ? next : new IriTerm(RdfVocabulary.Nil, "rdf:nil");
                    }

                    break;
                }

                default:
                    if (SkosVocabulary.Mappings.Contains(triple.Predicate.Iri) && subjectId is not null)
                    {
                        // Drawn only when both ends are concepts in this file; otherwise the
                        // triple becomes a property-grid row - never a stub node (Requirement 1.3).
                        if (objectId is not null && conceptIds.Contains(subjectId) && conceptIds.Contains(objectId))
                        {
                            mappingEdges.Add(new SkosEdge(
                                $"edge:{subjectId}|{triple.Predicate.Iri}|{objectId}",
                                subjectId, objectId, SkosEdgeKind.Mapping, triple.Predicate.Iri, [triple], AssertedBothWays: false));
                        }
                        else
                        {
                            outOfFileMappings.Add(triple);
                        }
                    }

                    break;
            }
        }

        // The budget order (Requirement 8.2), walked over the drawable element set.
        var childrenOf = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (broaderId, narrowerId) in hierarchyPairs.Keys)
        {
            Add(childrenOf, broaderId, narrowerId);
        }

        var order = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var schemeId in schemeIds.Order(StringComparer.Ordinal))
        {
            Take(schemeId);
            var members = conceptIds.Where(id => memberships.TryGetValue(id, out var m) && m.Contains(schemeId)).ToHashSet(StringComparer.Ordinal);
            var tops = topsByScheme.TryGetValue(schemeId, out var t)
                ? t.Distinct(StringComparer.Ordinal).ToList()
                : [];
            var broaderless = members.Where(id =>
                !hierarchyPairs.Keys.Any(pair => pair.NarrowerId == id && members.Contains(pair.BroaderId)));
            var layer = tops.Concat(broaderless).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            while (layer.Count > 0)
            {
                var next = new List<string>();
                foreach (var id in layer)
                {
                    if (members.Contains(id) || tops.Contains(id))
                    {
                        Take(id);
                    }

                    if (childrenOf.TryGetValue(id, out var children))
                    {
                        next.AddRange(children.Where(child => members.Contains(child) && !seen.Contains(child)));
                    }
                }

                layer = next.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            }

            // Members a cycle or a gap kept off every layer still draw, by id.
            foreach (var id in members.Order(StringComparer.Ordinal))
            {
                Take(id);
            }
        }

        foreach (var id in conceptIds.Order(StringComparer.Ordinal))
        {
            Take(id); // The unfiled band: no membership assertion (Requirement 1.1).
        }

        foreach (var id in collectionIds.Order(StringComparer.Ordinal))
        {
            Take(id);
        }

        var total = order.Count;
        var kept = order.Take(budget).ToHashSet(StringComparer.Ordinal);

        var schemes = order.Where(id => kept.Contains(id) && schemeIds.Contains(id))
            .Select(id => new SkosScheme(id, IriOf(id), LabelsOf(id), TopsOf(id)))
            .ToList();
        var concepts = order.Where(id => kept.Contains(id) && conceptIds.Contains(id))
            .Select(id => new SkosConcept(
                id,
                IriOf(id),
                LabelsOf(id),
                notations.TryGetValue(id, out var n) ? n : [],
                (memberships.TryGetValue(id, out var m) ? m : []).Where(schemeIds.Contains).Distinct(StringComparer.Ordinal).Select(IriOf).ToList(),
                id.StartsWith("blank:", StringComparison.Ordinal)))
            .ToList();
        var collections = order.Where(id => kept.Contains(id) && collectionIds.Contains(id))
            .Select(id => new SkosCollection(
                id,
                IriOf(id),
                LabelsOf(id),
                orderedCollectionIds.Contains(id),
                (membersByCollection.TryGetValue(id, out var members) ? members : []).Where(kept.Contains).ToList()))
            .ToList();

        var edges = new List<SkosEdge>();
        foreach (var ((broaderId, narrowerId), (triples, both)) in hierarchyPairs)
        {
            if (kept.Contains(broaderId) && kept.Contains(narrowerId))
            {
                // The canonical id names the broader-direction triple - narrower skos:broader
                // broader - whichever direction(s) the file asserted, so one pair is one id.
                edges.Add(new SkosEdge(
                    $"edge:{narrowerId}|{SkosVocabulary.Broader}|{broaderId}",
                    broaderId, narrowerId, SkosEdgeKind.Hierarchy, SkosVocabulary.Broader, triples, both));
            }
        }

        foreach (var ((a, b), (triples, both)) in relatedPairs)
        {
            if (kept.Contains(a) && kept.Contains(b))
            {
                edges.Add(new SkosEdge(
                    $"edge:{a}|{SkosVocabulary.Related}|{b}", a, b, SkosEdgeKind.Related, SkosVocabulary.Related, triples, both));
            }
        }

        edges.AddRange(mappingEdges.Where(edge => kept.Contains(edge.FromId) && kept.Contains(edge.ToId)));

        return new SkosProjectionResult(
            schemes, concepts, collections,
            edges.OrderBy(edge => edge.Id, StringComparer.Ordinal).ToList(),
            outOfFileMappings, kept.Count, total);

        void Take(string id)
        {
            if ((conceptIds.Contains(id) || schemeIds.Contains(id) || collectionIds.Contains(id)) && seen.Add(id))
            {
                order.Add(id);
            }
        }

        IReadOnlyList<SkosLabel> LabelsOf(string id) => labels.TryGetValue(id, out var found) ? found : [];

        IReadOnlyList<string> TopsOf(string id) =>
            topsByScheme.TryGetValue(id, out var tops) ? tops.Distinct(StringComparer.Ordinal).ToList() : [];
    }

    /// <summary>Whether the model asserts any SKOS-XL label triple - reported once, never resolved (Requirement 3.6).</summary>
    public static bool HasXlLabels(RdfModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Triples.Any(triple => SkosVocabulary.XlLabelProperties.Contains(triple.Predicate.Iri));
    }

    /// <summary>The element id a term draws under, or null for a literal.</summary>
    internal static string? ElementId(RdfTerm term) => term switch
    {
        IriTerm iri => $"res:{iri.Iri}",
        BlankTerm blank => $"blank:{blank.Ordinal}",
        _ => null,
    };

    private static string IriOf(string id) => id.StartsWith("res:", StringComparison.Ordinal) ? id["res:".Length..] : "";

    private static HashSet<string> TypedIds(RdfModel model, string typeIri) =>
        model.Triples
            .Where(triple => triple.Predicate.Iri == RdfVocabulary.Type
                && triple.Object is IriTerm type && type.Iri == typeIri)
            .Select(triple => ElementId(triple.Subject))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

    private static (Dictionary<BlankTerm, RdfTerm> Firsts, Dictionary<BlankTerm, RdfTerm> Rests) ConsCells(RdfModel model)
    {
        var firsts = new Dictionary<BlankTerm, RdfTerm>();
        var rests = new Dictionary<BlankTerm, RdfTerm>();
        foreach (var triple in model.Triples)
        {
            if (triple.Subject is not BlankTerm cell)
            {
                continue;
            }

            switch (triple.Predicate.Iri)
            {
                case RdfVocabulary.First:
                    firsts[cell] = triple.Object;
                    break;
                case RdfVocabulary.Rest:
                    rests[cell] = triple.Object;
                    break;
            }
        }

        return (firsts, rests);
    }

    private static void Add<TValue>(Dictionary<string, List<TValue>> map, string key, TValue value)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = [];
            map[key] = list;
        }

        list.Add(value);
    }

    private static void Pair<TKey>(Dictionary<TKey, (List<RdfTriple> Triples, bool Both)> map, TKey key, RdfTriple triple)
        where TKey : notnull
    {
        if (map.TryGetValue(key, out var entry))
        {
            entry.Triples.Add(triple);
            var both = entry.Both
                || entry.Triples.Select(t => t.Predicate.Iri).Distinct(StringComparer.Ordinal).Count() > 1
                || entry.Triples.Select(t => ElementId(t.Subject)).Distinct(StringComparer.Ordinal).Count() > 1;
            map[key] = (entry.Triples, both);
        }
        else
        {
            map[key] = ([triple], false);
        }
    }
}
