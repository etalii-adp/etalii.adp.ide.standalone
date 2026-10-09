namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// Pure: model in, drawn elements out - the variable-as-node projection that is this module's
/// center of gravity. One node per variable name however many patterns mention it, so a node's
/// degree on the canvas is its join count in the query; one node per distinct concrete term
/// form, which asserts "same term" and never a join; and one placement rule for all of them:
/// <b>every node lives at the shallowest scope that references it, and region edges reach out to
/// it</b> - the border crossing is the join being shown, not a rendering defect.
/// </summary>
public static class SparqlProjection
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
}
