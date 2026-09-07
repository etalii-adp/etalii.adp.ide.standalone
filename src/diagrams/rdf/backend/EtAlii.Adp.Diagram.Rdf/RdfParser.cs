using System.Globalization;
using System.Text;
using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// Reads a document's text into the model every reading of the family shares: triples with source
/// spans, prefix declarations, and the base IRI. Turtle in full - prefixes, base, prefixed names,
/// <c>a</c>, predicate lists, object lists, anonymous and labeled blank nodes, collections, typed
/// and language-tagged literals, comments - and N-Triples for free, being a subset.
/// </summary>
/// <remarks>
/// Recursive descent over <see cref="RdfTokenizer"/>'s tokens, hand-rolled because the splice
/// discipline needs spans - the line range each triple occupies and, on a shared line, the exact
/// character fragment - and no available library reports positions at that grain. The parser
/// never rewrites anything: it reads, records where, and leaves the lines to the writers.
/// </remarks>
public sealed class RdfParser
{
    private readonly RdfDocument _document;
    private readonly RdfTokenizer _tokenizer;
    private readonly List<RdfTriple> _triples = [];
    private readonly List<PrefixDeclaration> _declarations = [];
    private readonly Dictionary<string, string> _prefixes = [];
    private readonly Dictionary<string, BlankTerm> _labeledBlanks = [];
    private RdfToken _current;
    private string? _base;
    private int _baseLine = -1;
    private int _blankOrdinal;

    private RdfParser(RdfDocument document)
    {
        _document = document;
        _tokenizer = new RdfTokenizer(document.Text);
        _current = _tokenizer.Next();
    }

    /// <summary>
    /// What <paramref name="document"/> states. Throws <see cref="RdfParseException"/> naming line
    /// and reason where it is not valid Turtle; the store turns that into the unavailable state.
    /// </summary>
    public static RdfModel Parse(RdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new RdfParser(document).ParseDocument();
    }

    private RdfModel ParseDocument()
    {
        while (_current.Kind != RdfTokenKind.EndOfFile)
        {
            switch (_current.Kind)
            {
                case RdfTokenKind.PrefixDirective:
                    ParsePrefixDirective(expectDot: true);
                    break;
                case RdfTokenKind.SparqlPrefix:
                    ParsePrefixDirective(expectDot: false);
                    break;
                case RdfTokenKind.BaseDirective:
                    ParseBaseDirective(expectDot: true);
                    break;
                case RdfTokenKind.SparqlBase:
                    ParseBaseDirective(expectDot: false);
                    break;
                default:
                    ParseStatement();
                    break;
            }
        }

        return new RdfModel(_triples, _declarations, _base, _baseLine);
    }

    private void ParsePrefixDirective(bool expectDot)
    {
        var directive = _current;
        Advance();

        if (_current.Kind != RdfTokenKind.PrefixedName || !_current.Value.EndsWith(':'))
        {
            throw Error($"{directive.Value} expects a prefix ending in ':'.");
        }

        var prefix = _current.Value[..^1];
        Advance();

        if (_current.Kind != RdfTokenKind.Iri)
        {
            throw Error($"{directive.Value} expects a '<...>' IRI after the prefix.");
        }

        var iri = ResolveIri(DecodeIri(_current.Value));
        Advance();

        if (expectDot)
        {
            Expect(RdfTokenKind.Dot, $"{directive.Value} ends with a '.'");
        }

        _prefixes[prefix] = iri;
        _declarations.Add(new PrefixDeclaration(prefix, iri, _tokenizer.LineOf(directive.Start)));
    }

    private void ParseBaseDirective(bool expectDot)
    {
        var directive = _current;
        Advance();

        if (_current.Kind != RdfTokenKind.Iri)
        {
            throw Error($"{directive.Value} expects a '<...>' IRI.");
        }

        // A new base resolves against the one before it, so a document can narrow in steps.
        _base = ResolveIri(DecodeIri(_current.Value));
        _baseLine = _tokenizer.LineOf(directive.Start);
        Advance();

        if (expectDot)
        {
            Expect(RdfTokenKind.Dot, $"{directive.Value} ends with a '.'");
        }
    }

    private void ParseStatement()
    {
        var statementStart = _current.Start;
        var pending = new List<RdfPendingTriple>();

        var subjectIsPropertyList = _current.Kind == RdfTokenKind.OpenBracket;
        var subject = ParseSubject(pending);

        // '[ p o ] .' is a complete statement: the property list already stated its triples.
        if (!subjectIsPropertyList || _current.Kind != RdfTokenKind.Dot)
        {
            ParsePredicateObjectList(subject, pending);
        }

        var dot = _current;
        Expect(RdfTokenKind.Dot, "a statement ends with a '.'");

        var statement = new LineRange(_tokenizer.LineOf(statementStart), _tokenizer.LineOf(dot.Start));
        foreach (var triple in pending)
        {
            _triples.Add(new RdfTriple(
                triple.Subject,
                triple.Predicate,
                triple.Object,
                SpanOf(triple.Start, triple.End),
                statement,
                triple.Start,
                triple.End,
                triple.ObjectStart,
                triple.End,
                dot.Start));
        }
    }

    private RdfTerm ParseSubject(List<RdfPendingTriple> pending) =>
        _current.Kind switch
        {
            RdfTokenKind.Iri or RdfTokenKind.PrefixedName => ParseIriTerm(),
            RdfTokenKind.BlankNodeLabel => ParseLabeledBlank(),
            RdfTokenKind.OpenBracket => ParseBlankPropertyList(pending).Term,
            RdfTokenKind.OpenParen => ParseCollection(pending).Term,
            _ => throw Error($"Expected a subject, found '{_current.Value}'.")
        };

    private void ParsePredicateObjectList(RdfTerm subject, List<RdfPendingTriple> pending)
    {
        while (true)
        {
            var verbToken = _current;
            var verb = ParseVerb();
            ParseObjectList(subject, verb, verbToken.Start, pending);

            if (_current.Kind != RdfTokenKind.Semicolon)
            {
                return;
            }

            while (_current.Kind == RdfTokenKind.Semicolon)
            {
                Advance();
            }

            // Turtle allows a trailing ';' before the '.' or the ']', with nothing after it.
            if (_current.Kind is RdfTokenKind.Dot or RdfTokenKind.CloseBracket)
            {
                return;
            }
        }
    }

    private IriTerm ParseVerb() =>
        _current.Kind switch
        {
            RdfTokenKind.A => ParseTypeKeyword(),
            RdfTokenKind.Iri or RdfTokenKind.PrefixedName => ParseIriTerm(),
            _ => throw Error($"Expected a predicate, found '{_current.Value}'.")
        };

    private IriTerm ParseTypeKeyword()
    {
        Advance();
        return new IriTerm(RdfVocabulary.Type, "a");
    }

    private void ParseObjectList(RdfTerm subject, IriTerm verb, int pairStart, List<RdfPendingTriple> pending)
    {
        var first = true;
        while (true)
        {
            var objectStart = _current.Start;
            var (term, end) = ParseObject(pending);
            pending.Add(new RdfPendingTriple(subject, verb, term, first ? pairStart : objectStart, end, objectStart));

            if (_current.Kind != RdfTokenKind.Comma)
            {
                return;
            }

            Advance();
            first = false;
        }
    }

    private (RdfTerm Term, int End) ParseObject(List<RdfPendingTriple> pending)
    {
        switch (_current.Kind)
        {
            case RdfTokenKind.Iri or RdfTokenKind.PrefixedName:
            {
                var token = _current;
                return (ParseIriTerm(), token.End);
            }
            case RdfTokenKind.BlankNodeLabel:
            {
                var token = _current;
                return (ParseLabeledBlank(), token.End);
            }
            case RdfTokenKind.String:
                return ParseLiteral();
            case RdfTokenKind.Number:
            {
                var token = _current;
                Advance();
                var datatype = token.Value.Contains('e') || token.Value.Contains('E')
                    ? RdfVocabulary.XsdDouble
                    : token.Value.Contains('.') ? RdfVocabulary.XsdDecimal : RdfVocabulary.XsdInteger;
                return (new LiteralTerm(token.Value, datatype, null, token.Value), token.End);
            }
            case RdfTokenKind.Boolean:
            {
                var token = _current;
                Advance();
                return (new LiteralTerm(token.Value, RdfVocabulary.XsdBoolean, null, token.Value), token.End);
            }
            case RdfTokenKind.OpenBracket:
                return ParseBlankPropertyList(pending);
            case RdfTokenKind.OpenParen:
                return ParseCollection(pending);
            default:
                throw Error($"Expected an object, found '{_current.Value}'.");
        }
    }

    private (RdfTerm Term, int End) ParseLiteral()
    {
        var stringToken = _current;
        Advance();

        var lexical = DecodeString(stringToken.Value);

        if (_current.Kind == RdfTokenKind.LanguageTag)
        {
            var tag = _current;
            Advance();
            var written = Slice(stringToken.Start, tag.End);
            return (new LiteralTerm(lexical, RdfVocabulary.LangString, tag.Value, written), tag.End);
        }

        if (_current.Kind == RdfTokenKind.DoubleCaret)
        {
            Advance();
            if (_current.Kind is not (RdfTokenKind.Iri or RdfTokenKind.PrefixedName))
            {
                throw Error("'^^' expects a datatype IRI.");
            }

            var datatypeToken = _current;
            var datatype = ParseIriTerm();
            var written = Slice(stringToken.Start, datatypeToken.End);
            return (new LiteralTerm(lexical, datatype.Iri, null, written), datatypeToken.End);
        }

        return (new LiteralTerm(lexical, null, null, stringToken.Value), stringToken.End);
    }

    private IriTerm ParseIriTerm()
    {
        var token = _current;
        Advance();

        if (token.Kind == RdfTokenKind.Iri)
        {
            return new IriTerm(ResolveIri(DecodeIri(token.Value)), token.Value);
        }

        var colon = token.Value.IndexOf(':');
        var prefix = token.Value[..colon];
        if (!_prefixes.TryGetValue(prefix, out var expansion))
        {
            throw Error($"The prefix '{prefix}:' is not declared.");
        }

        return new IriTerm(expansion + DecodeLocal(token.Value[(colon + 1)..]), token.Value);
    }

    private BlankTerm ParseLabeledBlank()
    {
        var label = _current.Value[2..];
        Advance();

        if (_labeledBlanks.TryGetValue(label, out var existing))
        {
            return existing;
        }

        var term = new BlankTerm(label, _blankOrdinal++);
        _labeledBlanks[label] = term;
        return term;
    }

    private (RdfTerm Term, int End) ParseBlankPropertyList(List<RdfPendingTriple> pending)
    {
        Advance(); // [
        var blank = new BlankTerm(null, _blankOrdinal++);

        if (_current.Kind == RdfTokenKind.CloseBracket)
        {
            var closing = _current;
            Advance();
            return (blank, closing.End);
        }

        ParsePredicateObjectList(blank, pending);

        var close = _current;
        Expect(RdfTokenKind.CloseBracket, "a '[' property list ends with ']'");
        return (blank, close.End);
    }

    private (RdfTerm Term, int End) ParseCollection(List<RdfPendingTriple> pending)
    {
        Advance(); // (

        if (_current.Kind == RdfTokenKind.CloseParen)
        {
            var closing = _current;
            Advance();
            return (new IriTerm(RdfVocabulary.Nil, "()"), closing.End);
        }

        var first = new IriTerm(RdfVocabulary.First, "rdf:first");
        var rest = new IriTerm(RdfVocabulary.Rest, "rdf:rest");
        BlankTerm? head = null;
        BlankTerm? previous = null;
        var previousSpan = (Start: 0, End: 0);

        while (_current.Kind != RdfTokenKind.CloseParen)
        {
            var node = new BlankTerm(null, _blankOrdinal++);
            head ??= node;

            var elementStart = _current.Start;
            var (element, elementEnd) = ParseObject(pending);

            if (previous is not null)
            {
                pending.Add(new RdfPendingTriple(previous, rest, node, previousSpan.Start, previousSpan.End, previousSpan.Start));
            }

            pending.Add(new RdfPendingTriple(node, first, element, elementStart, elementEnd, elementStart));
            previous = node;
            previousSpan = (elementStart, elementEnd);
        }

        pending.Add(new RdfPendingTriple(previous!, rest, new IriTerm(RdfVocabulary.Nil, "rdf:nil"), previousSpan.Start, previousSpan.End, previousSpan.Start));

        var close = _current;
        Advance();
        return (head!, close.End);
    }

    private SourceSpan SpanOf(int start, int end)
    {
        var startLine = _tokenizer.LineOf(start);
        var endLine = _tokenizer.LineOf(end - 1);
        if (startLine != endLine)
        {
            return new SourceSpan(startLine, endLine, null, null);
        }

        var lineStart = _tokenizer.StartOfLine(startLine);
        var lineText = _document.Lines[startLine].Text;
        var startColumn = start - lineStart;
        var endColumn = end - lineStart;

        var owns = !IsSubstantiveBefore(lineText[..startColumn]) && !IsSubstantiveAfter(lineText[endColumn..]);
        return owns
            ? new SourceSpan(startLine, endLine, null, null)
            : new SourceSpan(startLine, endLine, startColumn, endColumn);
    }

    /// <summary>
    /// Whether the text before a triple's tokens on its line is more than whitespace and list
    /// separators - the subject, or an earlier triple - so the line cannot be removed whole.
    /// </summary>
    private static bool IsSubstantiveBefore(string before) =>
        before.Any(character => !char.IsWhiteSpace(character) && character is not (',' or ';'));

    /// <summary>
    /// Whether the text after a triple's tokens on its line is more than whitespace, separators,
    /// the statement terminator and a trailing comment - all of which the splicing writer manages
    /// itself when it takes the line.
    /// </summary>
    private static bool IsSubstantiveAfter(string after)
    {
        var remaining = after.TrimStart();
        while (remaining.Length > 0 && remaining[0] is ',' or ';' or '.')
        {
            remaining = remaining[1..].TrimStart();
        }

        return remaining.Length > 0 && remaining[0] != '#';
    }

    private string Slice(int start, int end) => _document.Text[start..end];

    private void Advance() => _current = _tokenizer.Next();

    // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Local
    // Reason: This works.
    private void Expect(RdfTokenKind kind, string expectation)
    {
        if (_current.Kind != kind)
        {
            throw Error($"Expected {expectation}, found '{(_current.Kind == RdfTokenKind.EndOfFile ? "end of file" : _current.Value)}'.");
        }

        Advance();
    }

    private RdfParseException Error(string message) =>
        new(message, _tokenizer.LineOf(_current.Kind == RdfTokenKind.EndOfFile ? Math.Max(0, _document.Text.Length - 1) : _current.Start) + 1);

    private string ResolveIri(string iri)
    {
        if (iri.Length == 0)
        {
            // '<>' means the base itself, or stays empty in a base-less document.
            return _base ?? iri;
        }

        if (IsAbsolute(iri) || _base is null)
        {
            return iri;
        }

        return Uri.TryCreate(new Uri(_base, UriKind.Absolute), iri, out var resolved)
            ? resolved.AbsoluteUri
            : iri;
    }

    internal static bool IsAbsolute(string iri)
    {
        if (iri.Length == 0 || !char.IsAsciiLetter(iri[0]))
        {
            return false;
        }

        for (var i = 1; i < iri.Length; i++)
        {
            var character = iri[i];
            if (character == ':')
            {
                return true;
            }

            if (!char.IsAsciiLetterOrDigit(character) && character is not ('+' or '-' or '.'))
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>The inside of a <c>&lt;...&gt;</c> token with its numeric escapes decoded - the only escapes an IRI may carry.</summary>
    private static string DecodeIri(string raw) => DecodeNumericEscapes(raw[1..^1]);

    /// <summary>A prefixed name's local part with its reserved-character escapes dropped: <c>\.</c> written for <c>.</c>, and so on.</summary>
    private static string DecodeLocal(string raw)
    {
        if (!raw.Contains('\\'))
        {
            return raw;
        }

        var builder = new StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '\\' && i + 1 < raw.Length)
            {
                i++;
            }

            builder.Append(raw[i]);
        }

        return builder.ToString();
    }

    /// <summary>A string token's value: quotes stripped, escapes decoded.</summary>
    private static string DecodeString(string raw)
    {
        var quote = raw[0];
        var isLong = raw.Length >= 6 && raw[1] == quote && raw[2] == quote;
        var inner = isLong ? raw[3..^3] : raw[1..^1];

        if (!inner.Contains('\\'))
        {
            return inner;
        }

        var builder = new StringBuilder(inner.Length);
        for (var i = 0; i < inner.Length; i++)
        {
            if (inner[i] != '\\' || i + 1 >= inner.Length)
            {
                builder.Append(inner[i]);
                continue;
            }

            i++;
            switch (inner[i])
            {
                case 't': builder.Append('\t'); break;
                case 'b': builder.Append('\b'); break;
                case 'n': builder.Append('\n'); break;
                case 'r': builder.Append('\r'); break;
                case 'f': builder.Append('\f'); break;
                case 'u' or 'U':
                    var length = inner[i] == 'u' ? 4 : 8;
                    AppendCodePoint(builder, inner, ref i, length);
                    break;
                default: builder.Append(inner[i]); break;
            }
        }

        return builder.ToString();
    }

    private static string DecodeNumericEscapes(string value)
    {
        if (!value.Contains('\\'))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length && value[i + 1] is 'u' or 'U')
            {
                var length = value[i + 1] == 'u' ? 4 : 8;
                i++;
                AppendCodePoint(builder, value, ref i, length);
                continue;
            }

            builder.Append(value[i]);
        }

        return builder.ToString();
    }

    private static void AppendCodePoint(StringBuilder builder, string source, ref int index, int digits)
    {
        if (index + digits < source.Length
            && int.TryParse(source.AsSpan(index + 1, digits), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var codePoint))
        {
            builder.Append(char.ConvertFromUtf32(codePoint));
            index += digits;
        }
        else
        {
            builder.Append(source[index]);
        }
    }
}
