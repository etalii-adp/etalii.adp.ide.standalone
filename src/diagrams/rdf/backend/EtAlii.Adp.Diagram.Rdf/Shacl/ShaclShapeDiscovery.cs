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
                foreach ((RdfTerm member, int memberIndex) in ListMembers(model, triple.Object))
                {
                    Mark(found, member, memberIndex);
                }
            }
        }

        // One pass each for the two per-shape facts, so discovery stays linear in triples
        // whatever the shape count (the reading's performance requirement).
        var pathSubjects = new HashSet<string>(StringComparer.Ordinal);
        foreach (var triple in model.Triples)
        {
            if (triple.Predicate.Iri == ShaclVocabulary.Path && KeyOf(triple.Subject) is { } subject)
            {
                pathSubjects.Add(subject);
            }
        }

        var classSubjects = ClassSubjects(model);

        return
        [
            .. found
                .OrderBy(entry => entry.Value.Index)
                .Select(entry => new ShaclShape(
                    entry.Value.Term,
                    entry.Key,
                    entry.Value.Index,
                    IsPropertyShape: pathSubjects.Contains(entry.Key),
                    ImplicitClassTarget: entry.Value.Term is IriTerm && classSubjects.Contains(entry.Key))),
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

    /// <summary>
    /// Every subject the file states to be a class: typed <c>rdfs:Class</c> directly, or typed
    /// with a class the file's own <c>rdfs:subClassOf</c> triples reach <c>rdfs:Class</c> from.
    /// Only stated triples count - the no-inference rule holds here as everywhere.
    /// </summary>
    private static HashSet<string> ClassSubjects(RdfModel model)
    {
        // The classes: rdfs:Class plus everything a stated subClassOf chain connects to it,
        // walked backward from rdfs:Class over the file's own triples.
        var classIris = new HashSet<string>(StringComparer.Ordinal) { ShaclVocabulary.RdfsClass };
        bool grew;
        do
        {
            grew = false;
            foreach (var triple in model.Triples)
            {
                if (triple is { Predicate.Iri: ShaclVocabulary.RdfsSubClassOf, Subject: IriTerm subject, Object: IriTerm parent }
                    && classIris.Contains(parent.Iri)
                    && classIris.Add(subject.Iri))
                {
                    grew = true;
                }
            }
        }
        while (grew);

        var subjects = new HashSet<string>(StringComparer.Ordinal);
        foreach (var triple in model.Triples)
        {
            if (triple.Predicate.Iri == RdfVocabulary.Type
                && triple.Object is IriTerm type
                && classIris.Contains(type.Iri)
                && KeyOf(triple.Subject) is { } subject)
            {
                subjects.Add(subject);
            }
        }

        return subjects;
    }
}
