namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// The classification pass under <see cref="OwlProjection"/>: which IRIs this file declares or
/// uses as classes, properties, datatypes and individuals, plus the lookups the sweeps need.
/// Classification is per-role, so a punned IRI lands in more than one set (Requirement 2.4).
/// Everything here is asserted - membership comes from the file's triples, never from entailment.
/// </summary>
internal sealed class OwlModelIndex
{
    public HashSet<string> Classes { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Individuals { get; } = new(StringComparer.Ordinal);
    public List<string> ObjectProperties { get; } = [];
    public List<string> DataProperties { get; } = [];
    public HashSet<string> AnnotationProperties { get; } = new(StringComparer.Ordinal);
    public HashSet<string> DeclaredSubjects { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Deprecated { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Labels { get; } = new(StringComparer.Ordinal);
    public Dictionary<int, RdfTerm> Firsts { get; } = [];
    public Dictionary<int, RdfTerm> Rests { get; } = [];
    public List<List<string>> DisjointGroups { get; } = [];
    public string? OntologyIri { get; private set; }

    private readonly Dictionary<string, List<string>> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _domains = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _ranges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _imports = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _inverses = new(StringComparer.Ordinal);
    private readonly Dictionary<int, List<RdfTriple>> _byBlankSubject = [];
    private readonly HashSet<string> _declaredDatatypes = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, string> _characteristicWords = new(StringComparer.Ordinal)
    {
        [OwlVocabulary.FunctionalProperty] = "functional",
        [OwlVocabulary.InverseFunctionalProperty] = "inverse functional",
        [OwlVocabulary.TransitiveProperty] = "transitive",
        [OwlVocabulary.SymmetricProperty] = "symmetric",
        [OwlVocabulary.AsymmetricProperty] = "asymmetric",
        [OwlVocabulary.ReflexiveProperty] = "reflexive",
        [OwlVocabulary.IrreflexiveProperty] = "irreflexive",
    };

    // Characteristic types that OWL 2 defines as object properties by themselves;
    // owl:FunctionalProperty is deliberately absent - it types datatype properties too.
    private static readonly string[] _objectPropertyTypes =
    [
        OwlVocabulary.ObjectProperty, OwlVocabulary.InverseFunctionalProperty, OwlVocabulary.TransitiveProperty,
        OwlVocabulary.SymmetricProperty, OwlVocabulary.AsymmetricProperty, OwlVocabulary.ReflexiveProperty,
        OwlVocabulary.IrreflexiveProperty,
    ];

    public static OwlModelIndex Build(RdfModel model)
    {
        var index = new OwlModelIndex();

        // First pass: declarations, plumbing and per-subject facts.
        foreach (var triple in model.Triples)
        {
            if (triple.Subject is BlankTerm blankSubject)
            {
                if (!index._byBlankSubject.TryGetValue(blankSubject.Ordinal, out var list))
                {
                    list = [];
                    index._byBlankSubject[blankSubject.Ordinal] = list;
                }

                list.Add(triple);

                switch (triple.Predicate.Iri)
                {
                    case RdfVocabulary.First:
                        index.Firsts[blankSubject.Ordinal] = triple.Object;
                        break;
                    case RdfVocabulary.Rest:
                        index.Rests[blankSubject.Ordinal] = triple.Object;
                        break;
                }

                continue;
            }

            if (triple.Subject is not IriTerm subject)
            {
                continue;
            }

            index.DeclaredSubjects.Add(subject.Iri);

            switch (triple.Predicate.Iri)
            {
                case RdfVocabulary.Type when triple.Object is IriTerm type:
                    index.Add(index._types, subject.Iri, type.Iri);
                    index.Classify(subject.Iri, type.Iri);
                    break;
                case RdfVocabulary.Label when triple.Object is LiteralTerm label && !index.Labels.ContainsKey(subject.Iri):
                    index.Labels[subject.Iri] = label.Lexical;
                    break;
                case OwlVocabulary.Deprecated when triple.Object is LiteralTerm { Lexical: "true" }:
                    index.Deprecated.Add(subject.Iri);
                    break;
                case OwlVocabulary.Domain when triple.Object is IriTerm domain:
                    index.Add(index._domains, subject.Iri, domain.Iri);
                    index.Classes.Add(domain.Iri);
                    break;
                case OwlVocabulary.Range when triple.Object is IriTerm range:
                    index.Add(index._ranges, subject.Iri, range.Iri);
                    break;
                case OwlVocabulary.SubClassOf:
                    index.Classes.Add(subject.Iri);
                    if (triple.Object is IriTerm super)
                    {
                        index.Classes.Add(super.Iri);
                    }

                    break;
                case OwlVocabulary.EquivalentClass or OwlVocabulary.DisjointWith when triple.Object is IriTerm other:
                    index.Classes.Add(subject.Iri);
                    index.Classes.Add(other.Iri);
                    break;
                case OwlVocabulary.InverseOf when triple.Object is IriTerm inverse:
                    index._inverses[subject.Iri] = inverse.Iri;
                    break;
                case OwlVocabulary.Imports when triple.Object is IriTerm import:
                    index.Add(index._imports, subject.Iri, import.Iri);
                    break;
            }
        }

        // Second pass: what classification of others implies - individuals typed by this
        // file's classes, datatype ranges, and the AllDisjointClasses groups.
        foreach (var (subject, types) in index._types)
        {
            if (types.Any(type => !OwlVocabulary.IsBuiltIn(type) && index.Classes.Contains(type)))
            {
                index.Individuals.Add(subject);
            }
        }

        foreach (var (_, triples) in index._byBlankSubject)
        {
            var isGroup = triples.Any(t =>
                t.Predicate.Iri == RdfVocabulary.Type && t.Object is IriTerm { Iri: OwlVocabulary.AllDisjointClasses });
            if (!isGroup)
            {
                continue;
            }

            var membersTriple = triples.FirstOrDefault(t => t.Predicate.Iri == OwlVocabulary.Members);
            if (membersTriple is null)
            {
                continue;
            }

            var members = new List<string>();
            var cursor = membersTriple.Object;
            while (cursor is BlankTerm cell && index.Firsts.TryGetValue(cell.Ordinal, out var member))
            {
                if (member is IriTerm memberIri)
                {
                    members.Add(memberIri.Iri);
                    index.Classes.Add(memberIri.Iri);
                }

                cursor = index.Rests.TryGetValue(cell.Ordinal, out var next) ? next : new IriTerm(RdfVocabulary.Nil, "rdf:nil");
            }

            index.DisjointGroups.Add(members);
        }

        return index;
    }

    public IReadOnlyList<string> TypesOf(string iri) =>
        _types.TryGetValue(iri, out var list) ? list : [];

    public IReadOnlyList<string> DomainsOf(string property) =>
        _domains.TryGetValue(property, out var list) ? list : [];

    public IReadOnlyList<string> RangesOf(string property) =>
        _ranges.TryGetValue(property, out var list) ? list : [];

    public IReadOnlyList<string> ImportsOf(string iri) =>
        _imports.TryGetValue(iri, out var list) ? list : [];

    public string? InverseOf(string property) =>
        _inverses.TryGetValue(property, out var inverse) ? inverse : null;

    public IEnumerable<string> CharacteristicsOf(string property) =>
        TypesOf(property)
            .Where(_characteristicWords.ContainsKey)
            .Select(type => _characteristicWords[type]);

    public IReadOnlyList<RdfTriple> TriplesOf(BlankTerm subject) =>
        _byBlankSubject.TryGetValue(subject.Ordinal, out var list) ? list : [];

    /// <summary>Whether the IRI names a datatype in this file's terms: XSD's, or one declared <c>rdfs:Datatype</c>.</summary>
    public bool IsDatatype(string iri) =>
        iri.StartsWith(OwlVocabulary.XsdNamespace, StringComparison.Ordinal)
        || iri is OwlVocabulary.RdfsLiteral or RdfVocabulary.LangString
        || _declaredDatatypes.Contains(iri);

    private void Classify(string subject, string type)
    {
        switch (type)
        {
            case OwlVocabulary.Class or OwlVocabulary.RdfsClass:
                Classes.Add(subject);
                break;
            case OwlVocabulary.DatatypeProperty:
                AddOnce(DataProperties, subject);
                break;
            case OwlVocabulary.AnnotationProperty:
                AnnotationProperties.Add(subject);
                break;
            case OwlVocabulary.NamedIndividual:
                Individuals.Add(subject);
                break;
            case OwlVocabulary.Ontology:
                OntologyIri ??= subject;
                break;
            case OwlVocabulary.RdfsDatatype:
                _declaredDatatypes.Add(subject);
                break;
            default:
                if (_objectPropertyTypes.Contains(type))
                {
                    AddOnce(ObjectProperties, subject);
                }

                break;
        }
    }

    private void Add(Dictionary<string, List<string>> map, string key, string value)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = [];
            map[key] = list;
        }

        list.Add(value);
    }

    private static void AddOnce(List<string> list, string value)
    {
        if (!list.Contains(value))
        {
            list.Add(value);
        }
    }
}
