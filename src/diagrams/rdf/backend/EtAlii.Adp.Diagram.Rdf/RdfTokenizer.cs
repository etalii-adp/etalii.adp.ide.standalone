using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// Cuts a document's text into Turtle tokens, each addressed by its exact character slice, so the
/// parser above can record spans to the character and the writers below can splice them.
/// </summary>
/// <remarks>
/// Hand-rolled rather than borrowed, deliberately: the splice discipline needs source positions
/// at token grain, and no available RDF library reports them. The tokenizer accepts names a
/// little more loosely than the grammar's character tables - it is not a validator, and a name
/// the grammar would refuse still tokenizes so the parser can say something better than
/// "unexpected character".
/// </remarks>
internal sealed class RdfTokenizer
{
    private readonly string _text;
    private readonly LineIndex _lines;
    private int _position;

    public RdfTokenizer(string text)
    {
        _text = text;
        _lines = new LineIndex(text);
    }

    /// <summary>The zero-based line index holding <paramref name="offset"/>.</summary>
    public int LineOf(int offset) => _lines.LineOf(offset);

    /// <summary>The offset at which the zero-based <paramref name="line"/> starts.</summary>
    public int StartOfLine(int line) => _lines.StartOf(line);

    /// <summary>The next token, consuming it. Whitespace and comments are skipped, never returned.</summary>
    public RdfToken Next()
    {
        SkipTrivia();

        if (_position >= _text.Length)
        {
            return new RdfToken(RdfTokenKind.EndOfFile, _position, 0, "");
        }

        var start = _position;
        var character = _text[_position];

        switch (character)
        {
            case '<':
                return ReadIri(start);
            case '"' or '\'':
                return ReadString(start);
            case '@':
                return ReadAtWord(start);
            case '.':
                // A dot opens a number only when a digit follows; alone it terminates a statement.
                if (_position + 1 < _text.Length && char.IsAsciiDigit(_text[_position + 1]))
                {
                    return ReadNumber(start);
                }

                _position++;
                return new RdfToken(RdfTokenKind.Dot, start, 1, ".");
            case ';':
                _position++;
                return new RdfToken(RdfTokenKind.Semicolon, start, 1, ";");
            case ',':
                _position++;
                return new RdfToken(RdfTokenKind.Comma, start, 1, ",");
            case '(':
                _position++;
                return new RdfToken(RdfTokenKind.OpenParen, start, 1, "(");
            case ')':
                _position++;
                return new RdfToken(RdfTokenKind.CloseParen, start, 1, ")");
            case '[':
                _position++;
                return new RdfToken(RdfTokenKind.OpenBracket, start, 1, "[");
            case ']':
                _position++;
                return new RdfToken(RdfTokenKind.CloseBracket, start, 1, "]");
            case '^':
                if (_position + 1 < _text.Length && _text[_position + 1] == '^')
                {
                    _position += 2;
                    return new RdfToken(RdfTokenKind.DoubleCaret, start, 2, "^^");
                }

                throw Error("A lone '^' is not a Turtle token; the datatype marker is '^^'.", start);
            case '_':
                if (_position + 1 < _text.Length && _text[_position + 1] == ':')
                {
                    return ReadBlankNodeLabel(start);
                }

                throw Error("A blank node label starts with '_:'.", start);
        }

        if (character is '+' or '-' || char.IsAsciiDigit(character))
        {
            return ReadNumber(start);
        }

        if (TerminalScanner.IsNameChar(character) || character == ':')
        {
            return ReadName(start);
        }

        throw Error($"Unexpected character '{character}'.", start);
    }

    private void SkipTrivia() => _position = TerminalScanner.SkipTrivia(_text, _position);

    private RdfToken ReadIri(int start)
    {
        var scan = TerminalScanner.Iri(_text, start);
        return scan.Problem switch
        {
            TerminalProblem.None => Token(RdfTokenKind.Iri, start, scan.End),
            TerminalProblem.LineEnded => throw Error("An IRI ran to the end of its line without its closing '>'.", start),
            _ => throw Error("An IRI ran to the end of the file without its closing '>'.", start),
        };
    }

    private RdfToken ReadString(int start)
    {
        var scan = TerminalScanner.String(_text, start);
        var isLong = start + 2 < _text.Length && _text[start + 1] == _text[start] && _text[start + 2] == _text[start];
        return scan.Problem switch
        {
            TerminalProblem.None => Token(RdfTokenKind.String, start, scan.End),
            TerminalProblem.LineEnded => throw Error("A string ran to the end of its line without its closing quote.", start),
            _ when isLong => throw Error("A long string ran to the end of the file without its closing quotes.", start),
            _ => throw Error("A string ran to the end of the file without its closing quote.", start),
        };
    }

    private RdfToken ReadAtWord(int start)
    {
        _position++; // @
        var wordStart = _position;
        while (_position < _text.Length && (char.IsAsciiLetter(_text[_position]) || _text[_position] == '-'))
        {
            _position++;
        }

        var word = _text[wordStart.._position];
        return word switch
        {
            "prefix" => new RdfToken(RdfTokenKind.PrefixDirective, start, _position - start, "@prefix"),
            "base" => new RdfToken(RdfTokenKind.BaseDirective, start, _position - start, "@base"),
            "" => throw Error("A lone '@' is not a Turtle token.", start),
            _ => new RdfToken(RdfTokenKind.LanguageTag, start, _position - start, word)
        };
    }

    private RdfToken ReadBlankNodeLabel(int start) =>
        Token(RdfTokenKind.BlankNodeLabel, start, TerminalScanner.LocalName(_text, start + 2)); // _:

    private RdfToken ReadNumber(int start) => Token(RdfTokenKind.Number, start, NumericLiteral.Scan(_text, start));

    private RdfToken ReadName(int start)
    {
        // The prefix part, up to a colon - or a bare word, which is a keyword or an error.
        while (_position < _text.Length && TerminalScanner.IsNameChar(_text[_position]))
        {
            _position++;
        }

        if (_position >= _text.Length || _text[_position] != ':')
        {
            var word = _text[start.._position];
            return word switch
            {
                "a" => new RdfToken(RdfTokenKind.A, start, 1, "a"),
                "true" or "false" => new RdfToken(RdfTokenKind.Boolean, start, word.Length, word),
                _ when word.Equals("PREFIX", StringComparison.OrdinalIgnoreCase) =>
                    new RdfToken(RdfTokenKind.SparqlPrefix, start, word.Length, word),
                _ when word.Equals("BASE", StringComparison.OrdinalIgnoreCase) =>
                    new RdfToken(RdfTokenKind.SparqlBase, start, word.Length, word),
                _ => throw Error($"'{word}' is not a Turtle keyword, and a prefixed name needs its ':'.", start)
            };
        }

        return Token(RdfTokenKind.PrefixedName, start, TerminalScanner.LocalName(_text, _position + 1)); // Past the colon.
    }

    /// <summary>The token from <paramref name="start"/> to <paramref name="end"/>, consuming it.</summary>
    private RdfToken Token(RdfTokenKind kind, int start, int end)
    {
        _position = end;
        return new RdfToken(kind, start, end - start, _text[start..end]);
    }

    private RdfParseException Error(string message, int offset) =>
        new(message, LineOf(offset) + 1);
}
