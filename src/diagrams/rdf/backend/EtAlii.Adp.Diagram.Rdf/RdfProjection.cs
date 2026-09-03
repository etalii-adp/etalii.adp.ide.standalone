namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// The data-graph reading: what a model draws. A pure function - the sibling readings project
/// the same model their own way, but every projection is measured against the same budget.
/// </summary>
/// <remarks>
/// <para>
/// The adopted positions, in one place: IRI subjects and objects draw as cards; literals fold
/// into their subject's rows, never nodes; <c>rdf:type</c> triples become badges rather than
/// edges, so a dataset's populations read at a glance without a thousand arrows converging on
/// one type node; collections render as ordered member edges while their cons pairs stay in the
/// file; blank nodes draw carrying the marker the identity boundary refuses edits by.
/// </para>
/// <para>
/// The budget counts drawn nodes in document order of first appearance, so the same file always
/// truncates the same way; edges whose endpoints did not make the cut go with them
/// (Requirement 8).
/// </para>
/// </remarks>
public static class RdfProjection
{
    /// <summary>The drawn-node budget - the module constant the family's specifications name.</summary>
    public const int DefaultBudget = 1000;

    /// <summary>What <paramref name="model"/> draws, cut to <paramref name="budget"/> nodes.</summary>
    public static RdfProjectionResult Project(RdfModel model, int budget = DefaultBudget)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentOutOfRangeException.ThrowIfLessThan(budget, 1);

        // The collection plumbing: a cons cell is a blank node with an rdf:first triple. Its
        // chain is followed here once, so the walk below can skip first/rest triples entirely
        // and draw members in order (Requirement 3.5).
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

        var nodes = new Dictionary<string, RdfProjectionNode>(StringComparer.Ordinal);
        var order = new List<string>();
        var edges = new List<RdfProjectionEdge>();
        var edgeCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var triple in model.Triples)
        {
            var isPlumbing = triple.Predicate.Iri is RdfVocabulary.First or RdfVocabulary.Rest
                && triple.Subject is BlankTerm plumb && firsts.ContainsKey(plumb);
            if (isPlumbing)
            {
                continue;
            }

            var subjectId = Ensure(triple.Subject);
            if (subjectId is null)
            {
                continue;
            }

            // Types become badges on the subject, not edges to a type node (Requirement 3.3).
            if (triple.Predicate.Iri == RdfVocabulary.Type && triple.Object is IriTerm typeIri)
            {
                Amend(subjectId, node => node with { Types = [.. node.Types, Display(model, typeIri)] });
                continue;
            }

            var predicate = Display(model, triple.Predicate);
            switch (triple.Object)
            {
                case LiteralTerm literal:
                    Amend(subjectId, node => node with
                    {
                        Rows = [.. node.Rows, new RdfProjectionRow(predicate, literal.Lexical, Annotation(model, literal))],
                    });
                    break;

                case BlankTerm head when firsts.ContainsKey(head):
                {
                    // A collection: ordered member edges, the cons pairs staying in the file.
                    var index = 1;
                    RdfTerm cursor = head;
                    while (cursor is BlankTerm cell && firsts.TryGetValue(cell, out var member))
                    {
                        Member(subjectId, $"{predicate} [{index}]", triple.Predicate.Iri, member);
                        index++;
                        cursor = rests.TryGetValue(cell, out var next) ? next : new IriTerm(RdfVocabulary.Nil, "rdf:nil");
                    }

                    break;
                }

                default:
                {
                    var objectId = Ensure(triple.Object);
                    if (objectId is not null)
                    {
                        Edge(subjectId, objectId, predicate, triple.Predicate.Iri);
                    }

                    break;
                }
            }
        }

        // The cut: the first N nodes in document order, and only the edges both of whose
        // endpoints survived - deterministic by construction.
        var total = order.Count;
        var kept = order.Take(budget).ToList();
        var keptSet = kept.ToHashSet(StringComparer.Ordinal);
        return new RdfProjectionResult(
            kept.Select(id => nodes[id]).ToList(),
            edges.Where(edge => keptSet.Contains(edge.FromId) && keptSet.Contains(edge.ToId)).ToList(),
            kept.Count,
            total);

        string? Ensure(RdfTerm term)
        {
            switch (term)
            {
                case IriTerm iri:
                {
                    var id = $"res:{iri.Iri}";
                    if (!nodes.ContainsKey(id))
                    {
                        nodes[id] = new RdfProjectionNode(id, iri.Iri, Display(model, iri), [], [], Blank: false);
                        order.Add(id);
                    }

                    return id;
                }

                case BlankTerm blank when !firsts.ContainsKey(blank):
                {
                    var id = $"blank:{blank.Ordinal}";
                    if (!nodes.ContainsKey(id))
                    {
                        var display = blank.Label is { Length: > 0 } label ? $"_:{label}" : $"_:b{blank.Ordinal}";
                        nodes[id] = new RdfProjectionNode(id, "", display, [], [], Blank: true);
                        order.Add(id);
                    }

                    return id;
                }

                default:
                    return null;
            }
        }

        void Amend(string id, Func<RdfProjectionNode, RdfProjectionNode> change) => nodes[id] = change(nodes[id]);

        void Edge(string fromId, string toId, string predicate, string predicateIri)
        {
            var key = $"{fromId}|{predicateIri}|{toId}";
            edgeCounts.TryGetValue(key, out var count);
            edgeCounts[key] = count + 1;
            var id = count == 0 ? $"edge:{key}" : $"edge:{key}|{count}";
            edges.Add(new RdfProjectionEdge(id, fromId, toId, predicate, predicateIri));
        }

        void Member(string subjectId, string label, string predicateIri, RdfTerm member)
        {
            switch (member)
            {
                case LiteralTerm literal:
                    Amend(subjectId, node => node with
                    {
                        Rows = [.. node.Rows, new RdfProjectionRow(label, literal.Lexical, Annotation(model, literal))],
                    });
                    break;

                default:
                {
                    var memberId = Ensure(member);
                    if (memberId is not null)
                    {
                        Edge(subjectId, memberId, label, predicateIri);
                    }

                    break;
                }
            }
        }
    }

    /// <summary>
    /// A term's display form: its prefixed name under the document's own declarations, the
    /// IRI's local name where no prefix reaches it, and the full IRI as the last resort
    /// (Requirement 3.1's fallback chain).
    /// </summary>
    internal static string Display(RdfModel model, IriTerm term)
    {
        var compressed = RdfWriter.Compress(model, term.Iri);
        if (!compressed.StartsWith('<'))
        {
            return compressed;
        }

        var iri = term.Iri;
        var cut = Math.Max(iri.LastIndexOf('#'), iri.LastIndexOf('/'));
        return cut >= 0 && cut < iri.Length - 1 ? iri[(cut + 1)..] : iri;
    }

    private static string Annotation(RdfModel model, LiteralTerm literal) =>
        literal.Language is { Length: > 0 } language
            ? $"@{language}"
            : literal.DatatypeIri is { Length: > 0 } datatype && datatype != RdfVocabulary.XsdString
                ? RdfWriter.Compress(model, datatype)
                : "";
}
