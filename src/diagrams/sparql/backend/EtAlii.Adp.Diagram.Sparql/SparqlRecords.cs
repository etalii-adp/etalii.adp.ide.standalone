using System.Text;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// The stored records of a query (DISL 4.11.1, FBL 11.2): what the reader behind the binding delivers, one record per
/// entry in document order, each with the span and the line it was read from. The drawn projection - the variables, the
/// terms, the edges, their ids, their order and the cut - is not here: it is the bundled definition's, derived from
/// these records by <see cref="SparqlDefinition"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The records and their attributes are the definition's stored types.</b> <c>Query</c> is mapped onto the diagram's
/// attributes and <c>Constraint</c> onto <c>Annotation</c> by its <c>persistence.typeMap</c>; <c>Prefix</c>, <c>Scope</c>,
/// <c>Pattern</c> and <c>SubQuery</c> are stored as they are named. A scope's record is a child of the scope it is
/// written in, and so is a subquery's; the where clause itself is the open canvas and has no record.
/// </para>
/// <para>
/// <b>Three things are decided here because DISL cannot decide them</b>, each named in the definition's companion:
/// whether a prefix is used, asked of the source text, comments and strings included (finding A7); a term's key, its
/// display, its full form and its annotation, a literal's key percent-encoded (gap 6); and a pattern's <c>repeat</c>,
/// how many patterns before it in the order the tool walks them have the same subject, predicate as written and object,
/// which counted per pattern in CEL is quadratic.
/// </para>
/// </remarks>
internal static class SparqlRecords
{
    /// <summary>The read id of the one <c>Query</c> record.</summary>
    private const string QueryId = "query";

    /// <summary>The where clause's scope path: the open canvas, which has no record of its own.</summary>
    private const string WherePath = "where";

    /// <summary>The records of <paramref name="model"/>, read from <paramref name="text"/>, in document order.</summary>
    public static FblModel From(SparqlQueryModel model, string text)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(text);
        return new FblModel(new Reader(model, text).Read(), [], false);
    }

    private sealed class Reader(SparqlQueryModel model, string text)
    {
        private readonly List<FblElement> _records = [];
        private readonly int[]? _byteOffsets = ByteOffsets(text);
        private readonly Dictionary<TriplePattern, int> _repeats = new(ReferenceEqualityComparer.Instance);
        private int _patterns;
        private int _prefixes;

        /// <summary>The span of the characters <paramref name="span"/> covers, in the UTF-8 bytes FBL's spans count (FBL 6.1).</summary>
        private Span ByteSpan(SparqlSpan span) => ByteSpan(span.Start, span.End);

        /// <summary>The span of the characters from <paramref name="start"/> to <paramref name="end"/>, in UTF-8 bytes.</summary>
        private Span ByteSpan(int start, int end) => _byteOffsets is null ? new Span(start, end) : new Span(_byteOffsets[start], _byteOffsets[end]);

        public List<FblElement> Read()
        {
            var repeatsWhere = RepeatsWhere();
            Repeats(repeatsWhere);

            _records.Add(Record(QueryId, "Query", Query(repeatsWhere), null, ByteSpan(0, text.Length), 1));
            foreach (var prefix in model.Prefixes)
            {
                _records.Add(Record($"prefix-{_prefixes++}", "Prefix", Attributes(
                    ("prefix", prefix.Prefix),
                    ("iri", prefix.Iri),
                    ("used", IsUsed(prefix.Prefix))), null, ByteSpan(prefix.Span), prefix.Line));
            }

            // The template of CONSTRUCT is written before the where clause; the short form's shares the where
            // clause's text and states no entry of its own.
            if (model.Template is { } template)
            {
                Scope(template, null, ownEntries: !repeatsWhere);
            }

            Entries(model.Where);
            return _records;
        }

        /// <summary>The diagram's attributes: the query's form, its projection, its targets and its modifiers.</summary>
        private Dictionary<string, object?> Query(bool repeatsWhere) => Attributes(
            ("form", model.Form.ToString().ToLowerInvariant()),
            ("distinct", model.Distinct),
            ("reduced", model.Reduced),
            ("projectsAll", model.ProjectsAll),
            ("projection", model.Projection.Select(item => item.Name).ToList()),
            ("projectionExpressions", model.Projection.Select(item => item.ExpressionText).ToList()),
            ("describeTargets", model.DescribeTargets.ToList()),
            ("describeTerms", model.DescribeTerms.Select(DescribeTerm).ToList()),
            ("datasetClauses", model.DatasetClauses.ToList()),
            ("modifierRows", model.ModifierRows.ToList()),
            ("templateRepeatsWhere", repeatsWhere),
            ("baseIri", model.BaseIri));

        /// <summary>A group's record, then its entries.</summary>
        private void Scope(GroupScope scope, string? parent, bool ownEntries = true)
        {
            _records.Add(Record(scope.Path, "Scope", Attributes(
                ("path", scope.Path),
                ("scopeKind", scope.Kind.ToString().ToLowerInvariant()),
                ("term", scope.Label)), parent, ByteSpan(scope.Span), scope.Span.Line));
            if (ownEntries)
            {
                Entries(scope);
            }
        }

        /// <summary>
        /// The entries <paramref name="scope"/> states, in the order they are written: its patterns, constraints,
        /// subqueries and groups merged by where each starts, each kind keeping the order the parser gave it. A
        /// subquery and a group are children of the group's record; the where clause has none.
        /// </summary>
        private void Entries(GroupScope scope)
        {
            var parent = scope.Path == WherePath ? null : scope.Path;
            var queues = new List<Queue<(SparqlSpan Span, Action Add)>>
            {
                new(scope.Patterns.Select(pattern => (pattern.Span, (Action)(() => Pattern(pattern, scope.Path))))),
                new(scope.Constraints.Select((constraint, index) => (constraint.Span, (Action)(() => Constraint(scope, index))))),
                new(scope.SubSelects.Select(subSelect => (subSelect.Span, (Action)(() => SubQuery(subSelect, scope.Path, parent))))),
                new(scope.Children.Select(child => (child.Span, (Action)(() => Scope(child, parent))))),
            };
            while (queues.Where(queue => queue.Count > 0).MinBy(queue => queue.Peek().Span.Start) is { } next)
            {
                next.Dequeue().Add();
            }
        }

        private void Pattern(TriplePattern pattern, string scope)
        {
            var attributes = Attributes(
                ("scope", scope),
                ("predicate", pattern.Predicate.AsWritten),
                ("isPath", pattern.Predicate is PathTerm),
                ("predicateVariable", pattern.Predicate is VariableTerm variable ? variable.Name : ""),
                ("repeat", (long)_repeats[pattern]));
            End(attributes, "s", pattern.Subject);
            End(attributes, "o", pattern.Object);
            _records.Add(Record($"pattern-{_patterns++}", "Pattern", attributes, null, ByteSpan(pattern.Span), pattern.Span.Line));
        }

        private void Constraint(GroupScope scope, int index)
        {
            var constraint = scope.Constraints[index];
            var ordinal = scope.Constraints.Take(index).Count(earlier => earlier.Kind == constraint.Kind);
            _records.Add(Record($"{scope.Path}/constraint.{index}", "Constraint", Attributes(
                ("constraintKind", constraint.Kind.ToString().ToLowerInvariant()),
                ("text", constraint.Text),
                ("scope", scope.Path),
                ("ordinal", (long)ordinal),
                ("definedVariable", constraint.DefinedVariable),
                ("referencedVariables", constraint.ReferencedVariables.ToList())), null, ByteSpan(constraint.Span), constraint.Span.Line));
        }

        private void SubQuery(SparqlSubSelect subSelect, string scope, string? parent)
        {
            _records.Add(Record($"{scope}/subquery.{subSelect.Ordinal}", "SubQuery", Attributes(
                ("scope", scope),
                ("ordinal", (long)subSelect.Ordinal),
                ("display", subSelect.ProjectionText),
                ("text", subSelect.Text),
                ("projectedNames", subSelect.ProjectedNames.ToList())), parent, ByteSpan(subSelect.Span), subSelect.Span.Line));
        }

        /// <summary>Whether the template of <c>CONSTRUCT WHERE</c> is the where clause's own patterns, stated once.</summary>
        private bool RepeatsWhere() =>
            model.Template is { Patterns.Count: > 0 } template
            && template.Patterns.Count == model.Where.Patterns.Count
            && template.Patterns.Zip(model.Where.Patterns).All(pair => ReferenceEquals(pair.First, pair.Second));

        /// <summary>
        /// Each pattern's <c>repeat</c>: how many patterns before it, in the order the tool walks them - the where clause
        /// and its groups depth first, then the template - have its subject, its predicate as written and its object.
        /// The template's copies of the where clause's patterns are the definition's to count (<c>repeatOf</c>).
        /// </summary>
        private void Repeats(bool repeatsWhere)
        {
            var seen = new Dictionary<(string, string, string), int>();
            Walk(model.Where);
            if (model.Template is { } template && !repeatsWhere)
            {
                Walk(template);
            }

            void Walk(GroupScope scope)
            {
                foreach (var pattern in scope.Patterns)
                {
                    var key = (Key(pattern.Subject), pattern.Predicate.AsWritten, Key(pattern.Object));
                    var count = seen.GetValueOrDefault(key);
                    _repeats[pattern] = count;
                    seen[key] = count + 1;
                }

                foreach (var child in scope.Children)
                {
                    Walk(child);
                }
            }
        }

        /// <summary>
        /// Whether the query uses <paramref name="prefix"/> anywhere outside its own declarations, asked of the source
        /// text rather than the model: a prefixed name can appear in an expression or a solution modifier, which the
        /// model keeps as written rather than as terms. An occurrence on a line that starts with <c>PREFIX</c> is a
        /// declaration, not a use.
        /// </summary>
        private bool IsUsed(string prefix)
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
                if (text[lineStart..at].TrimStart().StartsWith("PREFIX", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return true;
            }
        }
    }

    /// <summary>One end of a pattern, as the attributes <c>{end}Kind</c>, <c>{end}Key</c>, <c>{end}Display</c>, <c>{end}Full</c> and <c>{end}Annotation</c>.</summary>
    private static void End(Dictionary<string, object?> attributes, string end, SparqlTerm term)
    {
        attributes[end + "Kind"] = Kind(term);
        attributes[end + "Key"] = Key(term);
        attributes[end + "Display"] = Display(term);
        attributes[end + "Full"] = Full(term);
        attributes[end + "Annotation"] = Annotation(term);
    }

    /// <summary>A <c>DESCRIBE</c> target, as the definition's <c>DescribeTerm</c>.</summary>
    private static CelMap DescribeTerm(SparqlTerm term) => new()
    {
        ["kind"] = Kind(term),
        ["key"] = Key(term),
        ["display"] = Display(term),
        ["full"] = Full(term),
        ["annotation"] = Annotation(term),
    };

    private static string Kind(SparqlTerm term) => term switch
    {
        VariableTerm => "var",
        AnonymousTerm => "anon",
        IriTerm => "iri",
        _ => "lit",
    };

    /// <summary>The key a term is told apart by, which is also its node's id: a literal's lexical form and datatype percent-encoded (gap 6).</summary>
    private static string Key(SparqlTerm term) => term switch
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

    /// <summary>What a term's node shows: a prefixed name or an IRI as written, a literal's lexical form, a blank node as written.</summary>
    private static string Display(SparqlTerm term) => term is LiteralTerm literal ? literal.Lexical : term.AsWritten;

    /// <summary>An IRI's full form, or a literal as written.</summary>
    private static string Full(SparqlTerm term) => term switch
    {
        IriTerm iri => iri.Iri,
        LiteralTerm literal => literal.AsWritten,
        _ => "",
    };

    /// <summary>A literal's language tag or the local name of its datatype.</summary>
    private static string Annotation(SparqlTerm term)
    {
        if (term is not LiteralTerm literal)
        {
            return "";
        }

        if (literal.Language.Length > 0)
        {
            return $"@{literal.Language}";
        }

        return literal.DatatypeIri.Length > 0 ? ShortDatatype(literal.DatatypeIri) : "";
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

    /// <summary>
    /// The UTF-8 byte offset of every character offset of <paramref name="text"/>, or null when the two are the same,
    /// as they are for every query that is ASCII. A surrogate pair is four bytes, counted at its first half, and a lone
    /// surrogate the three of the replacement character it is encoded as.
    /// </summary>
    private static int[]? ByteOffsets(string text)
    {
        if (Ascii.IsValid(text)) return null;

        var offsets = new int[text.Length + 1];
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            var bytes = character switch
            {
                < (char)0x80 => 1,
                < (char)0x800 => 2,
                _ when char.IsHighSurrogate(character) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]) => 4,
                _ when char.IsLowSurrogate(character) && index > 0 && char.IsHighSurrogate(text[index - 1]) => 0,
                _ => 3,
            };
            offsets[index + 1] = offsets[index] + bytes;
        }
        return offsets;
    }

    private static FblElement Record(string id, string type, IReadOnlyDictionary<string, object?> attributes, string? parent, Span span, int line) =>
        new(id, false, type, type, false, attributes, parent, parent is null ? null : "children", null, null, span, line);


    private static Dictionary<string, object?> Attributes(params (string Name, object? Value)[] attributes) =>
        attributes.ToDictionary(attribute => attribute.Name, attribute => attribute.Value, StringComparer.Ordinal);
}
