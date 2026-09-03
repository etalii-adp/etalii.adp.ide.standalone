namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>
/// The shapes reading: node shapes as cards, property shapes as rows, targets as chips,
/// shape-to-shape references as edges - and nothing else. Non-shape subjects belong to the
/// sibling readings; target terms are declarations, never elements; constraint blank nodes are
/// rows of the card that references them, which is what dissolves the identity boundary here
/// (shacl-diagram Requirements 1 and 3).
/// </summary>
/// <remarks>
/// Pure over <see cref="RdfModel"/>, deterministic: cards in discovery order, rows in source
/// order, edges keyed by their endpoints and kind. The drawn-element budget counts cards only.
/// </remarks>
public static class ShaclProjection
{
    /// <summary>The default drawn-card budget, aligned with the family's.</summary>
    public const int DefaultBudget = 1000;

    /// <summary>The fixed order constraint summaries print their parameters in.</summary>
    private static readonly string[] _summaryOrder =
    [
        ShaclVocabulary.Datatype,
        ShaclVocabulary.Class,
        ShaclVocabulary.NodeKind,
        ShaclVocabulary.Namespace + "minLength",
        ShaclVocabulary.Namespace + "maxLength",
        ShaclVocabulary.Namespace + "pattern",
        ShaclVocabulary.Namespace + "minInclusive",
        ShaclVocabulary.Namespace + "maxInclusive",
        ShaclVocabulary.Namespace + "minExclusive",
        ShaclVocabulary.Namespace + "maxExclusive",
        ShaclVocabulary.Namespace + "uniqueLang",
        ShaclVocabulary.HasValue,
        ShaclVocabulary.In,
        ShaclVocabulary.Node,
        ShaclVocabulary.QualifiedValueShape,
        ShaclVocabulary.Sparql,
    ];

    public static ShaclProjectionResult Project(RdfModel model, int budget = DefaultBudget)
    {
        ArgumentNullException.ThrowIfNull(model);

        var shapes = ShaclShapeDiscovery.Discover(model);
        var context = ProjectionContext.Build(model, shapes);

        // Which shapes are cards: node shapes - except blank ones living only inside a
        // combinator or qualified reference, which summarize inline - plus IRI property shapes
        // no card references, which would otherwise be invisible (Requirements 1.1, 1.6).
        var cardShapes = shapes.Where(shape => IsCard(shape, context)).ToArray();
        var drawn = cardShapes.Length <= budget ? cardShapes : cardShapes[..budget];
        var drawnIds = drawn.Select(shape => IdOf(shape.Key)).ToHashSet(StringComparer.Ordinal);

        // The sh:class claimant map, over drawn cards only: class IRI -> the single drawn card
        // targeting it, or null where zero or several claim it (Requirement 1.5).
        var claimants = ClaimantsOf(drawn, context);

        var cards = new List<ShaclCard>(drawn.Length);
        var edges = new List<ShaclEdge>();
        foreach (var shape in drawn)
        {
            cards.Add(BuildCard(model, shape, context, claimants, drawnIds, edges));
        }

        var uniqueEdges = edges
            .Where(edge => drawnIds.Contains(edge.FromId) && drawnIds.Contains(edge.ToId))
            .DistinctBy(edge => edge.Id, StringComparer.Ordinal)
            .ToArray();

        return new ShaclProjectionResult(cards, uniqueEdges, cards.Count, cardShapes.Length);
    }

    /// <summary>The element id a shape draws under - the family vocabulary's shapes.</summary>
    public static string IdOf(string shapeKey) =>
        shapeKey.StartsWith("i:", StringComparison.Ordinal) ? "res:" + shapeKey[2..] : "blank:" + shapeKey[2..];

    private static bool IsCard(ShaclShape shape, ProjectionContext context)
    {
        if (shape.IsPropertyShape)
        {
            return shape.Term is IriTerm && !context.PropertyReferenced.Contains(shape.Key);
        }

        return shape.Term is IriTerm
            || context.NodeReferenced.Contains(shape.Key)
            || !context.OperandReferenced.Contains(shape.Key);
    }

    private static Dictionary<string, string?> ClaimantsOf(ShaclShape[] drawn, ProjectionContext context)
    {
        var claimants = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var shape in drawn)
        {
            foreach (var classIri in context.TargetedClasses(shape))
            {
                // First claimant wins the slot; a second one voids it - "exactly one" is the rule.
                claimants[classIri] = claimants.ContainsKey(classIri) ? null : IdOf(shape.Key);
            }
        }

        return claimants;
    }

    private static ShaclCard BuildCard(
        RdfModel model,
        ShaclShape shape,
        ProjectionContext context,
        Dictionary<string, string?> claimants,
        HashSet<string> drawnIds,
        List<ShaclEdge> edges)
    {
        var id = IdOf(shape.Key);
        var iri = shape.Term is IriTerm iriTerm ? iriTerm.Iri : "";
        var display = shape.Term switch
        {
            IriTerm term => RdfProjection.Display(model, term),
            BlankTerm { Label: { Length: > 0 } label } => "_:" + label,
            _ => "anonymous shape",
        };

        var chips = new List<ShaclTargetChip>();
        var rows = new List<ShaclRow>();
        var severity = "";
        var deactivated = false;
        var closed = false;
        var name = "";
        var description = "";

        foreach (var (triple, _) in context.TriplesOf(shape.Key))
        {
            var predicate = triple.Predicate.Iri;
            switch (predicate)
            {
                case ShaclVocabulary.TargetClass or ShaclVocabulary.TargetNode
                    or ShaclVocabulary.TargetSubjectsOf or ShaclVocabulary.TargetObjectsOf:
                    chips.Add(Chip(model, context, iri, predicate, triple.Object));
                    break;

                case ShaclVocabulary.Deactivated when triple.Object is LiteralTerm { Lexical: "true" }:
                    deactivated = true;
                    break;

                case ShaclVocabulary.Closed when triple.Object is LiteralTerm { Lexical: "true" }:
                    closed = true;
                    break;

                case ShaclVocabulary.Severity when triple.Object is IriTerm severityTerm
                    && severityTerm.Iri != ShaclVocabulary.Violation:
                    severity = RdfProjection.Display(model, severityTerm);
                    break;

                case ShaclVocabulary.Name when triple.Object is LiteralTerm nameLiteral && name.Length == 0:
                    name = nameLiteral.Lexical;
                    break;

                case ShaclVocabulary.Description when triple.Object is LiteralTerm descriptionLiteral && description.Length == 0:
                    description = descriptionLiteral.Lexical;
                    break;

                case ShaclVocabulary.Property:
                    rows.Add(PropertyRow(model, context, triple.Object, id, claimants, edges));
                    break;

                case ShaclVocabulary.Node:
                    Reference(edges, id, triple.Object, "node", "node");
                    break;

                case ShaclVocabulary.And or ShaclVocabulary.Or or ShaclVocabulary.Xone:
                    CombinatorRow(model, context, triple.Object, id, KindOf(predicate), rows, edges);
                    break;

                case ShaclVocabulary.Not:
                    if (triple.Object is IriTerm)
                    {
                        Reference(edges, id, triple.Object, "not", "not");
                    }
                    else
                    {
                        rows.Add(new ShaclRow("", "", "not(" + InlineSummary(model, context, triple.Object, depth: 0) + ")", "", Sparql: false, Blank: true, ""));
                    }

                    break;

                case ShaclVocabulary.Sparql:
                    rows.Add(new ShaclRow("", "", "SPARQL constraint", "", Sparql: true,
                        Blank: triple.Object is BlankTerm, ""));
                    break;
            }
        }

        if (shape.ImplicitClassTarget)
        {
            chips.Add(new ShaclTargetChip(ShaclTargetKind.ImplicitClass, display, iri, DescribedInFile: true, iri, PredicateIri: ""));
        }

        return new ShaclCard(id, iri, display, shape.Term is BlankTerm, deactivated, severity, closed, name, description, chips, rows);

        void Reference(List<ShaclEdge> into, string fromId, RdfTerm target, string kind, string label)
        {
            if (ShaclShapeDiscovery.KeyOf(target) is { } key)
            {
                var toId = IdOf(key);
                into.Add(new ShaclEdge($"shacl-edge:{fromId}|{kind}|{toId}", fromId, toId, kind, label));
            }
        }
    }

    private static ShaclTargetChip Chip(RdfModel model, ProjectionContext context, string shapeIri, string predicate, RdfTerm target)
    {
        var kind = predicate switch
        {
            ShaclVocabulary.TargetClass => ShaclTargetKind.Class,
            ShaclVocabulary.TargetNode => ShaclTargetKind.Node,
            ShaclVocabulary.TargetSubjectsOf => ShaclTargetKind.SubjectsOf,
            _ => ShaclTargetKind.ObjectsOf,
        };

        var (termDisplay, termIri) = target switch
        {
            IriTerm iri => (RdfProjection.Display(model, iri), iri.Iri),
            LiteralTerm literal => (literal.Lexical, ""),
            BlankTerm blank => ("_:" + (blank.Label ?? blank.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)), ""),
            _ => ("", ""),
        };

        var described = ShaclShapeDiscovery.KeyOf(target) is { } key && context.SubjectKeys.Contains(key);
        return new ShaclTargetChip(kind, termDisplay, termIri, described, shapeIri, predicate);
    }

    private static ShaclRow PropertyRow(
        RdfModel model,
        ProjectionContext context,
        RdfTerm propertyShape,
        string cardId,
        Dictionary<string, string?> claimants,
        List<ShaclEdge> edges)
    {
        var key = ShaclShapeDiscovery.KeyOf(propertyShape);
        if (key is null)
        {
            return new ShaclRow("", "", "", "", Sparql: false, Blank: true, "");
        }

        var path = context.FirstObject(key, ShaclVocabulary.Path) is { } pathTerm ? PathSyntax(model, context, pathTerm, nested: false) : "";
        var name = context.FirstLiteral(key, ShaclVocabulary.Name);
        var min = context.FirstLiteral(key, ShaclVocabulary.MinCount);
        var max = context.FirstLiteral(key, ShaclVocabulary.MaxCount);
        var cardinality = $"[{(min.Length > 0 ? min : "0")}..{(max.Length > 0 ? max : "*")}]";
        var severity = context.FirstObject(key, ShaclVocabulary.Severity) is IriTerm severityTerm
            && severityTerm.Iri != ShaclVocabulary.Violation
                ? RdfProjection.Display(model, severityTerm)
                : "";

        var parts = new List<string>();
        foreach (var parameter in _summaryOrder)
        {
            foreach (var value in context.Objects(key, parameter))
            {
                var part = SummaryPart(model, context, cardId, path, parameter, value, claimants, edges);
                if (part.Length > 0)
                {
                    parts.Add(part);
                }
            }
        }

        foreach (var combinator in (string[])[ShaclVocabulary.And, ShaclVocabulary.Or, ShaclVocabulary.Xone, ShaclVocabulary.Not])
        {
            foreach (var value in context.Objects(key, combinator))
            {
                parts.Add(KindOf(combinator) + "(" + OperandList(model, context, combinator, value) + ")");
            }
        }

        var sparql = context.Objects(key, ShaclVocabulary.Sparql).Count > 0;
        return new ShaclRow(path, name, string.Join(", ", parts), cardinality, sparql, propertyShape is BlankTerm, severity);
    }

    private static string SummaryPart(
        RdfModel model,
        ProjectionContext context,
        string cardId,
        string rowPath,
        string parameter,
        RdfTerm value,
        Dictionary<string, string?> claimants,
        List<ShaclEdge> edges)
    {
        switch (parameter)
        {
            case ShaclVocabulary.Class when value is IriTerm classTerm:
                // The single-claimant rule: exactly one drawn card targeting the class draws the
                // constraint as an edge labeled by the row's path; otherwise it stays in the row.
                if (claimants.TryGetValue(classTerm.Iri, out var claimant) && claimant is not null && claimant != cardId)
                {
                    edges.Add(new ShaclEdge($"shacl-edge:{cardId}|class|{claimant}", cardId, claimant, "class", rowPath));
                    return "";
                }

                return "class " + RdfProjection.Display(model, classTerm);

            case ShaclVocabulary.Node when value is IriTerm:
                // A property shape's node reference: an edge from the containing card, labeled by
                // the path - the SHACL Play association convention.
                if (ShaclShapeDiscovery.KeyOf(value) is { } nodeKey)
                {
                    edges.Add(new ShaclEdge($"shacl-edge:{cardId}|node|{IdOf(nodeKey)}", cardId, IdOf(nodeKey), "node", rowPath));
                }

                return "";

            case ShaclVocabulary.Node:
                return "node " + InlineSummary(model, context, value, depth: 0);

            case ShaclVocabulary.Datatype when value is IriTerm datatype:
                return RdfProjection.Display(model, datatype);

            case ShaclVocabulary.NodeKind when value is IriTerm nodeKind:
                return nodeKind.Iri.StartsWith(ShaclVocabulary.Namespace, StringComparison.Ordinal)
                    ? nodeKind.Iri[ShaclVocabulary.Namespace.Length..]
                    : RdfProjection.Display(model, nodeKind);

            case ShaclVocabulary.In:
                return "in (" + context.ListMemberDisplays(model, value) + ")";

            case ShaclVocabulary.HasValue:
                return "has " + TermDisplay(model, value);

            case ShaclVocabulary.QualifiedValueShape:
                return "qualified " + (value is IriTerm ? TermDisplay(model, value) : InlineSummary(model, context, value, depth: 0));

            case ShaclVocabulary.Sparql:
                return "";

            default:
                var local = parameter[ShaclVocabulary.Namespace.Length..];
                return local + " " + TermDisplay(model, value);
        }
    }

    private static void CombinatorRow(
        RdfModel model,
        ProjectionContext context,
        RdfTerm listHead,
        string cardId,
        string kind,
        List<ShaclRow> rows,
        List<ShaclEdge> edges)
    {
        var hasBlankOperand = false;
        foreach (var member in context.ListMembers(listHead))
        {
            if (member is IriTerm && ShaclShapeDiscovery.KeyOf(member) is { } key)
            {
                edges.Add(new ShaclEdge($"shacl-edge:{cardId}|{kind}|{IdOf(key)}", cardId, IdOf(key), kind, kind));
            }
            else
            {
                hasBlankOperand = true;
            }
        }

        // The row exists only when an operand cannot be an edge: it lists every operand - the
        // IRI ones by name so the combination reads complete, the blank ones one level deep.
        if (hasBlankOperand)
        {
            rows.Add(new ShaclRow("", "", kind + "(" + OperandList(model, context, ShaclVocabulary.And, listHead) + ")", "", Sparql: false, Blank: true, ""));
        }
    }

    private static string OperandList(RdfModel model, ProjectionContext context, string combinator, RdfTerm value)
    {
        // sh:not takes one operand directly; the list combinators take an RDF list.
        var operands = combinator == ShaclVocabulary.Not ? [value] : context.ListMembers(value);
        return string.Join(", ", operands.Select(operand =>
            operand is IriTerm iri ? RdfProjection.Display(model, iri) : InlineSummary(model, context, operand, depth: 0)));
    }

    /// <summary>
    /// A blank operand's one-level constraint summary; anything nested deeper elides to an
    /// ellipsis, with the property grid carrying the full structure (design: inline summaries).
    /// </summary>
    private static string InlineSummary(RdfModel model, ProjectionContext context, RdfTerm operand, int depth)
    {
        if (depth > 0)
        {
            return "…";
        }

        if (ShaclShapeDiscovery.KeyOf(operand) is not { } key)
        {
            return "…";
        }

        var parts = new List<string>();
        foreach (var (triple, _) in context.TriplesOf(key))
        {
            var predicate = triple.Predicate.Iri;
            if (predicate == RdfVocabulary.Type)
            {
                continue;
            }

            if (!predicate.StartsWith(ShaclVocabulary.Namespace, StringComparison.Ordinal))
            {
                continue;
            }

            var local = predicate[ShaclVocabulary.Namespace.Length..];
            parts.Add(local + " " + (triple.Object is IriTerm or LiteralTerm ? TermDisplay(model, triple.Object) : "…"));
        }

        return "{ " + string.Join(", ", parts) + " }";
    }

    /// <summary>A <c>sh:path</c> value printed as SHACL path syntax, never expanded into plumbing.</summary>
    private static string PathSyntax(RdfModel model, ProjectionContext context, RdfTerm path, bool nested)
    {
        if (path is IriTerm iri)
        {
            return RdfProjection.Display(model, iri);
        }

        if (ShaclShapeDiscovery.KeyOf(path) is not { } key)
        {
            return "";
        }

        if (context.FirstObject(key, ShaclVocabulary.InversePath) is { } inverse)
        {
            return "^" + PathSyntax(model, context, inverse, nested: true);
        }

        if (context.FirstObject(key, ShaclVocabulary.AlternativePath) is { } alternatives)
        {
            var joined = string.Join("|", context.ListMembers(alternatives).Select(member => PathSyntax(model, context, member, nested: true)));
            return nested ? "(" + joined + ")" : joined;
        }

        if (context.FirstObject(key, ShaclVocabulary.ZeroOrMorePath) is { } zeroOrMore)
        {
            return PathSyntax(model, context, zeroOrMore, nested: true) + "*";
        }

        if (context.FirstObject(key, ShaclVocabulary.OneOrMorePath) is { } oneOrMore)
        {
            return PathSyntax(model, context, oneOrMore, nested: true) + "+";
        }

        if (context.FirstObject(key, ShaclVocabulary.ZeroOrOnePath) is { } zeroOrOne)
        {
            return PathSyntax(model, context, zeroOrOne, nested: true) + "?";
        }

        // A blank node with rdf:first plumbing is a sequence path.
        if (context.FirstObject(key, RdfVocabulary.First) is not null)
        {
            var joined = string.Join("/", context.ListMembers(path).Select(member => PathSyntax(model, context, member, nested: true)));
            return nested ? "(" + joined + ")" : joined;
        }

        return "";
    }

    private static string TermDisplay(RdfModel model, RdfTerm term) => term switch
    {
        IriTerm iri => RdfProjection.Display(model, iri),
        LiteralTerm literal => literal.Lexical,
        BlankTerm => "…",
        _ => "",
    };

    private static string KindOf(string combinator) => combinator[ShaclVocabulary.Namespace.Length..];

    /// <summary>The one-pass indexes the projection reads instead of rescanning triples.</summary>
    private sealed class ProjectionContext
    {
        private readonly Dictionary<string, List<(RdfTriple Triple, int Index)>> _bySubject = new(StringComparer.Ordinal);

        public HashSet<string> SubjectKeys { get; } = new(StringComparer.Ordinal);
        public HashSet<string> PropertyReferenced { get; } = new(StringComparer.Ordinal);
        public HashSet<string> NodeReferenced { get; } = new(StringComparer.Ordinal);
        public HashSet<string> OperandReferenced { get; } = new(StringComparer.Ordinal);

        public static ProjectionContext Build(RdfModel model, IReadOnlyList<ShaclShape> shapes)
        {
            var context = new ProjectionContext();
            for (var index = 0; index < model.Triples.Count; index++)
            {
                var triple = model.Triples[index];
                if (ShaclShapeDiscovery.KeyOf(triple.Subject) is { } subjectKey)
                {
                    context.SubjectKeys.Add(subjectKey);
                    if (!context._bySubject.TryGetValue(subjectKey, out var list))
                    {
                        context._bySubject[subjectKey] = list = [];
                    }

                    list.Add((triple, index));
                }

                if (ShaclShapeDiscovery.KeyOf(triple.Object) is not { } objectKey)
                {
                    continue;
                }

                switch (triple.Predicate.Iri)
                {
                    case ShaclVocabulary.Property:
                        context.PropertyReferenced.Add(objectKey);
                        break;
                    case ShaclVocabulary.Node:
                        context.NodeReferenced.Add(objectKey);
                        break;
                    case ShaclVocabulary.Not or ShaclVocabulary.QualifiedValueShape:
                        context.OperandReferenced.Add(objectKey);
                        break;
                }
            }

            // Combinator list members are operands too, reached through the cons plumbing.
            foreach (var shape in shapes)
            {
                foreach (var (triple, _) in context.TriplesOf(shape.Key))
                {
                    if (!ShaclVocabulary.ShapeListPredicates.Contains(triple.Predicate.Iri))
                    {
                        continue;
                    }

                    foreach (var member in context.ListMembers(triple.Object))
                    {
                        if (ShaclShapeDiscovery.KeyOf(member) is { } memberKey)
                        {
                            context.OperandReferenced.Add(memberKey);
                        }
                    }
                }
            }

            return context;
        }

        public IReadOnlyList<(RdfTriple Triple, int Index)> TriplesOf(string subjectKey) =>
            _bySubject.TryGetValue(subjectKey, out var list) ? list : [];

        public IReadOnlyList<RdfTerm> Objects(string subjectKey, string predicateIri) =>
            [.. TriplesOf(subjectKey).Where(entry => entry.Triple.Predicate.Iri == predicateIri).Select(entry => entry.Triple.Object)];

        public RdfTerm? FirstObject(string subjectKey, string predicateIri)
        {
            foreach (var (triple, _) in TriplesOf(subjectKey))
            {
                if (triple.Predicate.Iri == predicateIri)
                {
                    return triple.Object;
                }
            }

            return null;
        }

        public string FirstLiteral(string subjectKey, string predicateIri) =>
            FirstObject(subjectKey, predicateIri) is LiteralTerm literal ? literal.Lexical : "";

        public IReadOnlyList<RdfTerm> ListMembers(RdfTerm head)
        {
            var members = new List<RdfTerm>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var current = head;
            while (current is not IriTerm { Iri: RdfVocabulary.Nil })
            {
                if (ShaclShapeDiscovery.KeyOf(current) is not { } key || !visited.Add(key))
                {
                    break;
                }

                if (FirstObject(key, RdfVocabulary.First) is { } member)
                {
                    members.Add(member);
                }

                if (FirstObject(key, RdfVocabulary.Rest) is not { } rest)
                {
                    break;
                }

                current = rest;
            }

            return members;
        }

        public string ListMemberDisplays(RdfModel model, RdfTerm head) =>
            string.Join(", ", ListMembers(head).Select(member => TermDisplay(model, member)));

        public IEnumerable<string> TargetedClasses(ShaclShape shape)
        {
            foreach (var (triple, _) in TriplesOf(shape.Key))
            {
                if (triple.Predicate.Iri == ShaclVocabulary.TargetClass && triple.Object is IriTerm classTerm)
                {
                    yield return classTerm.Iri;
                }
            }

            if (shape is { ImplicitClassTarget: true, Term: IriTerm iri })
            {
                yield return iri.Iri;
            }
        }
    }
}
