using System.Text;

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
    private static readonly string[] _updateVerbs =
        ["INSERT", "DELETE", "LOAD", "CLEAR", "CREATE", "DROP", "COPY", "MOVE", "ADD", "WITH"];

    /// <summary>What <paramref name="text"/> asks, or a <see cref="SparqlParseException"/> saying why it could not be read.</summary>
    public static SparqlQueryModel Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new Parser(text).ParseQuery();
    }

    private sealed class Parser
    {
        private readonly string _text;
        private readonly SparqlTokenizer _tokenizer;
        private readonly List<SparqlToken> _tokens;
        private int _index;

        private readonly List<SparqlPrefixDeclaration> _prefixes = [];
        private readonly Dictionary<string, string> _prefixMap = [];
        private readonly Dictionary<string, AnonymousTerm> _labeledAnonymous = [];
        private string _baseIri = "";
        private int _anonymousOrdinal;

        public Parser(string text)
        {
            _text = text;
            _tokenizer = new SparqlTokenizer(text);
            _tokens = _tokenizer.Tokenize();
        }

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

            if (Current.Kind != SparqlTokenKind.EndOfFile)
            {
                throw Error($"The query is over, but '{Current.Value}' follows it.", Current);
            }

            return model;
        }

        // -- The prologue -------------------------------------------------------------------

        private void ParsePrologue()
        {
            while (true)
            {
                if (Current.IsKeyword("PREFIX"))
                {
                    var line = LineOf(Current);
                    Advance();
                    var name = Expect(SparqlTokenKind.PrefixedName, "a prefix name ending in ':'");
                    var colon = name.Value.IndexOf(':');
                    if (colon != name.Value.Length - 1)
                    {
                        throw Error($"A PREFIX declaration names the prefix alone, ending in ':' - found '{name.Value}'.", name);
                    }

                    var iri = Expect(SparqlTokenKind.Iri, "the prefix's <IRI>");
                    var prefix = name.Value[..colon];
                    var expanded = Unbracket(iri.Value);
                    _prefixMap[prefix] = expanded;
                    _prefixes.Add(new SparqlPrefixDeclaration(prefix, expanded, line));
                }
                else if (Current.IsKeyword("BASE"))
                {
                    Advance();
                    var iri = Expect(SparqlTokenKind.Iri, "the base <IRI>");
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

            var (projectsAll, projection) = ParseProjection();
            var datasets = ParseDatasetClauses();
            var where = ParseWhere();
            var (modifiers, values) = ParseSolutionModifiers();
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
                template = new GroupScope { Kind = GroupScopeKind.Template, Ordinal = 0 };
                template.Patterns.AddRange(where.Patterns);
            }

            var (modifiers, values) = ParseSolutionModifiers();
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
            var (modifiers, values) = ParseSolutionModifiers();
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
            var projection = new List<SparqlProjectionItem>();
            if (Current.IsPunct("*"))
            {
                targets.Add("*");
                Advance();
            }
            else
            {
                while (Current.Kind is SparqlTokenKind.Variable or SparqlTokenKind.Iri or SparqlTokenKind.PrefixedName)
                {
                    targets.Add(Current.Value);
                    if (Current.Kind == SparqlTokenKind.Variable)
                    {
                        projection.Add(new SparqlProjectionItem(Current.Value[1..], ""));
                    }

                    Advance();
                }

                if (targets.Count == 0)
                {
                    throw Error("DESCRIBE names at least one variable or IRI, or '*'.", Current);
                }
            }

            var datasets = ParseDatasetClauses();

            var where = new GroupScope { Kind = GroupScopeKind.Group, Ordinal = 0 };
            if (Current.IsKeyword("WHERE") || Current.IsPunct("{"))
            {
                where = ParseWhere();
            }

            var (modifiers, values) = ParseSolutionModifiers();
            if (values is not null)
            {
                where.Constraints.Add(values);
            }

            return Finish(new SparqlQueryModel
            {
                Form = SparqlQueryForm.Describe,
                DescribeTargets = targets,
                Projection = projection,
                DatasetClauses = datasets,
                Where = where,
                ModifierRows = modifiers,
            });
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
                    var open = _index;
                    var close = FindBalancedEnd(open);
                    var asIndex = FindTopLevelKeyword(open + 1, close, "AS");
                    if (asIndex < 0)
                    {
                        throw Error("A parenthesized projection item is written '(expression AS ?name)'.", Current);
                    }

                    var variable = _tokens[asIndex + 1];
                    if (variable.Kind != SparqlTokenKind.Variable)
                    {
                        throw Error("AS names the variable the expression defines.", variable);
                    }

                    var expression = SliceTokens(open + 1, asIndex - 1);
                    items.Add(new SparqlProjectionItem(variable.Value[1..], expression));
                    _index = close + 1;
                }
                else
                {
                    break;
                }
            }

            if (items.Count == 0)
            {
                throw Error("SELECT projects at least one variable, or '*'.", Current);
            }

            return (false, items);
        }

        private List<string> ParseDatasetClauses()
        {
            var rows = new List<string>();
            while (Current.IsKeyword("FROM"))
            {
                var start = _index;
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
                rows.Add(SliceTokens(start, _index - 1));
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
            ExpectPunct("{");
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
                    Advance();
                    return scope;
                }

                if (token.IsPunct("."))
                {
                    Advance();
                    continue;
                }

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

                    continue;
                }

                if (token.IsKeyword("OPTIONAL"))
                {
                    Advance();
                    scope.Children.Add(ParseGroupGraphPattern(GroupScopeKind.Optional, NextOrdinal(scope, GroupScopeKind.Optional)));
                    continue;
                }

                if (token.IsKeyword("MINUS"))
                {
                    Advance();
                    scope.Children.Add(ParseGroupGraphPattern(GroupScopeKind.Minus, NextOrdinal(scope, GroupScopeKind.Minus)));
                    continue;
                }

                if (token.IsKeyword("GRAPH"))
                {
                    Advance();
                    var term = Current;
                    if (term.Kind is not (SparqlTokenKind.Variable or SparqlTokenKind.Iri or SparqlTokenKind.PrefixedName))
                    {
                        throw Error("GRAPH names its graph by variable or IRI.", term);
                    }

                    Advance();
                    scope.Children.Add(ParseGroupGraphPattern(GroupScopeKind.Graph, NextOrdinal(scope, GroupScopeKind.Graph), term.Value));
                    continue;
                }

                if (token.IsKeyword("SERVICE"))
                {
                    Advance();
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
                    scope.Children.Add(ParseGroupGraphPattern(GroupScopeKind.Service, NextOrdinal(scope, GroupScopeKind.Service), serviceLabel));
                    continue;
                }

                if (token.IsKeyword("FILTER"))
                {
                    scope.Constraints.Add(ParseFilter());
                    continue;
                }

                if (token.IsKeyword("BIND"))
                {
                    scope.Constraints.Add(ParseBind());
                    continue;
                }

                if (token.IsKeyword("VALUES"))
                {
                    scope.Constraints.Add(ParseValues());
                    continue;
                }

                ParseTriplesBlock(scope);
            }
        }

        /// <summary>The ordinal a new child of <paramref name="kind"/> takes under <paramref name="parent"/>: its count among same-kind siblings so far.</summary>
        private static int NextOrdinal(GroupScope parent, GroupScopeKind kind) =>
            parent.Children.Count(child => child.Kind == kind);

        private void ParseGroupOrUnion(GroupScope parent)
        {
            var firstOrdinal = NextOrdinal(parent, GroupScopeKind.Group);
            var first = ParseGroupGraphPattern(GroupScopeKind.Group, firstOrdinal);

            if (!Current.IsKeyword("UNION"))
            {
                parent.Children.Add(first);
                return;
            }

            // The branches join under one union scope; the first group parsed becomes branch 0.
            var union = new GroupScope { Kind = GroupScopeKind.Union, Ordinal = NextOrdinal(parent, GroupScopeKind.Union) };
            var rebranded = new GroupScope { Kind = GroupScopeKind.Branch, Ordinal = 0, Label = first.Label };
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

            parent.Children.Add(union);
        }

        private void ParseSubSelect(GroupScope parent)
        {
            var open = _index;
            var close = FindBalancedEnd(open);

            // The projection: everything between SELECT and the point its clause ends.
            var select = open + 1;
            var projectionEnd = select + 1;
            var projectedNames = new List<string>();
            var projectsAll = false;
            for (var i = select + 1; i < close; i++)
            {
                var token = _tokens[i];
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
                    if (i > select + 1 && _tokens[i - 1].IsKeyword("AS"))
                    {
                        projectedNames.Add(name);
                    }
                    else if (DepthBetween(select + 1, i) == 0)
                    {
                        projectedNames.Add(name);
                    }
                }
                else if (token.IsPunct("*") && i == select + 1)
                {
                    projectsAll = true;
                }
            }

            if (projectsAll)
            {
                // SELECT * projects the subquery's own in-scope names; what can be read from
                // its text is every variable it mentions.
                for (var i = select + 1; i < close; i++)
                {
                    if (_tokens[i].Kind == SparqlTokenKind.Variable)
                    {
                        var name = _tokens[i].Value[1..];
                        if (!projectedNames.Contains(name))
                        {
                            projectedNames.Add(name);
                        }
                    }
                }
            }

            var projectionText = SliceTokens(select, Math.Max(select, projectionEnd));
            var text = SliceTokens(open, close);
            parent.SubSelects.Add(new SparqlSubSelect(projectionText, text, projectedNames, projectsAll, parent.SubSelects.Count));
            _index = close + 1;
        }

        // -- Constraints: tier two, sliced as written ----------------------------------------

        private SparqlConstraint ParseFilter()
        {
            var start = _index;
            Advance(); // FILTER

            if (Current.IsKeyword("EXISTS"))
            {
                Advance();
                SkipBalanced("{");
            }
            else if (Current.IsKeyword("NOT"))
            {
                Advance();
                if (!Current.IsKeyword("EXISTS"))
                {
                    throw Error("After FILTER NOT comes EXISTS.", Current);
                }

                Advance();
                SkipBalanced("{");
            }
            else if (Current.IsPunct("("))
            {
                SkipBalanced("(");
            }
            else if (Current.Kind is SparqlTokenKind.Name or SparqlTokenKind.Iri or SparqlTokenKind.PrefixedName)
            {
                // A built-in or named function call: REGEX(...), ex:matches(...).
                Advance();
                SkipBalanced("(");
            }
            else
            {
                throw Error("FILTER takes a bracketed expression, a function call, or EXISTS { ... }.", Current);
            }

            var end = _index - 1;
            return new SparqlConstraint(
                SparqlConstraintKind.Filter,
                SliceTokens(start, end),
                "",
                VariablesBetween(start, end));
        }

        private SparqlConstraint ParseBind()
        {
            var start = _index;
            Advance(); // BIND
            if (!Current.IsPunct("("))
            {
                throw Error("BIND is written 'BIND(expression AS ?name)'.", Current);
            }

            var open = _index;
            var close = FindBalancedEnd(open);
            var asIndex = FindTopLevelKeyword(open + 1, close, "AS");
            if (asIndex < 0 || _tokens[asIndex + 1].Kind != SparqlTokenKind.Variable)
            {
                throw Error("BIND is written 'BIND(expression AS ?name)'.", _tokens[open]);
            }

            var defined = _tokens[asIndex + 1].Value[1..];
            _index = close + 1;
            return new SparqlConstraint(
                SparqlConstraintKind.Bind,
                SliceTokens(start, close),
                defined,
                VariablesBetween(open + 1, asIndex - 1));
        }

        private SparqlConstraint ParseValues()
        {
            var start = _index;
            Advance(); // VALUES

            var referenced = new List<string>();
            if (Current.Kind == SparqlTokenKind.Variable)
            {
                referenced.Add(Current.Value[1..]);
                Advance();
            }
            else if (Current.IsPunct("("))
            {
                var close = FindBalancedEnd(_index);
                referenced.AddRange(VariablesBetween(_index + 1, close - 1));
                _index = close + 1;
            }
            else
            {
                throw Error("VALUES names its variables before its data block.", Current);
            }

            if (!Current.IsPunct("{"))
            {
                throw Error("VALUES takes its rows in a { ... } block.", Current);
            }

            SkipBalanced("{");
            return new SparqlConstraint(
                SparqlConstraintKind.Values,
                SliceTokens(start, _index - 1),
                "",
                referenced);
        }

        // -- Triple patterns -----------------------------------------------------------------

        private void ParseTriplesBlock(GroupScope scope)
        {
            var subject = ParseTermOrBlank(scope, subjectPosition: true);
            ParsePropertyList(scope, subject);

            if (Current.IsPunct("."))
            {
                Advance();
            }
        }

        private void ParsePropertyList(GroupScope scope, SparqlTerm subject)
        {
            while (true)
            {
                var predicate = ParseVerb();
                ParseObjectList(scope, subject, predicate);

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

        private void ParseObjectList(GroupScope scope, SparqlTerm subject, SparqlTerm predicate)
        {
            while (true)
            {
                var @object = ParseTermOrBlank(scope, subjectPosition: false);
                scope.Patterns.Add(new TriplePattern(subject, predicate, @object));

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

            var start = _index;
            ConsumePathExpression();
            var end = _index - 1;

            if (end == start)
            {
                var only = _tokens[start];
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

            return new PathTerm(SliceTokens(start, end));
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
                    SkipBalanced("(");
                    ConsumePathPostfix();
                    return;
                }
            }

            if (Current.IsPunct("("))
            {
                var open = _index;
                Advance();
                ConsumePathExpression();
                if (!Current.IsPunct(")"))
                {
                    throw Error("The path group that starts here is never closed with ')'.", _tokens[open]);
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
                    if (!_labeledAnonymous.TryGetValue(token.Value, out var labeled))
                    {
                        labeled = new AnonymousTerm(_anonymousOrdinal++, token.Value);
                        _labeledAnonymous[token.Value] = labeled;
                    }

                    return labeled;

                case SparqlTokenKind.String:
                    return ParseLiteral(token);

                case SparqlTokenKind.Number:
                    Advance();
                    return new LiteralTerm(token.Value, NumericDatatype(token.Value), "", token.Value);

                case SparqlTokenKind.Boolean:
                    Advance();
                    return new LiteralTerm(token.Value, SparqlVocabulary.XsdBoolean, "", token.Value);
            }

            if (token.IsPunct("["))
            {
                var anonymous = new AnonymousTerm(_anonymousOrdinal++, "");
                Advance();
                if (Current.IsPunct("]"))
                {
                    Advance();
                    return anonymous;
                }

                ParsePropertyList(scope, anonymous);
                ExpectPunct("]");
                return anonymous;
            }

            if (token.IsPunct("("))
            {
                return ParseCollection(scope);
            }

            var position = subjectPosition ? "subject" : "object";
            throw Error($"Expected a {position} term, found '{token.Value}'.", token);
        }

        private SparqlTerm ParseCollection(GroupScope scope)
        {
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
                scope.Patterns.Add(new TriplePattern(current, first, item));

                if (Current.IsPunct(")"))
                {
                    Advance();
                    scope.Patterns.Add(new TriplePattern(current, rest, new IriTerm(SparqlVocabulary.Nil, "rdf:nil")));
                    return head;
                }

                var next = new AnonymousTerm(_anonymousOrdinal++, "");
                scope.Patterns.Add(new TriplePattern(current, rest, next));
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
                return new LiteralTerm(lexical, "", tag, SliceTokens(_index - 2, _index - 1));
            }

            if (Current.IsPunct("^^"))
            {
                Advance();
                var datatype = Current;
                if (datatype.Kind == SparqlTokenKind.Iri)
                {
                    Advance();
                    return new LiteralTerm(lexical, ResolveIri(datatype), "", SliceTokens(_index - 3, _index - 1));
                }

                if (datatype.Kind == SparqlTokenKind.PrefixedName)
                {
                    Advance();
                    return new LiteralTerm(lexical, ExpandPrefixed(datatype), "", SliceTokens(_index - 3, _index - 1));
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
                    var start = _index;
                    Advance();
                    Expect(SparqlTokenKind.Number, "a whole number");
                    rows.Add(SliceTokens(start, _index - 1));
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
            var start = _index;
            Advance();

            while (Current.Kind != SparqlTokenKind.EndOfFile)
            {
                if (Current.Kind == SparqlTokenKind.Name && stopKeywords.Any(Current.IsKeyword))
                {
                    break;
                }

                if (Current.IsPunct("("))
                {
                    SkipBalanced("(");
                    continue;
                }

                Advance();
            }

            return SliceTokens(start, _index - 1);
        }

        // -- Finishing: paths and the variable index ------------------------------------------

        private SparqlQueryModel Finish(SparqlQueryModel model)
        {
            model.Where.Path = "where";
            AssignPaths(model.Where);
            if (model.Template is not null)
            {
                model.Template.Path = "template";
                AssignPaths(model.Template);
            }

            var usages = new Dictionary<string, UsageBuilder>(StringComparer.Ordinal);
            IndexScope(model.Where, usages);
            if (model.Template is not null)
            {
                IndexScope(model.Template, usages);
            }

            var projection = model.Projection;
            if (model.ProjectsAll && model.Form == SparqlQueryForm.Select)
            {
                projection = usages.Keys.OrderBy(name => usages[name].FirstSeen).Select(name => new SparqlProjectionItem(name, "")).ToList();
            }

            foreach (var item in projection)
            {
                var builder = GetOrAdd(usages, item.Name);
                builder.Projected = true;
                if (item.ExpressionText.Length > 0)
                {
                    builder.DefiningExpression = item.ExpressionText;
                }
            }

            return model with
            {
                BaseIri = _baseIri,
                Prefixes = _prefixes,
                Projection = projection,
                Variables = usages.ToDictionary(
                    pair => pair.Key,
                    pair => new SparqlVariableUsage(
                        pair.Key,
                        pair.Value.PatternOccurrences,
                        pair.Value.Projected,
                        pair.Value.DefiningExpression,
                        pair.Value.ScopePaths)),
            };
        }

        private static void AssignPaths(GroupScope scope)
        {
            foreach (var child in scope.Children)
            {
                child.Path = $"{scope.Path}/{child.Kind.ToString().ToLowerInvariant()}.{child.Ordinal}";
                AssignPaths(child);
            }
        }

        private sealed class UsageBuilder
        {
            public int PatternOccurrences;
            public bool Projected;
            public string DefiningExpression = "";
            public List<string> ScopePaths { get; } = [];
            public int FirstSeen;
        }

        private int _seenCounter;

        private UsageBuilder GetOrAdd(Dictionary<string, UsageBuilder> usages, string name)
        {
            if (!usages.TryGetValue(name, out var builder))
            {
                builder = new UsageBuilder { FirstSeen = _seenCounter++ };
                usages[name] = builder;
            }

            return builder;
        }

        private void IndexScope(GroupScope scope, Dictionary<string, UsageBuilder> usages)
        {
            void Touch(string name, bool patternPosition)
            {
                var builder = GetOrAdd(usages, name);
                if (patternPosition)
                {
                    builder.PatternOccurrences++;
                }

                if (!builder.ScopePaths.Contains(scope.Path))
                {
                    builder.ScopePaths.Add(scope.Path);
                }
            }

            foreach (var pattern in scope.Patterns)
            {
                foreach (var term in (SparqlTerm[])[pattern.Subject, pattern.Predicate, pattern.Object])
                {
                    if (term is VariableTerm variable)
                    {
                        Touch(variable.Name, patternPosition: true);
                    }
                }
            }

            foreach (var constraint in scope.Constraints)
            {
                foreach (var name in constraint.ReferencedVariables)
                {
                    Touch(name, patternPosition: false);
                }

                if (constraint.DefinedVariable.Length > 0)
                {
                    var builder = GetOrAdd(usages, constraint.DefinedVariable);
                    builder.DefiningExpression = constraint.Text;
                    Touch(constraint.DefinedVariable, patternPosition: false);
                }
            }

            foreach (var subSelect in scope.SubSelects)
            {
                foreach (var name in subSelect.ProjectedNames)
                {
                    Touch(name, patternPosition: false);
                }
            }

            foreach (var child in scope.Children)
            {
                IndexScope(child, usages);
            }
        }

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

            return iri + local;
        }

        private static string Unbracket(string bracketed) => bracketed[1..^1];

        private string DecodeString(SparqlToken token)
        {
            var value = token.Value;
            var quote = value[0];
            var longQuote = value.Length >= 6 && value[1] == quote && value[2] == quote;
            var inner = longQuote ? value[3..^3] : value[1..^1];

            var result = new StringBuilder(inner.Length);
            for (var i = 0; i < inner.Length; i++)
            {
                var character = inner[i];
                if (character != '\\' || i + 1 >= inner.Length)
                {
                    result.Append(character);
                    continue;
                }

                i++;
                var escaped = inner[i];
                switch (escaped)
                {
                    case 't': result.Append('\t'); break;
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    case '"': result.Append('"'); break;
                    case '\'': result.Append('\''); break;
                    case '\\': result.Append('\\'); break;
                    case 'u' when i + 4 < inner.Length:
                        result.Append((char)Convert.ToInt32(inner.Substring(i + 1, 4), 16));
                        i += 4;
                        break;
                    case 'U' when i + 8 < inner.Length:
                        result.Append(char.ConvertFromUtf32(Convert.ToInt32(inner.Substring(i + 1, 8), 16)));
                        i += 8;
                        break;
                    default:
                        throw Error($"'\\{escaped}' is not a string escape SPARQL knows.", token);
                }
            }

            return result.ToString();
        }

        private static string NumericDatatype(string value)
        {
            if (value.Contains('e') || value.Contains('E'))
            {
                return SparqlVocabulary.XsdDouble;
            }

            return value.Contains('.') ? SparqlVocabulary.XsdDecimal : SparqlVocabulary.XsdInteger;
        }

        // -- Token plumbing ----------------------------------------------------------------------

        private SparqlToken Current => _tokens[_index];

        private SparqlToken Next => _index + 1 < _tokens.Count ? _tokens[_index + 1] : _tokens[^1];

        private void Advance() => _index = Math.Min(_index + 1, _tokens.Count - 1);

        private SparqlToken Expect(SparqlTokenKind kind, string described)
        {
            var token = Current;
            if (token.Kind != kind)
            {
                throw Error($"Expected {described}, found '{(token.Kind == SparqlTokenKind.EndOfFile ? "the end of the file" : token.Value)}'.", token);
            }

            Advance();
            return token;
        }

        private void ExpectPunct(string punctuation)
        {
            if (!Current.IsPunct(punctuation))
            {
                throw Error($"Expected '{punctuation}', found '{(Current.Kind == SparqlTokenKind.EndOfFile ? "the end of the file" : Current.Value)}'.", Current);
            }

            Advance();
        }

        /// <summary>The index of the token closing the bracket at <paramref name="openIndex"/> - '(' , '{' and '[' counted together, strings and IRIs already single tokens.</summary>
        private int FindBalancedEnd(int openIndex)
        {
            var depth = 0;
            for (var i = openIndex; i < _tokens.Count; i++)
            {
                var token = _tokens[i];
                if (token.Kind == SparqlTokenKind.Punct)
                {
                    if (token.Value is "(" or "{" or "[")
                    {
                        depth++;
                    }
                    else if (token.Value is ")" or "}" or "]")
                    {
                        depth--;
                        if (depth == 0)
                        {
                            return i;
                        }
                    }
                }
            }

            throw Error("The bracket that opens here is never closed.", _tokens[openIndex]);
        }

        /// <summary>Consumes from the current opening bracket through its balanced close.</summary>
        private void SkipBalanced(string expectedOpen)
        {
            if (!Current.IsPunct(expectedOpen))
            {
                throw Error($"Expected '{expectedOpen}', found '{Current.Value}'.", Current);
            }

            _index = FindBalancedEnd(_index) + 1;
        }

        /// <summary>The first <paramref name="keyword"/> between the bounds sitting at bracket depth zero relative to <paramref name="start"/>.</summary>
        private int FindTopLevelKeyword(int start, int endExclusive, string keyword)
        {
            var depth = 0;
            for (var i = start; i < endExclusive; i++)
            {
                var token = _tokens[i];
                if (token.Kind == SparqlTokenKind.Punct)
                {
                    if (token.Value is "(" or "{" or "[")
                    {
                        depth++;
                    }
                    else if (token.Value is ")" or "}" or "]")
                    {
                        depth--;
                    }
                }
                else if (depth == 0 && token.IsKeyword(keyword))
                {
                    return i;
                }
            }

            return -1;
        }

        private int DepthBetween(int start, int endExclusive)
        {
            var depth = 0;
            for (var i = start; i < endExclusive; i++)
            {
                var token = _tokens[i];
                if (token.Kind == SparqlTokenKind.Punct)
                {
                    if (token.Value is "(" or "{" or "[")
                    {
                        depth++;
                    }
                    else if (token.Value is ")" or "}" or "]")
                    {
                        depth--;
                    }
                }
            }

            return depth;
        }

        private List<string> VariablesBetween(int start, int endInclusive)
        {
            var names = new List<string>();
            for (var i = start; i <= endInclusive && i < _tokens.Count; i++)
            {
                if (_tokens[i].Kind == SparqlTokenKind.Variable)
                {
                    var name = _tokens[i].Value[1..];
                    if (!names.Contains(name))
                    {
                        names.Add(name);
                    }
                }
            }

            return names;
        }

        /// <summary>The raw source text from the first token through the last, exactly as written - tier two's whole mechanism.</summary>
        private string SliceTokens(int firstIndex, int lastIndex)
        {
            var first = _tokens[firstIndex];
            var last = _tokens[Math.Max(firstIndex, lastIndex)];
            return _text[first.Start..last.End];
        }

        private int LineOf(SparqlToken token) => _tokenizer.LineOf(token.Start) + 1;

        private SparqlParseException Error(string message, SparqlToken at) =>
            new(message, LineOf(at));
    }
}
