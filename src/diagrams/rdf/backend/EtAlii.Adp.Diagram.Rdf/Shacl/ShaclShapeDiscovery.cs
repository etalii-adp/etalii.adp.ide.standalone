namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>
/// Which subjects and objects of a parsed document are shapes, by the recommendation's own
/// declaration-or-use definition: typed <c>sh:NodeShape</c>/<c>sh:PropertyShape</c>, the subject
/// of a target or of a constraint parameter, the object of a shape-expecting parameter
/// (<c>sh:node</c>, <c>sh:property</c>, <c>sh:qualifiedValueShape</c>, <c>sh:not</c>), or a
/// member of a <c>sh:and</c>/<c>sh:or</c>/<c>sh:xone</c> list. Non-shape subjects are never
/// shapes - co-resident ontology or instance content belongs to the sibling readings
/// (shacl-diagram Requirement 1.1).
/// </summary>
/// <remarks>
/// Pure over <see cref="RdfModel"/>, never the text. Ordering is the deterministic backbone of
/// the whole reading: shapes come back in document order of the triple that first made each one
/// a shape.
/// </remarks>
public static class ShaclShapeDiscovery
{
    public static IReadOnlyList<ShaclShape> Discover(RdfModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var found = new Dictionary<string, (RdfTerm Term, int Index)>(StringComparer.Ordinal);

        for (var index = 0; index < model.Triples.Count; index++)
        {
            var triple = model.Triples[index];
            var predicate = triple.Predicate.Iri;

            if (predicate == RdfVocabulary.Type
                && triple.Object is IriTerm { Iri: ShaclVocabulary.NodeShape or ShaclVocabulary.PropertyShape })
            {
                Mark(found, triple.Subject, index);
                continue;
            }

            if (ShaclVocabulary.TargetPredicates.Contains(predicate)
                || ShaclVocabulary.ConstraintParameters.Contains(predicate))
            {
                Mark(found, triple.Subject, index);
            }

            if (ShaclVocabulary.ShapeExpectingPredicates.Contains(predicate))
            {
                Mark(found, triple.Object, index);
            }

            if (ShaclVocabulary.ShapeListPredicates.Contains(predicate))
            {
                foreach (var (member, memberIndex) in ListMembers(model, triple.Object))
                {
                    Mark(found, member, memberIndex);
                }
            }
        }

        return
        [
            .. found
                .OrderBy(entry => entry.Value.Index)
                .Select(entry => new ShaclShape(
                    entry.Value.Term,
                    entry.Key,
                    entry.Value.Index,
                    model.Triples[entry.Value.Index].Span,
                    IsPropertyShape: HasPath(model, entry.Key),
                    ImplicitClassTarget: entry.Value.Term is IriTerm && IsClass(model, entry.Key))),
        ];
    }

    /// <summary>The identity key of a term, or null where the term can never be a shape.</summary>
    public static string? KeyOf(RdfTerm term) => term switch
    {
        IriTerm iri => "i:" + iri.Iri,
        BlankTerm blank => "b:" + blank.Ordinal,
        _ => null, // a literal is not a shape, whatever position it sits in
    };

    private static void Mark(Dictionary<string, (RdfTerm Term, int Index)> found, RdfTerm term, int index)
    {
        if (KeyOf(term) is not { } key)
        {
            return;
        }

        // The first triple that makes a term a shape wins, so ordering stays document order.
        if (!found.TryGetValue(key, out var existing) || index < existing.Index)
        {
            found[key] = (term, index);
        }
    }

    /// <summary>
    /// The members of an RDF list, walked over the cons triples the parser expanded, each with the
    /// index of its own <c>rdf:first</c> triple. A malformed or cyclic list yields what it has and
    /// stops - discovery draws what is stated, the validator judges it.
    /// </summary>
    private static IEnumerable<(RdfTerm Member, int Index)> ListMembers(RdfModel model, RdfTerm head)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = head;
        while (current is not IriTerm { Iri: RdfVocabulary.Nil })
        {
            if (KeyOf(current) is not { } key || !visited.Add(key))
            {
                yield break;
            }

            var first = FirstTriple(model, key, RdfVocabulary.First);
            var rest = FirstTriple(model, key, RdfVocabulary.Rest);
            if (first is { } firstTriple)
            {
                yield return (firstTriple.Triple.Object, firstTriple.Index);
            }

            if (rest is not { } restTriple)
            {
                yield break;
            }

            current = restTriple.Triple.Object;
        }
    }

    private static (RdfTriple Triple, int Index)? FirstTriple(RdfModel model, string subjectKey, string predicateIri)
    {
        for (var index = 0; index < model.Triples.Count; index++)
        {
            var triple = model.Triples[index];
            if (triple.Predicate.Iri == predicateIri && KeyOf(triple.Subject) == subjectKey)
            {
                return (triple, index);
            }
        }

        return null;
    }

    private static bool HasPath(RdfModel model, string shapeKey) =>
        model.Triples.Any(triple => triple.Predicate.Iri == ShaclVocabulary.Path && KeyOf(triple.Subject) == shapeKey);

    /// <summary>
    /// Whether the file states the term to be a class: typed <c>rdfs:Class</c> directly, or typed
    /// with something the file's own <c>rdfs:subClassOf</c> triples reach <c>rdfs:Class</c> from.
    /// Only stated triples count - the no-inference rule holds here as everywhere.
    /// </summary>
    private static bool IsClass(RdfModel model, string shapeKey)
    {
        foreach (var triple in model.Triples)
        {
            if (triple.Predicate.Iri != RdfVocabulary.Type || KeyOf(triple.Subject) != shapeKey)
            {
                continue;
            }

            if (triple.Object is IriTerm type && ReachesRdfsClass(model, type.Iri, []))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ReachesRdfsClass(RdfModel model, string classIri, HashSet<string> visited)
    {
        if (classIri == ShaclVocabulary.RdfsClass)
        {
            return true;
        }

        if (!visited.Add(classIri))
        {
            return false;
        }

        return model.Triples.Any(triple =>
            triple.Predicate.Iri == ShaclVocabulary.RdfsSubClassOf
            && triple.Subject is IriTerm { } subject && subject.Iri == classIri
            && triple.Object is IriTerm parent && ReachesRdfsClass(model, parent.Iri, visited));
    }
}
