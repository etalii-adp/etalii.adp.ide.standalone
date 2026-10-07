namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// The ontology reading: what a model draws when its triples are read as OWL 2 axioms
/// (owl-diagram Requirements 1-3 and 8.3). A pure function over the anchor's model - no parser,
/// no store, no file I/O of this reading's own.
/// </summary>
/// <remarks>
/// <para>
/// The adopted positions, in one place: classes, datatypes and individuals draw as nodes while
/// properties draw as edges between their stated domains and ranges (VOWL's vocabulary);
/// a domain-less or range-less property anchors at a materialized <c>owl:Thing</c>; datatypes
/// are nodes because in an ontology the datatype is the axiom's point, while an individual's
/// literal assertions stay rows in its card (the split literal-node position); types are badges,
/// never edges; blank-node-rooted class expressions draw as expression nodes on structural ids
/// the identity boundary governs; and everything shown is asserted - nothing is inferred.
/// </para>
/// <para>
/// The budget cut walks whole units - a named node together with the expression nodes attached
/// to it - in document order of first appearance, so the same file always truncates the same way
/// and never splits a class from its expressions (Requirement 8.3).
/// </para>
/// </remarks>
public static class OwlProjection
{
    private static readonly string[] _quantifierPredicates =
    [
        OwlVocabulary.SomeValuesFrom, OwlVocabulary.AllValuesFrom, OwlVocabulary.HasValue,
        OwlVocabulary.Cardinality, OwlVocabulary.MinCardinality, OwlVocabulary.MaxCardinality,
        OwlVocabulary.QualifiedCardinality, OwlVocabulary.MinQualifiedCardinality, OwlVocabulary.MaxQualifiedCardinality,
    ];

    /// <summary>What <paramref name="model"/> draws as an ontology, cut to <paramref name="budget"/> nodes.</summary>
    public static OwlGraphResult Project(RdfModel model, int budget = RdfProjection.DefaultBudget)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentOutOfRangeException.ThrowIfLessThan(budget, 1);

        var index = OwlModelIndex.Build(model);

        var nodes = new Dictionary<string, OwlNode>(StringComparer.Ordinal);
        var order = new List<string>();
        var unitOf = new Dictionary<string, string>(StringComparer.Ordinal);
        var edges = new List<OwlEdge>();
        var edgeCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        // Named nodes, in document order of first appearance - every role an IRI has draws
        // in its own place, which is all punning needs (Requirement 2.4).
        foreach (var triple in model.Triples)
        {
            EnsureNodesFor(triple.Subject);
            EnsureNodesFor(triple.Object);
        }

        // Rows and badges.
        foreach (var triple in model.Triples)
        {
            if (triple.Subject is not IriTerm subject || triple.Object is not LiteralTerm literal)
            {
                continue;
            }

            if (triple.Predicate.Iri == OwlVocabulary.Deprecated)
            {
                continue; // The flag, not a row.
            }

            var id = PrimaryId(subject.Iri);
            if (id is not null && nodes.TryGetValue(id, out var node))
            {
                nodes[id] = node with
                {
                    Rows = [.. node.Rows, new RdfProjectionRow(RdfProjection.Display(model, triple.Predicate), literal.Lexical, Annotation(model, literal))],
                };
            }
        }

        foreach (var individual in index.Individuals)
        {
            if (nodes.TryGetValue($"{(index.Classes.Contains(individual) ? "ind" : "res")}:{individual}", out var node))
            {
                var badges = index.TypesOf(individual)
                    .Where(type => !OwlVocabulary.IsBuiltIn(type))
                    .Select(Display)
                    .ToList();
                nodes[node.Id] = node with { Badges = badges };
            }
        }

        if (index.OntologyIri is { } ontologyIri && nodes.TryGetValue($"res:{ontologyIri}", out var header))
        {
            var imports = index.ImportsOf(ontologyIri)
                .Select(import => new RdfProjectionRow("owl:imports", import, ""))
                .ToList();
            nodes[header.Id] = header with { Rows = [.. header.Rows, .. imports] };
        }

        // Property edges: each object property between its domains and ranges (Thing anchors
        // where a side is unstated, Requirement 1.2), each datatype property to its datatype.
        foreach (var property in index.ObjectProperties)
        {
            var label = PropertyLabel(property);
            var froms = EndpointIds(index.DomainsOf(property), property, "domain");
            var tos = EndpointIds(index.RangesOf(property).Where(range => !index.IsDatatype(range)), property, "range");
            foreach (var from in froms)
            {
                foreach (var to in tos)
                {
                    Edge(OwlEdgeKind.ObjectProperty, from, to, label, property);
                }
            }
        }

        foreach (var property in index.DataProperties)
        {
            var label = PropertyLabel(property);
            var froms = EndpointIds(index.DomainsOf(property), property, "domain");
            var ranges = index.RangesOf(property).Where(index.IsDatatype).ToList();
            var tos = ranges.Count > 0
                ? ranges.Select(EnsureDatatype).ToList()
                : new List<string> { EnsureAnchor($"dt:{property}", OwlNodeKind.Datatype, "Literal", "") };
            foreach (var from in froms)
            {
                foreach (var to in tos)
                {
                    Edge(OwlEdgeKind.DatatypeProperty, from, to, label, property);
                }
            }
        }

        // Axiom edges and assertions, in document order.
        foreach (var triple in model.Triples)
        {
            if (triple.Subject is not IriTerm subject)
            {
                continue;
            }

            switch (triple.Predicate.Iri)
            {
                case OwlVocabulary.SubClassOf when triple.Object is IriTerm to:
                    Edge(OwlEdgeKind.Subclass, $"res:{subject.Iri}", $"res:{to.Iri}", "", triple.Predicate.Iri);
                    break;
                case OwlVocabulary.EquivalentClass when triple.Object is IriTerm to && index.Classes.Contains(subject.Iri):
                    Edge(OwlEdgeKind.Equivalent, $"res:{subject.Iri}", $"res:{to.Iri}", "", triple.Predicate.Iri);
                    break;
                case OwlVocabulary.DisjointWith when triple.Object is IriTerm to:
                    Edge(OwlEdgeKind.Disjoint, $"res:{subject.Iri}", $"res:{to.Iri}", "", triple.Predicate.Iri);
                    break;
                default:
                {
                    // An object-property assertion between individuals draws as an edge; the
                    // subject's literal assertions already landed as rows.
                    if (index.Individuals.Contains(subject.Iri)
                        && triple.Object is IriTerm target
                        && !OwlVocabulary.IsBuiltIn(triple.Predicate.Iri)
                        && nodes.ContainsKey(IndividualId(target.Iri)))
                    {
                        Edge(OwlEdgeKind.Assertion, IndividualId(subject.Iri), IndividualId(target.Iri), Display(triple.Predicate.Iri), triple.Predicate.Iri);
                    }

                    break;
                }
            }
        }

        // owl:AllDisjointClasses groups: pairwise disjointness edges over the members list.
        foreach (var group in index.DisjointGroups)
        {
            for (var i = 0; i < group.Count; i++)
            {
                for (var j = i + 1; j < group.Count; j++)
                {
                    Edge(OwlEdgeKind.Disjoint, $"res:{group[i]}", $"res:{group[j]}", "", OwlVocabulary.AllDisjointClasses);
                }
            }
        }

        // Class expressions: every blank node in class position becomes an expression node on a
        // structural id - deterministic within this parse, deliberately unstable across edits,
        // which is exactly what the blank-node identity boundary governs (Requirement 3).
        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var triple in model.Triples)
        {
            if (triple.Subject is not IriTerm owner
                || triple.Predicate.Iri is not (OwlVocabulary.SubClassOf or OwlVocabulary.EquivalentClass or OwlVocabulary.DisjointWith))
            {
                continue;
            }

            var ordinalKey = $"{owner.Iri}|{triple.Predicate.Iri}";
            ordinals.TryGetValue(ordinalKey, out var ordinal);
            ordinals[ordinalKey] = ordinal + 1;

            if (triple.Object is not BlankTerm root)
            {
                continue;
            }

            var ownerId = $"res:{owner.Iri}";
            var expressionId = $"expr:{owner.Iri}|{triple.Predicate.Iri}|{ordinal}";
            var kind = triple.Predicate.Iri switch
            {
                OwlVocabulary.EquivalentClass => OwlEdgeKind.Equivalent,
                OwlVocabulary.DisjointWith => OwlEdgeKind.Disjoint,
                _ => OwlEdgeKind.Subclass,
            };
            BuildExpression(expressionId, ownerId, root);
            Edge(kind, ownerId, expressionId, "", triple.Predicate.Iri);
        }

        // The cut: whole units in document order - a unit is a named node plus every expression
        // node and anchor grouped with it - and only the edges both of whose endpoints survived.
        var unitRoots = new List<string>();
        var unitMembers = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var id in order)
        {
            var root = unitOf.TryGetValue(id, out var owner) ? owner : id;
            if (!unitMembers.TryGetValue(root, out var members))
            {
                members = [];
                unitMembers[root] = members;
                unitRoots.Add(root);
            }

            members.Add(id);
        }

        var kept = new List<string>();
        foreach (var root in unitRoots)
        {
            var members = unitMembers[root];
            if (kept.Count + members.Count > budget)
            {
                break;
            }

            kept.AddRange(members);
        }

        var keptSet = kept.ToHashSet(StringComparer.Ordinal);
        return new OwlGraphResult(
            kept.Select(id => nodes[id]).ToList(),
            edges.Where(edge => keptSet.Contains(edge.FromId) && keptSet.Contains(edge.ToId)).ToList(),
            kept.Count,
            order.Count);

        void EnsureNodesFor(RdfTerm term)
        {
            if (term is not IriTerm iri)
            {
                return;
            }

            if (index.Classes.Contains(iri.Iri) || iri.Iri == OwlVocabulary.Thing)
            {
                var kind = iri.Iri is OwlVocabulary.Thing or OwlVocabulary.Nothing ? OwlNodeKind.Thing : OwlNodeKind.Class;
                Ensure($"res:{iri.Iri}", kind, iri.Iri);
            }

            if (index.IsDatatype(iri.Iri))
            {
                EnsureDatatype(iri.Iri);
            }

            if (index.Individuals.Contains(iri.Iri))
            {
                Ensure(IndividualId(iri.Iri), OwlNodeKind.Individual, iri.Iri);
            }

            if (iri.Iri == index.OntologyIri)
            {
                Ensure($"res:{iri.Iri}", OwlNodeKind.OntologyHeader, iri.Iri);
            }
        }

        void Ensure(string id, OwlNodeKind kind, string iri)
        {
            if (nodes.ContainsKey(id))
            {
                return;
            }

            // Datatype nodes skip the external dimming: their kind already says they are
            // borrowed vocabulary, and dimming every xsd term would drown the signal.
            var external = kind is OwlNodeKind.Class or OwlNodeKind.Individual
                && !index.DeclaredSubjects.Contains(iri)
                && !OwlVocabulary.IsBuiltIn(iri);
            nodes[id] = new OwlNode(id, kind, iri, Display(iri), [], [], index.Deprecated.Contains(iri), external);
            order.Add(id);
        }

        string EnsureDatatype(string iri)
        {
            var id = $"res:{iri}";
            Ensure(id, OwlNodeKind.Datatype, iri);
            return id;
        }

        string EnsureAnchor(string id, OwlNodeKind kind, string display, string unitRoot)
        {
            if (!nodes.ContainsKey(id))
            {
                nodes[id] = new OwlNode(id, kind, "", display, [], [], Deprecated: false, External: false);
                order.Add(id);
                if (unitRoot.Length > 0)
                {
                    unitOf[id] = unitRoot;
                }
            }

            return id;
        }

        List<string> EndpointIds(IEnumerable<string> stated, string property, string side)
        {
            var ids = stated.Select(iri => $"res:{iri}").Where(nodes.ContainsKey).ToList();
            if (ids.Count == 0)
            {
                // The VOWL anchor: a property without a stated end attaches to its own small
                // owl:Thing, distinct from a stated owl:Thing axiom (Requirement 1.2).
                ids.Add(EnsureAnchor($"thing:{property}|{side}", OwlNodeKind.Thing, "Thing", ""));
            }

            return ids;
        }

        string IndividualId(string iri) => index.Classes.Contains(iri) ? $"ind:{iri}" : $"res:{iri}";

        string? PrimaryId(string iri)
        {
            if (iri == index.OntologyIri || index.Classes.Contains(iri))
            {
                return $"res:{iri}";
            }

            if (index.Individuals.Contains(iri))
            {
                return IndividualId(iri);
            }

            return nodes.ContainsKey($"res:{iri}") ? $"res:{iri}" : null;
        }

        string Display(string iri) =>
            index.Labels.TryGetValue(iri, out var label) ? label : RdfProjection.Display(model, new IriTerm(iri, ""));

        string PropertyLabel(string property)
        {
            var words = index.CharacteristicsOf(property).ToList();
            if (index.InverseOf(property) is { } inverse)
            {
                words.Add($"inverse of {Display(inverse)}");
            }

            return words.Count == 0 ? Display(property) : $"{Display(property)} ({string.Join(", ", words)})";
        }

        void Edge(OwlEdgeKind kind, string fromId, string toId, string label, string propertyIri)
        {
            if (!nodes.ContainsKey(fromId) || !nodes.ContainsKey(toId))
            {
                return;
            }

            var key = $"{fromId}|{propertyIri}|{toId}";
            edgeCounts.TryGetValue(key, out var count);
            edgeCounts[key] = count + 1;
            var id = count == 0 ? $"edge:{key}" : $"edge:{key}|{count}";
            edges.Add(new OwlEdge(id, kind, fromId, toId, label, propertyIri));
        }

        void BuildExpression(string id, string ownerId, BlankTerm root)
        {
            var about = index.TriplesOf(root);
            var isOperator = about.Any(t => t.Predicate.Iri
                is OwlVocabulary.UnionOf or OwlVocabulary.IntersectionOf or OwlVocabulary.ComplementOf or OwlVocabulary.OneOf);
            var hasOnProperty = about.Any(t => t.Predicate.Iri == OwlVocabulary.OnProperty);
            var quantifiers = about.Count(t => _quantifierPredicates.Contains(t.Predicate.Iri));
            var malformed = !isOperator && (!hasOnProperty || quantifiers != 1);

            var kind = isOperator ? OwlNodeKind.Operator : OwlNodeKind.Restriction;
            var rendered = ExpressionRenderer.Render(root, index, model, ExpressionRenderer.CanvasDepth);
            nodes[id] = new OwlNode(
                id, kind, "", rendered.Text, [], [], Deprecated: false, External: false, ownerId, root,
                malformed || rendered.Cyclic, rendered.Elided);
            order.Add(id);
            unitOf[id] = ownerId;

            var childIndex = 0;
            foreach (var triple in about)
            {
                switch (triple.Predicate.Iri)
                {
                    case OwlVocabulary.SomeValuesFrom or OwlVocabulary.AllValuesFrom or OwlVocabulary.OnClass or OwlVocabulary.OnDataRange or OwlVocabulary.HasValue or OwlVocabulary.ComplementOf:
                        Reference(triple.Object);
                        break;

                    case OwlVocabulary.UnionOf or OwlVocabulary.IntersectionOf or OwlVocabulary.OneOf:
                    {
                        var cursor = triple.Object;
                        while (cursor is BlankTerm cell && index.Firsts.TryGetValue(cell.Ordinal, out var member))
                        {
                            Reference(member);
                            cursor = index.Rests.TryGetValue(cell.Ordinal, out var next) ? next : new IriTerm(RdfVocabulary.Nil, "rdf:nil");
                        }

                        // A list that neither reaches rdf:nil nor follows the cons shape is broken (Requirement 3.5).
                        if (cursor is not IriTerm { Iri: RdfVocabulary.Nil } and not BlankTerm)
                        {
                            Malform();
                        }
                        else if (cursor is BlankTerm dangling && !index.Firsts.ContainsKey(dangling.Ordinal))
                        {
                            Malform();
                        }

                        break;
                    }
                }
            }

            void Reference(RdfTerm target)
            {
                switch (target)
                {
                    case IriTerm iri when !OwlVocabulary.IsBuiltIn(iri.Iri) || index.IsDatatype(iri.Iri) || iri.Iri is OwlVocabulary.Thing or OwlVocabulary.Nothing:
                        // A named filler: draw the reference to its class, datatype or individual node.
                        EnsureNodesFor(iri);
                        if (!nodes.ContainsKey($"res:{iri.Iri}"))
                        {
                            Ensure($"res:{iri.Iri}", index.IsDatatype(iri.Iri) ? OwlNodeKind.Datatype : OwlNodeKind.Class, iri.Iri);
                        }

                        Edge(OwlEdgeKind.Expression, id, $"res:{iri.Iri}", "", "");
                        break;

                    case BlankTerm child:
                    {
                        // A nested expression: its own node on a child-suffixed structural id.
                        var childId = $"{id}/{childIndex}";
                        childIndex++;
                        BuildExpression(childId, ownerId, child);
                        Edge(OwlEdgeKind.Expression, id, childId, "", "");
                        break;
                    }
                }
            }

            void Malform() => nodes[id] = nodes[id] with { Malformed = true };
        }
    }

    private static string Annotation(RdfModel model, LiteralTerm literal) =>
        literal.Language is { Length: > 0 } language
            ? $"@{language}"
            : literal.DatatypeIri is { Length: > 0 } datatype && datatype != RdfVocabulary.XsdString
                ? RdfWriter.Compress(model, datatype)
                : "";
}
