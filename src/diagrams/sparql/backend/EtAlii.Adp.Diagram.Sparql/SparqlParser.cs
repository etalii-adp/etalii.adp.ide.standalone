using EtAlii.Adp.Documents;
using Serilog;

namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// A hand-rolled recursive-descent parser over the SPARQL 1.1 Query grammar, holding the design's
/// three tiers exactly. <b>Parsed structurally</b>: the prologue, the four query forms and their
/// clause shapes, the group-pattern tree, triple patterns with their lists and blank-node and
/// collection syntax, solution modifiers and <c>VALUES</c>. <b>Recognized but kept as written</b>:
/// every expression and every property path - brackets and strings are matched so the exact
/// source text can be sliced and the variable references inside found, and no expression tree is
/// ever built, because the requirements draw expressions as annotations showing what the author
/// wrote. <b>Refused by name</b>: a SPARQL Update document, recognized and then declined with the
/// reason, because an update is a write instruction rather than a question.
/// </summary>
/// <remarks>
/// This is deliberately not the RDF family's parser and shares nothing with it: a query is not a
/// serialization of a graph. Token positions serve error lines and text slicing only - there is
/// no writer anywhere in this module for them to serve.
/// </remarks>
public static class SparqlParser
{
    private static readonly ILogger _logger = Log.ForContext(typeof(SparqlParser));

    private static readonly string[] _updateVerbs =
        ["INSERT", "DELETE", "LOAD", "CLEAR", "CREATE", "DROP", "COPY", "MOVE", "ADD", "WITH"];

    /// <summary>What <paramref name="text"/> asks, or a <see cref="SparqlParseException"/> saying why it could not be read.</summary>
    public static SparqlQueryModel Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new Parser(text).ParseQuery();
    }

    /// <summary>
    /// What <paramref name="text"/> asks, as a <see cref="ParseOutcome{TModel}"/>: a query that does
    /// not parse is an ordinary state for a file somebody is editing, so it is an unusable outcome with
    /// an empty model, the parser's sentence and the line it stopped at, rather than an exception.
    /// </summary>
    public static ParseOutcome<SparqlQueryModel> Outcome(string text)
    {
        try
        {
            return new ParseOutcome<SparqlQueryModel>(Parse(text), []);
        }
        catch (SparqlParseException exception)
        {
            _logger.Debug(exception, "A query does not parse at line {Line}", exception.Line);
            return new ParseOutcome<SparqlQueryModel>(
                SparqlQueryModel.Empty,
                [new ParseProblem(Math.Max(exception.Line, 1) - 1, exception.Message)],
                exception.Message);
        }
    }

    private sealed class Parser
    {
        private readonly SparqlTokenCursor _cursor;

        private readonly List<SparqlPrefixDeclaration> _prefixes = [];
        private readonly Dictionary<string, string> _prefixMap = [];
        private readonly Dictionary<string, AnonymousTerm> _labeledAnonymous = [];
        private string _baseIri = "";
        private int _anonymousOrdinal;

        public Parser(string text) => _cursor = new SparqlTokenCursor(text);

        public SparqlQueryModel ParseQuery()
        {
            ParsePrologue();

            var first = Current;
            if (first.Kind == SparqlTokenKind.EndOfFile)
            {
                throw Error("The file holds no query: it ends after the prologue.", first);
            }

            foreach (var verb in _updateVerbs)
            {
                if (first.IsKeyword(verb))
                {
                    throw Error(
                        $"This is a SPARQL Update document ({verb.ToUpperInvariant()} ...), and SPARQL Update is out of scope: "
                        + "an update is a write instruction, not a question, and this diagram draws questions. "
                        + "Update documents conventionally use the .ru extension.",
                        first);
                }
            }

            SparqlQueryModel model;
            if (first.IsKeyword("SELECT"))
            {
                model = ParseSelectQuery();
            }
            else if (first.IsKeyword("CONSTRUCT"))
            {
                model = ParseConstructQuery();
            }
            else if (first.IsKeyword("ASK"))
            {
                model = ParseAskQuery();
            }
            else if (first.IsKeyword("DESCRIBE"))
            {
                model = ParseDescribeQuery();
            }
            else
            {
                throw Error($"Expected a query form (SELECT, CONSTRUCT, ASK or DESCRIBE), found '{first.Value}'.", first);
            }

            return Current.Kind != SparqlTokenKind.EndOfFile
                ? throw Error($"The query is over, but '{Current.Value}' follows it.", Current)
                : model;
        }

        // -- The prologue -------------------------------------------------------------------

        private void ParsePrologue()
        {
            while (true)
            {
                if (Current.IsKeyword("PREFIX"))
                {
                    var line = _cursor.LineOf(Current);
                    var declaration = _cursor.Index;
                    Advance();
                    var name = _cursor.Expect(SparqlTokenKind.PrefixedName, "a prefix name ending in ':'");
                    var colon = name.Value.IndexOf(':');
                    if (colon != name.Value.Length - 1)
                    {
                        throw Error($"A PREFIX declaration names the prefix alone, ending in ':' - found '{name.Value}'.", name);
                    }

                    var iri = _cursor.Expect(SparqlTokenKind.Iri, "the prefix's <IRI>");
                    var prefix = name.Value[..colon];
                    var expanded = Unbracket(iri.Value);
                    _prefixMap[prefix] = expanded;
                    _prefixes.Add(new SparqlPrefixDeclaration(prefix, expanded, line) { Span = _cursor.SpanOf(declaration, _cursor.Index - 1) });
                }
                else if (Current.IsKeyword("BASE"))
                {
                    Advance();
                    var iri = _cursor.Expect(SparqlTokenKind.Iri, "the base <IRI>");
                    _baseIri = Unbracket(iri.Value);
                }
                else
                {
                    return;
                }
            }
        }

        // -- The four forms -----------------------------------------------------------------

        private SparqlQueryModel ParseSelectQuery()
        {
            Advance(); // SELECT

            var distinct = false;
            var reduced = false;
            if (Current.IsKeyword("DISTINCT"))
            {
                distinct = true;
                Advance();
            }
            else if (Current.IsKeyword("REDUCED"))
            {
                reduced = true;
                Advance();
            }

            (bool projectsAll, List<SparqlProjectionItem> projection) = ParseProjection();
            var datasets = ParseDatasetClauses();
            var where = ParseWhere();
            (List<string> modifiers, SparqlConstraint? values) = ParseSolutionModifiers();
            if (values is not null)
            {
                where.Constraints.Add(values);
            }

            return Finish(new SparqlQueryModel
            {
                Form = SparqlQueryForm.Select,
                Distinct = distinct,
                Reduced = reduced,
                ProjectsAll = projectsAll,
                Projection = projection,
                DatasetClauses = datasets,
                Where = where,
                ModifierRows = modifiers,
            });
        }

        private SparqlQueryModel ParseConstructQuery()
        {
            Advance(); // CONSTRUCT

            GroupScope? template = null;
            if (Current.IsPunct("{"))
            {
                template = ParseGroupGraphPattern(GroupScopeKind.Template, 0);
            }

            var datasets = ParseDatasetClauses();
            var where = ParseWhere();
            if (template is null)
            {
                // The short form, CONSTRUCT WHERE { ... }: the template is the pattern itself.
                template = new GroupScope { Kind = GroupScopeKind.Template, Ordinal = 0, Span = where.Span };
                template.Patterns.AddRange(where.Patterns);
            }

            (List<string> modifiers, SparqlConstraint? values) = ParseSolutionModifiers();
            if (values is not null)
            {
                where.Constraints.Add(values);
            }

            return Finish(new SparqlQueryModel
            {
                Form = SparqlQueryForm.Construct,
                DatasetClauses = datasets,
                Template = template,
                Where = where,
                ModifierRows = modifiers,
            });
        }

        private SparqlQueryModel ParseAskQuery()
        {
            Advance(); // ASK
            var datasets = ParseDatasetClauses();
            var where = ParseWhere();
            (List<string> modifiers, SparqlConstraint? values) = ParseSolutionModifiers();
            if (values is not null)
            {
                where.Constraints.Add(values);
            }

            return Finish(new SparqlQueryModel
            {
                Form = SparqlQueryForm.Ask,
                DatasetClauses = datasets,
                Where = where,
                ModifierRows = modifiers,
            });
        }

        private SparqlQueryModel ParseDescribeQuery()
        {
            Advance(); // DESCRIBE

            var targets = new List<string>();
            var terms = new List<SparqlTerm>();
            var projection = new List<SparqlProjectionItem>();
            if (Current.IsPunct("*"))
            {
                targets.Add("*");
                Advance();
            }
            else
            {
                ParseDescribeTargets(targets, terms, projection);
            }

            var datasets = ParseDatasetClauses();

            var where = new GroupScope { Kind = GroupScopeKind.Group, Ordinal = 0 };
            if (Current.IsKeyword("WHERE") || Current.IsPunct("{"))
            {
                where = ParseWhere();
            }

            (List<string> modifiers, SparqlConstraint? values) = ParseSolutionModifiers();
            if (values is not null)
            {
                where.Constraints.Add(values);
            }

            return Finish(new SparqlQueryModel
            {
                Form = SparqlQueryForm.Describe,
                DescribeTargets = targets,
                DescribeTerms = terms,
                Projection = projection,
                DatasetClauses = datasets,
                Where = where,
                ModifierRows = modifiers,
            });
        }

        /// <summary>The variables and IRIs DESCRIBE names, each as written, as a term, and - for a variable - as a projected name.</summary>
        private void ParseDescribeTargets(List<string> targets, List<SparqlTerm> terms, List<SparqlProjectionItem> projection)
        {
            while (Current.Kind is SparqlTokenKind.Variable or SparqlTokenKind.Iri or SparqlTokenKind.PrefixedName)
            {
                var token = Current;
                targets.Add(token.Value);
                switch (token.Kind)
                {
                    case SparqlTokenKind.Variable:
                        projection.Add(new SparqlProjectionItem(token.Value[1..], ""));
                        terms.Add(new VariableTerm(token.Value[1..], token.Value));
                        break;
                    case SparqlTokenKind.Iri:
                        terms.Add(new IriTerm(ResolveIri(token), token.Value));
                        break;
                    default:
                        terms.Add(new IriTerm(ExpandPrefixed(token), token.Value));
                        break;
                }

                Advance();
            }

            if (targets.Count == 0)
            {
                throw Error("DESCRIBE names at least one variable or IRI, or '*'.", Current);
            }
        }

        private (bool ProjectsAll, List<SparqlProjectionItem> Items) ParseProjection()
        {
            if (Current.IsPunct("*"))
            {
                Advance();
                return (true, []);
            }

            var items = new List<SparqlProjectionItem>();
            while (true)
            {
                if (Current.Kind == SparqlTokenKind.Variable)
                {
                    items.Add(new SparqlProjectionItem(Current.Value[1..], ""));
                    Advance();
                }
                else if (Current.IsPunct("("))
                {
                    // (expression AS ?name) - the expression stays as written, tier two.
                    var open = _cursor.Index;
                    var close = _cursor.FindBalancedEnd(open);
                    var asIndex = _cursor.FindTopLevelKeyword(open + 1, close, "AS");
                    if (asIndex < 0)
                    {
                        throw Error("A parenthesized projection item is written '(expression AS ?name)'.", Current);
                    }

                    var variable = _cursor[asIndex + 1];
                    if (variable.Kind != SparqlTokenKind.Variable)
                    {
                        throw Error("AS names the variable the expression defines.", variable);
                    }

                    var expression = _cursor.SliceTokens(open + 1, asIndex - 1);
                    items.Add(new SparqlProjectionItem(variable.Value[1..], expression));
                    _cursor.Index = close + 1;
                }
                else
                {
                    break;
                }
            }

            return items.Count == 0
                ? throw Error("SELECT projects at least one variable, or '*'.", Current)
                : (false, items);
        }

        private List<string> ParseDatasetClauses()
        {
            var rows = new List<string>();
            while (Current.IsKeyword("FROM"))
            {
                var start = _cursor.Index;
                Advance();
                if (Current.IsKeyword("NAMED"))
                {
                    Advance();
                }

                if (Current.Kind is not (SparqlTokenKind.Iri or SparqlTokenKind.PrefixedName))
                {
                    throw Error("FROM names a graph by IRI.", Current);
                }

                Advance();
                rows.Add(_cursor.SliceTokens(start, _cursor.Index - 1));
            }

            return rows;
        }

        private GroupScope ParseWhere()
        {
            if (Current.IsKeyword("WHERE"))
            {
                Advance();
            }

            return ParseGroupGraphPattern(GroupScopeKind.Group, 0);
        }

        // -- The group-pattern tree ---------------------------------------------------------

        private GroupScope ParseGroupGraphPattern(GroupScopeKind kind, int ordinal, string label = "")
        {
            var open = _cursor.Index;
            _cursor.ExpectPunct("{");
            var scope = new GroupScope { Kind = kind, Ordinal = ordinal, Label = label };

            while (true)
            {
                var token = Current;
                if (token.Kind == SparqlTokenKind.EndOfFile)
                {
                    throw Error("The group that starts here is never closed with '}'.", token);
                }

                if (token.IsPunct("}"))
                {
                    scope.Span = _cursor.SpanOf(open, _cursor.Index);
                    Advance();
                    return scope;
                }

                if (token.IsPunct("."))
                {
                    Advance();
                    continue;
                }

                ParseGroupMember(scope, token);
            }
        }

        /// <summary>One member of a group, starting at <paramref name="token"/>: a nested group or subquery, a constraint, or a triples block.</summary>
        private void ParseGroupMember(GroupScope scope, SparqlToken token)
        {
            if (token.IsPunct("{"))
            {
                if (Next.IsKeyword("SELECT"))
                {
                    ParseSubSelect(scope);
                }
                else
                {
                    ParseGroupOrUnion(scope);
                }
            }
            else if (token.IsKeyword("OPTIONAL"))
            {
                ParseKeywordGroup(scope, GroupScopeKind.Optional);
            }
            else if (token.IsKeyword("MINUS"))
            {
                ParseKeywordGroup(scope, GroupScopeKind.Minus);
            }
            else if (token.IsKeyword("GRAPH"))
            {
                ParseGraph(scope);
            }
            else if (token.IsKeyword("SERVICE"))
            {
                ParseService(scope);
            }
            else if (token.IsKeyword("FILTER"))
            {
                scope.Constraints.Add(ParseFilter());
            }
            else if (token.IsKeyword("BIND"))
            {
                scope.Constraints.Add(ParseBind());
            }
            else if (token.IsKeyword("VALUES"))
            {
                scope.Constraints.Add(ParseValues());
            }
            else
            {
                ParseTriplesBlock(scope);
            }
        }

        /// <summary><c>OPTIONAL { ... }</c> or <c>MINUS { ... }</c>: the keyword, then the group it introduces.</summary>
        private void ParseKeywordGroup(GroupScope scope, GroupScopeKind kind)
        {
            var keyword = _cursor.Index;
            Advance(); // OPTIONAL or MINUS
            var child = ParseGroupGraphPattern(kind, NextOrdinal(scope, kind));
            child.Span = _cursor.SpanOf(keyword, _cursor.Index - 1);
            scope.Children.Add(child);
        }

        private void ParseGraph(GroupScope scope)
        {
            var keyword = _cursor.Index;
            Advance(); // GRAPH
            var term = Current;
            if (term.Kind is not (SparqlTokenKind.Variable or SparqlTokenKind.Iri or SparqlTokenKind.PrefixedName))
            {
                throw Error("GRAPH names its graph by variable or IRI.", term);
            }

            Advance();
            var child = ParseGroupGraphPattern(GroupScopeKind.Graph, NextOrdinal(scope, GroupScopeKind.Graph), term.Value);
            child.Span = _cursor.SpanOf(keyword, _cursor.Index - 1);
            scope.Children.Add(child);
        }

        private void ParseService(GroupScope scope)
        {
            var keyword = _cursor.Index;
            Advance(); // SERVICE
            var silent = Current.IsKeyword("SILENT");
            if (silent)
            {
                Advance();
            }

            var term = Current;
            if (term.Kind is not (SparqlTokenKind.Variable or SparqlTokenKind.Iri or SparqlTokenKind.PrefixedName))
            {
                throw Error("SERVICE names its endpoint by variable or IRI.", term);
            }

            Advance();
            var serviceLabel = silent ? $"SILENT {term.Value}" : term.Value;
            var child = ParseGroupGraphPattern(GroupScopeKind.Service, NextOrdinal(scope, GroupScopeKind.Service), serviceLabel);
            child.Span = _cursor.SpanOf(keyword, _cursor.Index - 1);
            scope.Children.Add(child);
        }

        /// <summary>The ordinal a new child of <paramref name="kind"/> takes under <paramref name="parent"/>: its count among same-kind siblings so far.</summary>
        private static int NextOrdinal(GroupScope parent, GroupScopeKind kind) =>
            parent.Children.Count(child => child.Kind == kind);

        private void ParseGroupOrUnion(GroupScope parent)
        {
            var firstOrdinal = NextOrdinal(parent, GroupScopeKind.Group);
            var open = _cursor.Index;
            var first = ParseGroupGraphPattern(GroupScopeKind.Group, firstOrdinal);

            if (!Current.IsKeyword("UNION"))
            {
                parent.Children.Add(first);
                return;
            }

            // The branches join under one union scope; the first group parsed becomes branch 0.
            var union = new GroupScope { Kind = GroupScopeKind.Union, Ordinal = NextOrdinal(parent, GroupScopeKind.Union) };
            var rebranded = new GroupScope { Kind = GroupScopeKind.Branch, Ordinal = 0, Label = first.Label, Span = first.Span };
            rebranded.Patterns.AddRange(first.Patterns);
            rebranded.Children.AddRange(first.Children);
            rebranded.Constraints.AddRange(first.Constraints);
            rebranded.SubSelects.AddRange(first.SubSelects);
            union.Children.Add(rebranded);

            while (Current.IsKeyword("UNION"))
            {
                Advance();
                union.Children.Add(ParseGroupGraphPattern(GroupScopeKind.Branch, union.Children.Count));
            }

            union.Span = _cursor.SpanOf(open, _cursor.Index - 1);
            parent.Children.Add(union);
        }

        private void ParseSubSelect(GroupScope parent)
        {
            var open = _cursor.Index;
            var close = _cursor.FindBalancedEnd(open);

            // The projection: everything between SELECT and the point its clause ends.
            var select = open + 1;
            (int projectionEnd, List<string> projectedNames, bool projectsAll) = ReadSubSelectProjection(select, close);

            if (projectsAll)
            {
                // SELECT * projects the subquery's own in-scope names; what can be read from
                // its text is every variable it mentions.
                foreach (var name in _cursor.VariablesBetween(select + 1, close - 1))
                {
                    if (!projectedNames.Contains(name))
                    {
                        projectedNames.Add(name);
                    }
                }
            }

            var projectionText = _cursor.SliceTokens(select, Math.Max(select, projectionEnd));
            var text = _cursor.SliceTokens(open, close);
            parent.SubSelects.Add(new SparqlSubSelect(projectionText, text, projectedNames, projectsAll, parent.SubSelects.Count) { Span = _cursor.SpanOf(open, close) });
            _cursor.Index = close + 1;
        }

        /// <summary>
        /// The projection of the subquery whose SELECT is at <paramref name="select"/>: the index of
        /// its last token, the names it projects and whether it is <c>*</c>.
        /// </summary>
        private (int ProjectionEnd, List<string> Names, bool ProjectsAll) ReadSubSelectProjection(int select, int close)
        {
            var projectionEnd = select + 1;
            var projectedNames = new List<string>();
            var projectsAll = false;
            for (var i = select + 1; i < close; i++)
            {
                var token = _cursor[i];
                if (token.IsKeyword("WHERE") || token.IsKeyword("FROM") || token.IsPunct("{"))
                {
                    projectionEnd = i - 1;
                    break;
                }

                if (token.Kind == SparqlTokenKind.Variable)
                {
                    var name = token.Value[1..];
                    // Inside '(expr AS ?n)' only the aliased name is projected; a plain item is
                    // its own name. Both arrive here; the AS-form's inner references are pruned
                    // by only counting names at clause depth or directly after AS.
                    if (i > select + 1 && _cursor[i - 1].IsKeyword("AS"))
                    {
                        projectedNames.Add(name);
                    }
                    else if (_cursor.DepthBetween(select + 1, i) == 0)
                    {
                        projectedNames.Add(name);
                    }
                }
                else if (token.IsPunct("*") && i == select + 1)
                {
                    projectsAll = true;
                }
            }

            return (projectionEnd, projectedNames, projectsAll);
        }

        // -- Constraints: tier two, sliced as written ----------------------------------------

        private SparqlConstraint ParseFilter()
        {
            var start = _cursor.Index;
            Advance(); // FILTER

            if (Current.IsKeyword("EXISTS"))
            {
                Advance();
                _cursor.SkipBalanced("{");
            }
            else if (Current.IsKeyword("NOT"))
            {
                Advance();
                if (!Current.IsKeyword("EXISTS"))
                {
                    throw Error("After FILTER NOT comes EXISTS.", Current);
                }

                Advance();
                _cursor.SkipBalanced("{");
            }
            else if (Current.IsPunct("("))
            {
                _cursor.SkipBalanced("(");
            }
            else if (Current.Kind is SparqlTokenKind.Name or SparqlTokenKind.Iri or SparqlTokenKind.PrefixedName)
            {
                // A built-in or named function call: REGEX(...), ex:matches(...).
                Advance();
                _cursor.SkipBalanced("(");
            }
            else
            {
                throw Error("FILTER takes a bracketed expression, a function call, or EXISTS { ... }.", Current);
            }

            var end = _cursor.Index - 1;
            return new SparqlConstraint(
                SparqlConstraintKind.Filter,
                _cursor.SliceTokens(start, end),
                "",
                _cursor.VariablesBetween(start, end))
            {
                Span = _cursor.SpanOf(start, end),
            };
        }

        private SparqlConstraint ParseBind()
        {
            var start = _cursor.Index;
            Advance(); // BIND
            if (!Current.IsPunct("("))
            {
                throw Error("BIND is written 'BIND(expression AS ?name)'.", Current);
            }

            var open = _cursor.Index;
            var close = _cursor.FindBalancedEnd(open);
            var asIndex = _cursor.FindTopLevelKeyword(open + 1, close, "AS");
            if (asIndex < 0 || _cursor[asIndex + 1].Kind != SparqlTokenKind.Variable)
            {
                throw Error("BIND is written 'BIND(expression AS ?name)'.", _cursor[open]);
            }

            var defined = _cursor[asIndex + 1].Value[1..];
            _cursor.Index = close + 1;
            return new SparqlConstraint(
                SparqlConstraintKind.Bind,
                _cursor.SliceTokens(start, close),
                defined,
                _cursor.VariablesBetween(open + 1, asIndex - 1))
            {
                Span = _cursor.SpanOf(start, close),
            };
        }

        private SparqlConstraint ParseValues()
        {
            var start = _cursor.Index;
            Advance(); // VALUES

            var referenced = new List<string>();
            if (Current.Kind == SparqlTokenKind.Variable)
            {
                referenced.Add(Current.Value[1..]);
                Advance();
            }
            else if (Current.IsPunct("("))
            {
                var close = _cursor.FindBalancedEnd(_cursor.Index);
                referenced.AddRange(_cursor.VariablesBetween(_cursor.Index + 1, close - 1));
                _cursor.Index = close + 1;
            }
            else
            {
                throw Error("VALUES names its variables before its data block.", Current);
            }

            if (!Current.IsPunct("{"))
            {
                throw Error("VALUES takes its rows in a { ... } block.", Current);
            }

            _cursor.SkipBalanced("{");
            return new SparqlConstraint(
                SparqlConstraintKind.Values,
                _cursor.SliceTokens(start, _cursor.Index - 1),
                "",
                referenced)
            {
                Span = _cursor.SpanOf(start, _cursor.Index - 1),
            };
        }

        // -- Triple patterns -----------------------------------------------------------------

        private void ParseTriplesBlock(GroupScope scope)
        {
            var subjectStart = _cursor.Index;
            var subject = ParseTermOrBlank(scope, subjectPosition: true);
            ParsePropertyList(scope, subject, subjectStart);

            if (Current.IsPunct("."))
            {
                Advance();
            }
        }

        /// <param name="scope">The group the patterns are read into.</param>
        /// <param name="subject">The subject they share.</param>
        /// <param name="subjectStart">The index of the subject's first token, where each pattern's span starts.</param>
        private void ParsePropertyList(GroupScope scope, SparqlTerm subject, int subjectStart)
        {
            while (true)
            {
                var predicate = ParseVerb();
                ParseObjectList(scope, subject, subjectStart, predicate);

                if (Current.IsPunct(";"))
                {
                    Advance();

                    // A trailing ';' before the block ends is legal and empty.
                    if (Current.IsPunct(".") || Current.IsPunct("}") || Current.IsPunct("]") || Current.IsPunct(";"))
                    {
                        while (Current.IsPunct(";"))
                        {
                            Advance();
                        }

                        if (!StartsVerb(Current))
                        {
                            return;
                        }
                    }

                    continue;
                }

                return;
            }
        }

        private static bool StartsVerb(SparqlToken token) =>
            token.Kind is SparqlTokenKind.Variable or SparqlTokenKind.Iri or SparqlTokenKind.PrefixedName
            || token.IsKeyword("a")
            || token.IsPunct("^") || token.IsPunct("!") || token.IsPunct("(");

        private void ParseObjectList(GroupScope scope, SparqlTerm subject, int subjectStart, SparqlTerm predicate)
        {
            while (true)
            {
                var @object = ParseTermOrBlank(scope, subjectPosition: false);
                scope.Patterns.Add(new TriplePattern(subject, predicate, @object) { Span = _cursor.SpanOf(subjectStart, _cursor.Index - 1) });

                if (Current.IsPunct(","))
                {
                    Advance();
                    continue;
                }

                return;
            }
        }

        /// <summary>
        /// The verb: a variable, <c>a</c>, a single IRI - or a property path, consumed by shape
        /// and kept as written (tier two). A path that turns out to be one plain IRI is that IRI.
        /// </summary>
        private SparqlTerm ParseVerb()
        {
            var token = Current;
            if (token.Kind == SparqlTokenKind.Variable)
            {
                Advance();
                return new VariableTerm(token.Value[1..], token.Value);
            }

            var start = _cursor.Index;
            ConsumePathExpression();
            var end = _cursor.Index - 1;

            if (end == start)
            {
                var only = _cursor[start];
                if (only.IsKeyword("a"))
                {
                    return new IriTerm(SparqlVocabulary.Type, "a");
                }

                if (only.Kind == SparqlTokenKind.Iri)
                {
                    return new IriTerm(ResolveIri(only), only.Value);
                }

                if (only.Kind == SparqlTokenKind.PrefixedName)
                {
                    return new IriTerm(ExpandPrefixed(only), only.Value);
                }
            }

            return new PathTerm(_cursor.SliceTokens(start, end));
        }

        /// <summary>Consumes one property-path expression: alternatives of sequences of possibly-inverted, possibly-negated, possibly-repeated path atoms.</summary>
        private void ConsumePathExpression()
        {
            ConsumePathSegment();
            while (Current.IsPunct("|") || Current.IsPunct("/"))
            {
                Advance();
                ConsumePathSegment();
            }
        }

        private void ConsumePathSegment()
        {
            if (Current.IsPunct("^"))
            {
                Advance();
            }

            if (Current.IsPunct("!"))
            {
                Advance();
                if (Current.IsPunct("("))
                {
                    _cursor.SkipBalanced("(");
                    ConsumePathPostfix();
                    return;
                }
            }

            if (Current.IsPunct("("))
            {
                var open = _cursor.Index;
                Advance();
                ConsumePathExpression();
                if (!Current.IsPunct(")"))
                {
                    throw Error("The path group that starts here is never closed with ')'.", _cursor[open]);
                }

                Advance();
                ConsumePathPostfix();
                return;
            }

            if (Current.Kind is SparqlTokenKind.Iri or SparqlTokenKind.PrefixedName || Current.IsKeyword("a"))
            {
                Advance();
                ConsumePathPostfix();
                return;
            }

            throw Error($"Expected a predicate or property path, found '{Current.Value}'.", Current);
        }

        private void ConsumePathPostfix()
        {
            if (Current.IsPunct("?") || Current.IsPunct("*") || Current.IsPunct("+"))
            {
                Advance();
            }
        }

        private SparqlTerm ParseTermOrBlank(GroupScope scope, bool subjectPosition)
        {
            var token = Current;
            switch (token.Kind)
            {
                case SparqlTokenKind.Variable:
                    Advance();
                    return new VariableTerm(token.Value[1..], token.Value);

                case SparqlTokenKind.Iri:
                    Advance();
                    return new IriTerm(ResolveIri(token), token.Value);

                case SparqlTokenKind.PrefixedName:
                    Advance();
                    return new IriTerm(ExpandPrefixed(token), token.Value);

                case SparqlTokenKind.BlankNodeLabel:
                    Advance();
                    return LabeledAnonymous(token);

                case SparqlTokenKind.String:
                    return ParseLiteral(token);

                case SparqlTokenKind.Number:
                    Advance();
                    return new LiteralTerm(token.Value, NumericLiteral.Datatype(token.Value), "", token.Value);

                case SparqlTokenKind.Boolean:
                    Advance();
                    return new LiteralTerm(token.Value, SparqlVocabulary.XsdBoolean, "", token.Value);
            }

            if (token.IsPunct("["))
            {
                return ParseBlankNodePropertyList(scope);
            }

            if (token.IsPunct("("))
            {
                return ParseCollection(scope);
            }

            var position = subjectPosition ? "subject" : "object";
            throw Error($"Expected a {position} term, found '{token.Value}'.", token);
        }

        /// <summary>The one anonymous term a blank node label stands for, however often the query writes it.</summary>
        private AnonymousTerm LabeledAnonymous(SparqlToken label)
        {
            if (!_labeledAnonymous.TryGetValue(label.Value, out var labeled))
            {
                labeled = new AnonymousTerm(_anonymousOrdinal++, label.Value);
                _labeledAnonymous[label.Value] = labeled;
            }

            return labeled;
        }

        /// <summary><c>[ ... ]</c>: a fresh anonymous term, and the patterns its property list gives it.</summary>
        private AnonymousTerm ParseBlankNodePropertyList(GroupScope scope)
        {
            var anonymous = new AnonymousTerm(_anonymousOrdinal++, "");
            var open = _cursor.Index;
            Advance(); // [
            if (Current.IsPunct("]"))
            {
                Advance();
                return anonymous;
            }

            ParsePropertyList(scope, anonymous, open);
            _cursor.ExpectPunct("]");
            return anonymous;
        }

        private SparqlTerm ParseCollection(GroupScope scope)
        {
            // A cell's patterns start where the cell does: the '(' for the first, its item for the rest.
            var cellStart = _cursor.Index;
            Advance(); // (
            if (Current.IsPunct(")"))
            {
                Advance();
                return new IriTerm(SparqlVocabulary.Nil, "()");
            }

            // ( e1 e2 ... ) expands to its cons cells, each an anonymous variable, exactly the
            // way the grammar defines collection syntax in patterns.
            var head = new AnonymousTerm(_anonymousOrdinal++, "");
            var current = head;
            var first = new IriTerm(SparqlVocabulary.First, "rdf:first");
            var rest = new IriTerm(SparqlVocabulary.Rest, "rdf:rest");

            while (true)
            {
                var item = ParseTermOrBlank(scope, subjectPosition: false);
                scope.Patterns.Add(new TriplePattern(current, first, item) { Span = _cursor.SpanOf(cellStart, _cursor.Index - 1) });

                if (Current.IsPunct(")"))
                {
                    Advance();
                    scope.Patterns.Add(new TriplePattern(current, rest, new IriTerm(SparqlVocabulary.Nil, "rdf:nil")) { Span = _cursor.SpanOf(cellStart, _cursor.Index - 1) });
                    return head;
                }

                var next = new AnonymousTerm(_anonymousOrdinal++, "");
                scope.Patterns.Add(new TriplePattern(current, rest, next) { Span = _cursor.SpanOf(cellStart, _cursor.Index - 1) });
                cellStart = _cursor.Index;
                current = next;
            }
        }

        private LiteralTerm ParseLiteral(SparqlToken stringToken)
        {
            Advance();
            var lexical = DecodeString(stringToken);

            if (Current.Kind == SparqlTokenKind.LanguageTag)
            {
                var tag = Current.Value[1..];
                Advance();
                return new LiteralTerm(lexical, "", tag, _cursor.SliceTokens(_cursor.Index - 2, _cursor.Index - 1));
            }

            if (Current.IsPunct("^^"))
            {
                Advance();
                var datatype = Current;
                if (datatype.Kind == SparqlTokenKind.Iri)
                {
                    Advance();
                    return new LiteralTerm(lexical, ResolveIri(datatype), "", _cursor.SliceTokens(_cursor.Index - 3, _cursor.Index - 1));
                }

                if (datatype.Kind == SparqlTokenKind.PrefixedName)
                {
                    Advance();
                    return new LiteralTerm(lexical, ExpandPrefixed(datatype), "", _cursor.SliceTokens(_cursor.Index - 3, _cursor.Index - 1));
                }

                throw Error("After '^^' comes the datatype IRI.", datatype);
            }

            return new LiteralTerm(lexical, "", "", stringToken.Value);
        }

        // -- Solution modifiers: frame, kept as written ---------------------------------------

        private (List<string> Rows, SparqlConstraint? TrailingValues) ParseSolutionModifiers()
        {
            var rows = new List<string>();
            SparqlConstraint? trailingValues = null;

            while (true)
            {
                if (Current.IsKeyword("GROUP"))
                {
                    rows.Add(SliceModifier(["HAVING", "ORDER", "LIMIT", "OFFSET", "VALUES"]));
                }
                else if (Current.IsKeyword("HAVING"))
                {
                    rows.Add(SliceModifier(["ORDER", "LIMIT", "OFFSET", "VALUES"]));
                }
                else if (Current.IsKeyword("ORDER"))
                {
                    rows.Add(SliceModifier(["LIMIT", "OFFSET", "VALUES"]));
                }
                else if (Current.IsKeyword("LIMIT") || Current.IsKeyword("OFFSET"))
                {
                    var start = _cursor.Index;
                    Advance();
                    _cursor.Expect(SparqlTokenKind.Number, "a whole number");
                    rows.Add(_cursor.SliceTokens(start, _cursor.Index - 1));
                }
                else if (Current.IsKeyword("VALUES"))
                {
                    trailingValues = ParseValues();
                }
                else
                {
                    return (rows, trailingValues);
                }
            }
        }

        private string SliceModifier(string[] stopKeywords)
        {
            var start = _cursor.Index;
            Advance();

            while (Current.Kind != SparqlTokenKind.EndOfFile)
            {
                if (Current.Kind == SparqlTokenKind.Name && stopKeywords.Any(Current.IsKeyword))
                {
                    break;
                }

                if (Current.IsPunct("("))
                {
                    _cursor.SkipBalanced("(");
                    continue;
                }

                Advance();
            }

            return _cursor.SliceTokens(start, _cursor.Index - 1);
        }

        /// <summary>The query read, handed to the scope analysis with the prologue it was read under.</summary>
        private SparqlQueryModel Finish(SparqlQueryModel model) =>
            SparqlScopeAnalyzer.Finish(model with { BaseIri = _baseIri, Prefixes = _prefixes });

        // -- Names, IRIs and strings -----------------------------------------------------------

        private string ResolveIri(SparqlToken token)
        {
            var value = Unbracket(token.Value);
            if (value.Contains(':') || _baseIri.Length == 0)
            {
                return value;
            }

            return _baseIri + value;
        }

        private string ExpandPrefixed(SparqlToken token)
        {
            var colon = token.Value.IndexOf(':');
            var prefix = token.Value[..colon];
            var local = token.Value[(colon + 1)..];
            if (!_prefixMap.TryGetValue(prefix, out var iri))
            {
                var shown = prefix.Length > 0 ? prefix : "(the default prefix)";
                throw Error($"The prefix '{shown}' is used here but never declared - SPARQL requires every prefix to be declared with PREFIX.", token);
            }

            return iri + RdfEscapes.Local(local);
        }

        private static string Unbracket(string bracketed) => bracketed[1..^1];

        private string DecodeString(SparqlToken token)
        {
            // An escape SPARQL does not know is refused at its own line (behaviour changes B-18).
            if (RdfEscapes.Refusal(token.Value) is var (offset, message))
            {
                throw new SparqlParseException(message, _cursor.LineAt(token.Start + offset), _cursor.ColumnAt(token.Start + offset));
            }

            return RdfEscapes.String(token.Value);
        }

        // -- Token plumbing ----------------------------------------------------------------------

        private SparqlToken Current => _cursor.Current;

        private SparqlToken Next => _cursor.Next;

        private void Advance() => _cursor.Advance();

        private SparqlParseException Error(string message, SparqlToken at) => _cursor.Error(message, at);
    }
}
