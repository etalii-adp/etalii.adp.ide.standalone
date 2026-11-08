using System.Globalization;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// What <see cref="RdfTurtlePlugin"/> delivers for one body that parses: the parser's model as the stored
/// <c>Triple</c>, <c>Prefix</c> and <c>Base</c> elements of the four definitions, each with its span and the spans
/// of the values it alone writes, as UTF-8 byte ranges of the body's bytes.
/// </summary>
/// <remarks>
/// <para>
/// <b>The parser records triples, not statements</b>, so the statements are found again in the tokens: a directive
/// is its fixed run of tokens, and any other run ends at its <c>.</c>, the one token that never appears inside a
/// statement. A triple's statement is the run whose <c>.</c> is its <see cref="RdfTriple.TerminatorOffset"/>.
/// </para>
/// <para>
/// <b>A value span is checked against the token it names</b>: the token must spell the value as the triple has it,
/// which drops the triples a collection abbreviates, whose recorded offsets are the neighbouring element's. A span
/// two elements would still claim is delivered to neither.
/// </para>
/// </remarks>
internal sealed class RdfTurtleReading
{
    private readonly RdfModel _model;
    private readonly List<RdfToken> _tokens = [];
    private readonly Dictionary<int, int> _tokenAt = [];
    private readonly Dictionary<int, int> _statementAt = [];
    private readonly Dictionary<(int Terminator, RdfTerm Subject), int> _subjects = [];
    private readonly List<(int Start, int End)> _prefixSpans = [];
    private readonly int[] _bytes;
    private (int Start, int End)? _baseSpan;

    /// <param name="text">The body's text, after any byte-order mark.</param>
    /// <param name="mark">How many bytes of byte-order mark come before <paramref name="text"/>.</param>
    /// <param name="model">What the parser read from <paramref name="text"/>.</param>
    public RdfTurtleReading(string text, int mark, RdfModel model)
    {
        _model = model;
        _bytes = ByteOffsets(text, mark);
        var tokenizer = new RdfTokenizer(text);
        for (var token = tokenizer.Next(); token.Kind != RdfTokenKind.EndOfFile; token = tokenizer.Next())
        {
            _tokenAt[token.Start] = _tokens.Count;
            _tokens.Add(token);
        }

        Statements();
        foreach (var triple in model.Triples)
        {
            _subjects[(triple.TerminatorOffset, triple.Subject)] = _subjects.GetValueOrDefault((triple.TerminatorOffset, triple.Subject)) + 1;
        }
    }

    /// <summary>Every stored element, in document order: by where its span starts, a statement's triples in the parser's order.</summary>
    public IReadOnlyList<FblElement> Elements()
    {
        List<FblElement> elements = [];
        var claims = new Dictionary<(int Start, int End), int>();
        List<(RdfTriple Triple, Dictionary<string, (int Start, int End)> Values)> triples = [];
        foreach (var triple in _model.Triples)
        {
            var values = ValueSpans(triple);
            foreach (var span in values.Values)
            {
                claims[span] = claims.GetValueOrDefault(span) + 1;
            }

            triples.Add((triple, values));
        }

        for (var index = 0; index < triples.Count; index++)
        {
            (RdfTriple triple, Dictionary<string, (int Start, int End)> values) = triples[index];
            var owned = values.Where(value => claims[value.Value] == 1).ToDictionary(value => value.Key, value => Bytes(value.Value), StringComparer.Ordinal);
            var start = _statementAt[triple.TerminatorOffset];
            elements.Add(new FblElement($"triple#{index}", false, "Triple", "triple", false, TripleAttributes(triple), null, null, null, null, Bytes((start, triple.TerminatorOffset + 1)), triple.Span.StartLine + 1)
            {
                ValueSpans = owned,
            });
        }

        for (var index = 0; index < _model.Prefixes.Count; index++)
        {
            var declaration = _model.Prefixes[index];
            var span = _prefixSpans[index];
            var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = declaration.Prefix,
                ["iri"] = declaration.Iri,
                ["form"] = _tokens[_tokenAt[span.Start]].Kind == RdfTokenKind.SparqlPrefix ? "sparql" : "turtle",
                ["line"] = (long)(declaration.Line + 1),
            };
            elements.Add(new FblElement($"prefix#{index}", false, "Prefix", "prefix", false, attributes, null, null, null, null, Bytes(span), declaration.Line + 1));
        }

        if (_model.BaseIri is { } iri && _baseSpan is { } baseSpan)
        {
            var attributes = new Dictionary<string, object?>(StringComparer.Ordinal) { ["iri"] = iri };
            elements.Add(new FblElement("base", false, "Base", "base", false, attributes, null, null, null, null, Bytes(baseSpan), _model.BaseLine + 1));
        }

        return [.. elements.OrderBy(element => element.OwnSpan.Start)];
    }

    /// <summary>A triple's stored attributes, as the four definitions' <c>Triple</c> declares them.</summary>
    private static Dictionary<string, object?> TripleAttributes(RdfTriple triple)
    {
        var literal = triple.Object as LiteralTerm;
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["s"] = ValueOf(triple.Subject),
            ["sKind"] = KindOf(triple.Subject),
            ["sLabel"] = (triple.Subject as BlankTerm)?.Label ?? "",
            ["p"] = triple.Predicate.Iri,
            ["o"] = ValueOf(triple.Object),
            ["oKind"] = KindOf(triple.Object),
            ["oLabel"] = (triple.Object as BlankTerm)?.Label ?? "",
            ["language"] = literal?.Language ?? "",
            ["datatype"] = literal?.DatatypeIri ?? "",
            ["line"] = (long)(triple.Span.StartLine + 1),
        };
    }

    private static string ValueOf(RdfTerm term) => term switch
    {
        IriTerm iri => iri.Iri,
        BlankTerm blank => blank.Ordinal.ToString(CultureInfo.InvariantCulture),
        LiteralTerm literal => literal.Lexical,
        _ => throw new ArgumentOutOfRangeException(nameof(term), term, "An RDF term is an IRI, a blank node or a literal."),
    };

    private static string KindOf(RdfTerm term) => term switch
    {
        IriTerm => "iri",
        BlankTerm => "blank",
        _ => "literal",
    };

    /// <summary>
    /// The spans, in UTF-16 offsets, of the values <paramref name="triple"/> writes in a token of its own: its
    /// object, with the language tag or the datatype written with it, its predicate unless a <c>,</c> list shares
    /// it, and its subject unless a <c>;</c> list shares it.
    /// </summary>
    private Dictionary<string, (int Start, int End)> ValueSpans(RdfTriple triple)
    {
        var spans = new Dictionary<string, (int Start, int End)>(StringComparer.Ordinal);
        if (ObjectToken(triple) is { } objectToken)
        {
            var token = _tokens[objectToken];
            spans["o"] = (token.Start, token.End);
            if (triple.Object is LiteralTerm { Language: { } language } && At(objectToken + 1) is { Kind: RdfTokenKind.LanguageTag } tag && tag.Value == language)
            {
                // The tag's token starts at its '@', which is syntax and not the value.
                spans["language"] = (tag.Start + 1, tag.End);
            }
            else if (triple.Object is LiteralTerm { DatatypeIri: not null } && At(objectToken + 1) is { Kind: RdfTokenKind.DoubleCaret } && At(objectToken + 2) is { Kind: RdfTokenKind.Iri or RdfTokenKind.PrefixedName } datatype)
            {
                spans["datatype"] = (datatype.Start, datatype.End);
            }
        }

        if (!triple.IsListContinuation && TokenAt(triple.SpanStart) is { } predicate
            && _tokens[predicate].Value == triple.Predicate.AsWritten && Next(triple.ObjectEnd) is not { Kind: RdfTokenKind.Comma })
        {
            spans["p"] = (_tokens[predicate].Start, _tokens[predicate].End);
        }

        var first = _tokens[_tokenAt[_statementAt[triple.TerminatorOffset]]];
        if (Written(triple.Subject) is { } subject && first.Value == subject
            && _subjects[(triple.TerminatorOffset, triple.Subject)] == 1)
        {
            spans["s"] = (first.Start, first.End);
        }

        return spans;
    }

    /// <summary>The index of the token that spells <paramref name="triple"/>'s object, or null when no one token does.</summary>
    private int? ObjectToken(RdfTriple triple)
    {
        if (TokenAt(triple.ObjectStart) is not { } index)
        {
            return null;
        }

        var token = _tokens[index];
        var spelled = (triple.Object, token.Kind) switch
        {
            (IriTerm iri, RdfTokenKind.Iri or RdfTokenKind.PrefixedName) => token.Value == iri.AsWritten,
            (BlankTerm { Label: { } label }, RdfTokenKind.BlankNodeLabel) => token.Value == "_:" + label,
            (LiteralTerm literal, RdfTokenKind.String) => literal.AsWritten.StartsWith(token.Value, StringComparison.Ordinal),
            (LiteralTerm literal, RdfTokenKind.Number or RdfTokenKind.Boolean) => token.Value == literal.AsWritten,
            _ => false,
        };
        return spelled ? index : null;
    }

    /// <summary>How a subject that one token spells is written, or null for one no token spells, such as <c>[ ]</c>.</summary>
    private static string? Written(RdfTerm term) => term switch
    {
        IriTerm iri => iri.AsWritten,
        BlankTerm { Label: { } label } => "_:" + label,
        _ => null,
    };

    private int? TokenAt(int offset) => _tokenAt.TryGetValue(offset, out var index) ? index : null;

    private RdfToken? At(int index) => index < _tokens.Count ? _tokens[index] : null;

    /// <summary>The first token that starts at or after <paramref name="offset"/>, or null at the end.</summary>
    private RdfToken? Next(int offset)
    {
        var low = 0;
        var high = _tokens.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (_tokens[middle].Start < offset)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return At(low);
    }

    /// <summary>Finds the directives and the statements in the tokens, in document order.</summary>
    private void Statements()
    {
        var index = 0;
        while (index < _tokens.Count)
        {
            var token = _tokens[index];
            switch (token.Kind)
            {
                case RdfTokenKind.PrefixDirective:
                    _prefixSpans.Add((token.Start, _tokens[index + 3].End));
                    index += 4;
                    break;
                case RdfTokenKind.SparqlPrefix:
                    _prefixSpans.Add((token.Start, _tokens[index + 2].End));
                    index += 3;
                    break;
                case RdfTokenKind.BaseDirective:
                    _baseSpan = (token.Start, _tokens[index + 2].End);
                    index += 3;
                    break;
                case RdfTokenKind.SparqlBase:
                    _baseSpan = (token.Start, _tokens[index + 1].End);
                    index += 2;
                    break;
                default:
                {
                    var start = token.Start;
                    while (_tokens[index].Kind != RdfTokenKind.Dot)
                    {
                        index++;
                    }

                    _statementAt[_tokens[index].Start] = start;
                    index++;
                    break;
                }
            }
        }
    }

    /// <summary>A range of UTF-16 offsets into the text as a range of the body's bytes.</summary>
    private Span Bytes((int Start, int End) span) => new(_bytes[span.Start], _bytes[span.End]);

    /// <summary>The byte offset, a byte-order mark counted, of every UTF-16 offset into <paramref name="text"/>, its end included.</summary>
    private static int[] ByteOffsets(string text, int mark)
    {
        var offsets = new int[text.Length + 1];
        offsets[0] = mark;
        var index = 0;
        while (index < text.Length)
        {
            var character = text[index];
            if (char.IsHighSurrogate(character) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                // A pair is one character of four bytes; an offset between its halves is not a place a token starts.
                offsets[index + 1] = offsets[index] + 4;
                offsets[index + 2] = offsets[index] + 4;
                index += 2;
            }
            else
            {
                offsets[index + 1] = offsets[index] + (character < 0x80 ? 1 : character < 0x800 ? 2 : 3);
                index++;
            }
        }

        return offsets;
    }
}
