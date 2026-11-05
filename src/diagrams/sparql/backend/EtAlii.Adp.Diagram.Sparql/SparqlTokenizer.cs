using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// Cuts a query's text into tokens, each addressed by its exact character slice. Positions serve
/// two purposes only - reporting error lines, and slicing expression text as written - because
/// nothing in this module ever writes the query back.
/// </summary>
/// <remarks>
/// <para>
/// Hand-rolled, deliberately: the second parser tier needs exact source slices for expressions
/// and property paths, and a borrowed parser would hand back trees instead. Like its Turtle
/// sibling, it accepts names a little more loosely than the grammar's character tables - it is
/// not a validator, and a name the grammar would refuse still tokenizes so the parser can say
/// something better than "unexpected character".
/// </para>
/// <para>
/// The terminals SPARQL shares with Turtle - whitespace and comments, strings, numbers, blank node
/// labels and the local part of a prefixed name - are read by core's <see cref="TerminalScanner"/>
/// and <see cref="NumericLiteral"/>, so the two languages cannot drift apart on them again
/// (code-structure T132, behaviour changes B-02, B-17 and B-19). An IRI is the one shared terminal
/// read here: in a query '&lt;' is also less-than, so an IRI ends at the first whitespace or
/// quote, where Turtle's runs to its line's end.
/// </para>
/// </remarks>
internal sealed class SparqlTokenizer
{
    private readonly string _text;
    private readonly LineIndex _lines;
    private int _position;

    public SparqlTokenizer(string text)
    {
        _text = text;
        _lines = new LineIndex(text);
    }

    /// <summary>The zero-based line index holding <paramref name="offset"/>.</summary>
    public int LineOf(int offset) => _lines.LineOf(offset);

    /// <summary>The one-based column of <paramref name="offset"/> on its line.</summary>
    public int ColumnOf(int offset) => offset - _lines.StartOf(_lines.LineOf(offset)) + 1;

    /// <summary>Every token in the document, in order, ending with <see cref="SparqlTokenKind.EndOfFile"/>.</summary>
    public List<SparqlToken> Tokenize()
    {
        var tokens = new List<SparqlToken>();
        SparqlToken token;
        do
        {
            token = Next();
            tokens.Add(token);
        }
        while (token.Kind != SparqlTokenKind.EndOfFile);

        return tokens;
    }

    private SparqlToken Next()
    {
        SkipTrivia();

        if (_position >= _text.Length)
        {
            return new SparqlToken(SparqlTokenKind.EndOfFile, _position, 0, "");
        }

        var start = _position;
        var character = _text[_position];

        switch (character)
        {
            case '<':
                // '<' opens an IRI only when a closing '>' arrives before any whitespace or
                // quote - otherwise it is the less-than of an expression, which this module
                // never interprets but must still slice past correctly.
                return TryReadIri(start, out var iri) ? iri : ReadOperator(start);

            case '"' or '\'':
                return ReadString(start);
            case '?' or '$':
                if (_position + 1 < _text.Length && IsNameChar(_text[_position + 1]))
                {
                    return ReadVariable(start);
                }

                _position++;
                return new SparqlToken(SparqlTokenKind.Punct, start, 1, character.ToString());
            case '@':
                return ReadLanguageTag(start);
            case '_':
                if (_position + 1 < _text.Length && _text[_position + 1] == ':')
                {
                    return ReadBlankNodeLabel(start);
                }

                return ReadNameOrPrefixedName(start);
            case '.':
                // A dot opens a number only when a digit follows; alone it terminates a
                // triples block or dots a decimal nobody wrote a leading digit for.
                if (_position + 1 < _text.Length && char.IsAsciiDigit(_text[_position + 1]))
                {
                    return ReadNumber(start);
                }

                _position++;
                return new SparqlToken(SparqlTokenKind.Punct, start, 1, ".");
        }

        if (char.IsAsciiDigit(character))
        {
            return ReadNumber(start);
        }

        return IsNameStartChar(character) ? ReadNameOrPrefixedName(start) : ReadOperator(start);
    }

    private void SkipTrivia() => _position = TerminalScanner.SkipTrivia(_text, _position);

    private bool TryReadIri(int start, out SparqlToken token)
    {
        var position = start + 1;
        while (position < _text.Length)
        {
            var character = _text[position];
            if (character == '>')
            {
                _position = position + 1;
                var value = _text[start.._position];
                token = new SparqlToken(SparqlTokenKind.Iri, start, _position - start, value);
                return true;
            }

            if (character is '<' or '"' or '\'' or '{' or '}' or '|' or '^' or '`' || char.IsWhiteSpace(character))
            {
                break;
            }

            position++;
        }

        token = default;
        return false;
    }

    private SparqlToken ReadString(int start)
    {
        var scan = TerminalScanner.String(_text, start);
        _position = scan.End;
        return scan.Problem switch
        {
            TerminalProblem.None => Slice(SparqlTokenKind.String, start),
            TerminalProblem.LineEnded => throw Error("The string that starts here is not closed on its line.", start),
            _ => throw Error("The string that starts here is never closed.", start),
        };
    }

    private SparqlToken ReadVariable(int start)
    {
        _position++;
        while (_position < _text.Length && IsNameChar(_text[_position]))
        {
            _position++;
        }

        return Slice(SparqlTokenKind.Variable, start);
    }

    private SparqlToken ReadLanguageTag(int start)
    {
        _position++;
        while (_position < _text.Length && (char.IsAsciiLetterOrDigit(_text[_position]) || _text[_position] == '-'))
        {
            _position++;
        }

        return _position == start + 1
            ? throw Error("A lone '@' is not a SPARQL token; a language tag is '@' followed by letters.", start)
            : Slice(SparqlTokenKind.LanguageTag, start);
    }

    private SparqlToken ReadBlankNodeLabel(int start)
    {
        _position = TerminalScanner.LocalName(_text, start + 2); // _:
        return Slice(SparqlTokenKind.BlankNodeLabel, start);
    }

    private SparqlToken ReadNumber(int start)
    {
        // A sign is never read here: in a query '+' and '-' are operators first, and a signed
        // number reaches the parser as the operator and then the number.
        _position = NumericLiteral.Scan(_text, start);
        return Slice(SparqlTokenKind.Number, start);
    }

    private SparqlToken ReadNameOrPrefixedName(int start)
    {
        while (_position < _text.Length && IsNameChar(_text[_position]))
        {
            _position++;
        }

        if (_position < _text.Length && _text[_position] == ':')
        {
            // A prefixed name: what came before the colon is the prefix (possibly empty is
            // handled by the operator path, since ':' alone starts there), and the local part
            // follows directly.
            _position = TerminalScanner.LocalName(_text, _position + 1);
            return Slice(SparqlTokenKind.PrefixedName, start);
        }

        var value = _text[start.._position];
        return value is "true" or "false"
            ? new SparqlToken(SparqlTokenKind.Boolean, start, _position - start, value)
            : new SparqlToken(SparqlTokenKind.Name, start, _position - start, value);
    }

    private SparqlToken ReadOperator(int start)
    {
        var character = _text[_position];

        if (character == ':')
        {
            // The default prefix's bare form, ':thing' - or a lone ':' the parser will refuse
            // in context.
            _position = TerminalScanner.LocalName(_text, _position + 1);
            return Slice(SparqlTokenKind.PrefixedName, start);
        }

        // Two-character operators first, so '<=' never tokenizes as '<' '='. These are only
        // ever carried as written inside sliced expressions - the parser never interprets them.
        if (_position + 1 < _text.Length)
        {
            var pair = _text.Substring(_position, 2);
            if (pair is "^^" or "<=" or ">=" or "!=" or "&&" or "||")
            {
                _position += 2;
                return new SparqlToken(SparqlTokenKind.Punct, start, 2, pair);
            }
        }

        if (character is '{' or '}' or '(' or ')' or '[' or ']' or ';' or ',' or '|' or '/' or '^'
            or '*' or '+' or '!' or '=' or '<' or '>' or '-' or '?' or '$')
        {
            _position++;
            return new SparqlToken(SparqlTokenKind.Punct, start, 1, character.ToString());
        }

        throw Error($"'{character}' is not a SPARQL token.", start);
    }

    private SparqlToken Slice(SparqlTokenKind kind, int start) =>
        new(kind, start, _position - start, _text[start.._position]);

    private SparqlParseException Error(string message, int offset) =>
        new(message, LineOf(offset) + 1, ColumnOf(offset));

    private static bool IsNameStartChar(char character) =>
        char.IsLetter(character) || character == '_';

    private static bool IsNameChar(char character) => TerminalScanner.IsNameChar(character);
}
