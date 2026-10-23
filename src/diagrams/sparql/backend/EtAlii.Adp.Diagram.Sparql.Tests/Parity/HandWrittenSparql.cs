using System.Globalization;
using EtAlii.Adp.Context;

namespace EtAlii.Adp.Diagram.Sparql.Tests.Parity;

/// <summary>
/// The drawn query as the module projected it by hand before it was derived from the DISL definition
/// (sparql-disl-fbl, task 7), kept verbatim as the oracle the derived drawing is compared with, document by
/// document and element by element (<see cref="SparqlDerivedDrawingTests"/>); and the rows, findings and refusals the
/// two providers, the validator and the session gave by hand before task 8 derived them, the oracle of
/// <see cref="SparqlDerivedRowsTests"/>, <see cref="SparqlDerivedFindingsTests"/> and <see cref="SparqlDerivedRefusalsTests"/>.
/// </summary>
/// <remarks>
/// Pure: model in, drawn elements out - the variable-as-node projection that is this module's
/// center of gravity. One node per variable name however many patterns mention it, so a node's
/// degree on the canvas is its join count in the query; one node per distinct concrete term
/// form, which asserts "same term" and never a join; and one placement rule for all of them:
/// <b>every node lives at the shallowest scope that references it, and region edges reach out to
/// it</b> - the border crossing is the join being shown, not a rendering defect.
/// </remarks>
internal static class HandWrittenSparql
{
    /// <summary>
    /// The local sanity bound on drawn elements - this module's own number, deliberately not the
    /// RDF family's drawn-element budget: hand-written queries hold tens of patterns, and this
    /// bound exists only so a generated monster degrades honestly (Requirement 7.5).
    /// </summary>
    public const int SanityBound = 500;

    /// <summary>What <paramref name="model"/> draws, in deterministic order.</summary>
    public static SparqlProjectionResult Project(SparqlQueryModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new Builder(model).Build();
    }

    private sealed class Builder(SparqlQueryModel model)
    {
        private readonly List<SparqlNode> _nodes = [];
        private readonly List<SparqlEdge> _edges = [];
        private readonly List<SparqlRegion> _regions = [];
        private readonly List<SparqlAnnotation> _annotations = [];
        private readonly Dictionary<string, int> _nodeIndexById = [];
        private readonly Dictionary<string, List<string>> _termScopesById = [];
        private readonly Dictionary<string, int> _edgeOrdinals = [];

        public SparqlProjectionResult Build()
        {
            // First pass: where every term is mentioned, so placement can find the shallowest
            // scope before any node is created.
            CollectTermScopes(model.Where);
            if (model.Template is not null)
            {
                CollectTermScopes(model.Template);
            }

            // Regions frame the tree; they come first so a region always precedes its contents.
            CollectRegions(model.Where, parentRegionId: "");
            if (model.Template is not null)
            {
                _regions.Add(new SparqlRegion(
                    RegionId(model.Template.Path),
                    "template",
                    "CONSTRUCT",
                    "",
                    model.Template.Path));
                CollectRegions(model.Template, RegionId(model.Template.Path));
            }

            // Every named variable gets its one node - including a variable mentioned only in
            // predicate position or only by a subquery's projection, which would otherwise
            // vanish from the canvas despite being part of the query's join surface.
            foreach (var usage in OrderedUsages())
            {
                AddNode(new SparqlNode(
                    $"var:{usage.Name}",
                    SparqlNodeKind.Variable,
                    $"?{usage.Name}",
                    PlaceAt(usage.ScopePaths),
                    Projected: usage.Projected,
                    JoinCount: usage.PatternOccurrences,
                    DefiningExpression: usage.DefiningExpression));
            }

            // A DESCRIBE names its targets outright, and for a bare `DESCRIBE <iri>` those are
            // the whole query - so a target draws as a node like any other term. Found by the
            // vendored corpus: without this, a real one-line DESCRIBE drew nothing at all.
            foreach (var term in model.DescribeTerms)
            {
                EnsureTermNode(term);
            }

            // Patterns: concrete and anonymous nodes on first mention, then the edges.
            CollectPatterns(model.Where);
            if (model.Template is not null)
            {
                CollectPatterns(model.Template);
            }

            CollectSubSelects(model.Where);
            CollectAnnotations(model.Where);

            var headerForm = HeaderForm();
            var headerRows = model.DatasetClauses.Concat(model.ModifierRows).ToList();

            return Bound(headerForm, headerRows);
        }

        // -- Placement: the one rule --------------------------------------------------------------

        /// <summary>
        /// The shallowest scope that references the term: the longest common ancestor of every
        /// mentioning scope - lifted out of a UNION, because a name shared by branches joins at
        /// the scope that joins the branches (Requirement 4.5).
        /// </summary>
        private static string PlaceAt(IReadOnlyList<string> scopePaths)
        {
            if (scopePaths.Count == 0)
            {
                return "where";
            }

            var common = scopePaths[0].Split('/');
            foreach (var path in scopePaths.Skip(1))
            {
                var segments = path.Split('/');
                var length = 0;
                while (length < common.Length && length < segments.Length && common[length] == segments[length])
                {
                    length++;
                }

                common = common[..length];
            }

            // A union scope holds branches, not nodes: a node landing on the union itself lifts
            // to the union's parent, where the joined name actually binds.
            while (common.Length > 1 && common[^1].StartsWith("union.", StringComparison.Ordinal))
            {
                common = common[..^1];
            }

            return common.Length == 0 ? "where" : string.Join('/', common);
        }

        // -- Collection passes ---------------------------------------------------------------------

        private void CollectTermScopes(GroupScope scope)
        {
            foreach (var pattern in scope.Patterns)
            {
                foreach (var term in (SparqlTerm[])[pattern.Subject, pattern.Object])
                {
                    var id = NodeId(term);
                    if (id.Length == 0)
                    {
                        continue;
                    }

                    if (!_termScopesById.TryGetValue(id, out var scopes))
                    {
                        scopes = [];
                        _termScopesById[id] = scopes;
                    }

                    if (!scopes.Contains(scope.Path))
                    {
                        scopes.Add(scope.Path);
                    }
                }
            }

            foreach (var child in scope.Children)
            {
                CollectTermScopes(child);
            }
        }

        /// <summary>Every non-root scope is a region; the root where group is the open canvas.</summary>
        private void CollectRegions(GroupScope scope, string parentRegionId)
        {
            foreach (var child in scope.Children)
            {
                var region = new SparqlRegion(
                    RegionId(child.Path),
                    child.Kind.ToString().ToLowerInvariant(),
                    RegionLabel(child),
                    parentRegionId,
                    child.Path);
                _regions.Add(region);
                CollectRegions(child, region.Id);
            }
        }

        private static string RegionLabel(GroupScope scope) => scope.Kind switch
        {
            GroupScopeKind.Optional => "OPTIONAL",
            GroupScopeKind.Union => "UNION",
            GroupScopeKind.Branch => "",
            GroupScopeKind.Minus => "MINUS",
            GroupScopeKind.Graph => $"GRAPH {scope.Label}",
            GroupScopeKind.Service => $"SERVICE {scope.Label}",
            GroupScopeKind.Template => "CONSTRUCT",
            _ => "",
        };

        private void CollectPatterns(GroupScope scope)
        {
            foreach (var pattern in scope.Patterns)
            {
                var fromId = EnsureTermNode(pattern.Subject);
                var toId = EnsureTermNode(pattern.Object);
                var label = pattern.Predicate.AsWritten;
                var isPath = pattern.Predicate is PathTerm;

                var edgeKey = $"{fromId}|{label}|{toId}";
                var ordinal = _edgeOrdinals.TryGetValue(edgeKey, out var seen) ? seen : 0;
                _edgeOrdinals[edgeKey] = ordinal + 1;

                _edges.Add(new SparqlEdge(
                    $"edge:{fromId}|{label}|{toId}|{ordinal}",
                    fromId,
                    toId,
                    label,
                    isPath,
                    scope.Path));
            }

            foreach (var child in scope.Children)
            {
                CollectPatterns(child);
            }
        }

        private void CollectSubSelects(GroupScope scope)
        {
            foreach (var subSelect in scope.SubSelects)
            {
                var id = $"sub:{scope.Path}.{subSelect.Ordinal}";
                AddNode(new SparqlNode(
                    id,
                    SparqlNodeKind.SubSelect,
                    subSelect.ProjectionText,
                    scope.Path,
                    Full: subSelect.Text));

                // The join surface: one labeled edge per projected name, from the collapsed node
                // to the variable it feeds (Requirement 3.6).
                foreach (var name in subSelect.ProjectedNames)
                {
                    _edges.Add(new SparqlEdge(
                        $"edge:{id}|{name}|var:{name}|0",
                        id,
                        $"var:{name}",
                        $"?{name}",
                        IsPath: false,
                        scope.Path));
                }
            }

            foreach (var child in scope.Children)
            {
                CollectSubSelects(child);
            }
        }

        private void CollectAnnotations(GroupScope scope)
        {
            var kindCounts = new Dictionary<string, int>();
            foreach (var constraint in scope.Constraints)
            {
                var kind = constraint.Kind.ToString().ToLowerInvariant();
                var ordinal = kindCounts.TryGetValue(kind, out var seen) ? seen : 0;
                kindCounts[kind] = ordinal + 1;

                var attachedTo = constraint.Kind switch
                {
                    // A BIND anchors to the variable it defines; VALUES to the first variable it
                    // feeds; a FILTER to the region it constrains - floating at the root, whose
                    // "region" is the open canvas.
                    SparqlConstraintKind.Bind => $"var:{constraint.DefinedVariable}",
                    SparqlConstraintKind.Values when constraint.ReferencedVariables.Count > 0 =>
                        $"var:{constraint.ReferencedVariables[0]}",
                    _ => scope.Path.Contains('/') ? RegionId(scope.Path) : "",
                };

                _annotations.Add(new SparqlAnnotation(
                    $"note:{scope.Path}/{kind}.{ordinal}",
                    kind,
                    constraint.Text,
                    attachedTo,
                    scope.Path));
            }

            foreach (var child in scope.Children)
            {
                CollectAnnotations(child);
            }
        }

        // -- Nodes ------------------------------------------------------------------------------------

        private void AddNode(SparqlNode node)
        {
            if (_nodeIndexById.ContainsKey(node.Id))
            {
                return;
            }

            _nodeIndexById[node.Id] = _nodes.Count;
            _nodes.Add(node);
        }

        /// <summary>The node id for a subject or object term, creating the node on first mention.</summary>
        private string EnsureTermNode(SparqlTerm term)
        {
            var id = NodeId(term);
            if (_nodeIndexById.ContainsKey(id))
            {
                return id;
            }

            var node = term switch
            {
                VariableTerm variable => new SparqlNode(
                    // A variable node normally exists already from the index pass; this covers a
                    // defensive fallback only.
                    id, SparqlNodeKind.Variable, $"?{variable.Name}", "where"),
                AnonymousTerm anonymous => new SparqlNode(
                    id, SparqlNodeKind.Anonymous, anonymous.AsWritten, PlaceForTerm(id)),
                IriTerm iri => new SparqlNode(
                    id, SparqlNodeKind.Iri, iri.AsWritten, PlaceForTerm(id), Full: iri.Iri),
                LiteralTerm literal => new SparqlNode(
                    id, SparqlNodeKind.Literal, literal.Lexical, PlaceForTerm(id),
                    Full: literal.AsWritten, Annotation: LiteralAnnotation(literal)),
                _ => throw new InvalidOperationException($"A {term.GetType().Name} cannot be drawn as a node."),
            };

            AddNode(node);
            return id;
        }

        private string PlaceForTerm(string id) =>
            _termScopesById.TryGetValue(id, out var scopes) ? PlaceAt(scopes) : "where";

        private static string LiteralAnnotation(LiteralTerm literal)
        {
            if (literal.Language.Length > 0)
            {
                return $"@{literal.Language}";
            }

            return literal.DatatypeIri.Length > 0
                ? ShortDatatype(literal.DatatypeIri)
                : "";
        }

        private static string ShortDatatype(string iri)
        {
            var hash = iri.LastIndexOf('#');
            if (hash >= 0 && hash < iri.Length - 1)
            {
                return iri[(hash + 1)..];
            }

            var slash = iri.LastIndexOf('/');
            return slash >= 0 && slash < iri.Length - 1 ? iri[(slash + 1)..] : iri;
        }

        /// <summary>The stable, space-free element id a term keys layout and selection by (Requirement 5.2).</summary>
        private static string NodeId(SparqlTerm term) => term switch
        {
            VariableTerm variable => $"var:{variable.Name}",
            AnonymousTerm anonymous => $"anon:{anonymous.Ordinal}",
            IriTerm iri => $"iri:{iri.Iri}",
            LiteralTerm literal =>
                $"lit:{Uri.EscapeDataString(literal.Lexical)}"
                + (literal.Language.Length > 0 ? $"@{literal.Language}" : "")
                + (literal.DatatypeIri.Length > 0 ? $"^{Uri.EscapeDataString(ShortDatatype(literal.DatatypeIri))}" : ""),
            _ => "",
        };

        private static string RegionId(string scopePath) => $"region:{scopePath}";

        // -- Ordering, header, bound ---------------------------------------------------------------------

        private IEnumerable<SparqlVariableUsage> OrderedUsages() =>
            // Deterministic without depending on dictionary order: by first scope mention, then
            // name - the same model always yields the same node sequence.
            model.Variables.Values
                .OrderBy(usage => usage.ScopePaths.Count > 0 ? usage.ScopePaths[0] : "")
                .ThenBy(usage => usage.Name, StringComparer.Ordinal);

        private string HeaderForm()
        {
            var form = model.Form switch
            {
                SparqlQueryForm.Select => "SELECT",
                SparqlQueryForm.Construct => "CONSTRUCT",
                SparqlQueryForm.Ask => "ASK",
                SparqlQueryForm.Describe => $"DESCRIBE {string.Join(' ', model.DescribeTargets)}",
                _ => "",
            };

            if (model.Distinct)
            {
                form += " DISTINCT";
            }

            if (model.Reduced)
            {
                form += " REDUCED";
            }

            return form;
        }

        private SparqlProjectionResult Bound(string headerForm, List<string> headerRows)
        {
            var total = _regions.Count + _nodes.Count + _edges.Count + _annotations.Count;
            if (total <= SanityBound)
            {
                return new SparqlProjectionResult(_nodes, _edges, _regions, _annotations, headerForm, headerRows, total, total);
            }

            // The cut takes regions, then nodes, then edges, then annotations, in their
            // deterministic order, and drops any edge whose endpoint fell past the bound - an
            // honest first-N, never a hang (Requirement 7.5).
            var remaining = SanityBound;
            var regions = Take(_regions, ref remaining);
            var nodes = Take(_nodes, ref remaining);
            var keptNodeIds = nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
            var edges = Take(_edges, ref remaining)
                .Where(edge => keptNodeIds.Contains(edge.FromId) && keptNodeIds.Contains(edge.ToId))
                .ToList();
            var annotations = Take(_annotations, ref remaining);

            var shown = regions.Count + nodes.Count + edges.Count + annotations.Count;
            return new SparqlProjectionResult(nodes, edges, regions, annotations, headerForm, headerRows, shown, total);
        }

        private static List<T> Take<T>(List<T> source, ref int remaining)
        {
            var taken = source.Take(remaining).ToList();
            remaining -= taken.Count;
            return taken;
        }
    }

    // The surfaces beside the drawing, as the module answered them by hand before they were derived from the
    // definition (sparql-disl-fbl, task 8): the property rows of the two providers, the findings of the validator and
    // the refusals of the session and the action provider, each copied verbatim.

    /// <summary>The reason every row carried: one sentence, the same everywhere.</summary>
    public const string ReadOnlyReason =
        "This diagram reads the query; edit the .rq file in a text editor and the diagram follows.";

    /// <summary>What an action arriving anyway was told.</summary>
    public const string NoActionsReason =
        "A query diagram offers no edits: it reads the .rq file, which is edited in a text editor.";

    /// <summary>What a drag onto another parent was told, whatever was dragged.</summary>
    public const string ParentRefusal =
        "A query's structure comes from its text, so nothing here can be moved into a different group. Dragging changes where an element sits on the canvas.";

    /// <summary>The rule reported when the file is not a SPARQL query this can read - SPARQL Update included.</summary>
    public const string UnparseableRuleId = "sparql.unparseable";

    /// <summary>The rule reported when a projected variable never appears in the where clause.</summary>
    public const string UnboundProjectionRuleId = "sparql.unbound-projection";

    /// <summary>The rule reported when a prefix is declared but never used.</summary>
    public const string UnusedPrefixRuleId = "sparql.unused-prefix";

    /// <summary>A variable's name.</summary>
    public const string VariableNameProperty = "sparql.variable.name";

    /// <summary>Whether the projection carries the variable outward.</summary>
    public const string VariableProjectedProperty = "sparql.variable.projected";

    /// <summary>How many triple-pattern positions mention the variable - its degree on the canvas.</summary>
    public const string VariableJoinCountProperty = "sparql.variable.joins";

    /// <summary>The expression that defines the variable, as written.</summary>
    public const string VariableDefinitionProperty = "sparql.variable.definition";

    private const string TermDisplayProperty = "sparql.term.display";
    private const string TermFullProperty = "sparql.term.full";
    private const string TermAnnotationProperty = "sparql.term.annotation";

    /// <summary>A region's construct kind.</summary>
    public const string RegionKindProperty = "sparql.region.kind";

    /// <summary>A region's label - its graph term or service endpoint where it has one.</summary>
    public const string RegionLabelProperty = "sparql.region.label";

    private const string EdgeLabelProperty = "sparql.edge.label";
    private const string AnnotationTextProperty = "sparql.annotation.text";

    /// <summary>The query's form.</summary>
    public const string QueryFormProperty = "sparql.query.form";

    /// <summary>The query's solution modifiers and dataset clauses.</summary>
    public const string QueryModifiersProperty = "sparql.query.modifiers";

    private const string SubQueryTextProperty = "sparql.subquery.text";

    private const string IdentityGroup = "Identity";
    private const string StructureGroup = "Structure";
    private const string QueryGroup = "Query";

    /// <summary>The header band's id on the wire.</summary>
    public const string HeaderId = "header:query";

    /// <summary>The truncation banner's id on the wire.</summary>
    public const string TruncationId = "truncation";

    /// <summary>The rows the property provider gave for <paramref name="elementId"/> of a query that parsed, drawn as <paramref name="projection"/>.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(SparqlProjectionResult projection, string elementId)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (elementId == HeaderId)
        {
            return
            [
                Row(QueryFormProperty, "Form", projection.HeaderForm, QueryGroup),
                Row(QueryModifiersProperty, "Modifiers", string.Join(" · ", projection.HeaderRows), QueryGroup),
            ];
        }

        if (SparqlSelection.NodeOf(projection, elementId) is { } node)
        {
            return RowsFor(node);
        }

        if (SparqlSelection.RegionOf(projection, elementId) is { } region)
        {
            return
            [
                Row(RegionKindProperty, "Group", region.Kind.ToUpperInvariant(), StructureGroup),
                Row(RegionLabelProperty, "Constraint", region.Label, StructureGroup),
            ];
        }

        if (SparqlSelection.EdgeOf(projection, elementId) is { } edge)
        {
            return [Row(EdgeLabelProperty, edge.IsPath ? "Property path" : "Predicate", edge.Label, StructureGroup)];
        }

        if (SparqlSelection.AnnotationOf(projection, elementId) is { } annotation)
        {
            return [Row(AnnotationTextProperty, annotation.Kind.ToUpperInvariant(), annotation.Text, StructureGroup)];
        }

        return [];
    }

    private static IReadOnlyList<ContextPropertyDefinition> RowsFor(SparqlNode node) => node.Kind switch
    {
        SparqlNodeKind.Variable or SparqlNodeKind.Anonymous =>
        [
            Row(VariableNameProperty, node.Kind == SparqlNodeKind.Anonymous ? "Anonymous variable" : "Variable", node.Display, IdentityGroup),
            Row(VariableProjectedProperty, "Projected", node.Projected ? "Yes" : "No", IdentityGroup),
            Row(VariableJoinCountProperty, "Joins", node.JoinCount.ToString(CultureInfo.InvariantCulture), StructureGroup),
            Row(VariableDefinitionProperty, "Defined by", node.DefiningExpression, StructureGroup),
        ],
        SparqlNodeKind.SubSelect =>
        [
            Row(TermDisplayProperty, "Projection", node.Display, IdentityGroup),
            Row(SubQueryTextProperty, "Subquery", node.Full, QueryGroup),
        ],
        _ =>
        [
            Row(TermDisplayProperty, node.Kind == SparqlNodeKind.Iri ? "Prefixed name" : "Literal", node.Display, IdentityGroup),
            Row(TermFullProperty, node.Kind == SparqlNodeKind.Iri ? "IRI" : "As written", node.Full, IdentityGroup),
            Row(TermAnnotationProperty, "Type", node.Annotation, IdentityGroup),
        ],
    };

    private static ContextPropertyDefinition Row(string id, string label, string value, string group) =>
        new(id, label, value, ReadOnlyReason: ReadOnlyReason, Group: group);

    /// <summary>The validator's findings over <paramref name="text"/>: the one unparseable finding alone, else the unbound projections and the unused prefixes.</summary>
    public static IReadOnlyList<DiagramProblem> Findings(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var outcome = SparqlParser.Outcome(text);
        if (!outcome.IsUsable)
        {
            var problem = outcome.Problems[0];
            return
            [
                new DiagramProblem(
                    DiagramProblemSeverity.Error,
                    $"This is not a SPARQL query that can be read: {problem.Message}",
                    UnparseableRuleId,
                    new DiagramProblemLineLocation((uint)problem.Line + 1)),
            ];
        }

        return Judge(outcome.Model, text);
    }

    /// <summary>The findings over a parsed query.</summary>
    public static IReadOnlyList<DiagramProblem> Judge(SparqlQueryModel model, string text)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(text);
        var problems = new List<DiagramProblem>();
        foreach (var item in model.Projection)
        {
            if (!model.Variables.TryGetValue(item.Name, out var usage))
            {
                continue;
            }

            if (usage is { PatternOccurrences: 0, DefiningExpression.Length: 0 })
            {
                problems.Add(new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"?{item.Name} is selected but never appears in the where clause, so its column will always be unbound.",
                    UnboundProjectionRuleId,
                    new DiagramProblemLineLocation(1)));
            }
        }

        foreach (var prefix in model.Prefixes)
        {
            if (!IsUsed(prefix.Prefix, text))
            {
                problems.Add(new DiagramProblem(
                    DiagramProblemSeverity.Info,
                    $"The prefix '{prefix.Prefix}:' is declared but never used.",
                    UnusedPrefixRuleId,
                    new DiagramProblemLineLocation((uint)Math.Max(prefix.Line, 1))));
            }
        }

        return problems;
    }

    private static bool IsUsed(string prefix, string text)
    {
        var needle = prefix + ":";
        var from = 0;

        while (true)
        {
            var at = text.IndexOf(needle, from, StringComparison.Ordinal);
            if (at < 0)
            {
                return false;
            }

            from = at + needle.Length;
            var lineStart = text.LastIndexOf('\n', Math.Max(at - 1, 0)) + 1;
            var beforeOnLine = text[lineStart..at].TrimStart();
            if (beforeOnLine.StartsWith("PREFIX", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return true;
        }
    }

    /// <summary>Why the session refused to store a position for <paramref name="elementId"/>, or empty when it stored one.</summary>
    public static string MoveRefusal(string elementId)
    {
        ArgumentNullException.ThrowIfNull(elementId);
        if (elementId.StartsWith("anon:", StringComparison.Ordinal))
        {
            return "That is an anonymous variable - written as a blank node, it has no name to key a stored position by, so it takes its computed place.";
        }

        if (elementId.StartsWith("edge:", StringComparison.Ordinal))
        {
            return "An edge is drawn between its endpoints; move one of those instead.";
        }

        if (elementId.StartsWith("note:", StringComparison.Ordinal))
        {
            return "An annotation stays with what it constrains; move that instead.";
        }

        return elementId switch
        {
            HeaderId => "The header band states the query's form and modifiers, and stays at the top of the diagram.",
            TruncationId => "That banner reports the truncation and is not something this diagram can move.",
            _ => "",
        };
    }
}
