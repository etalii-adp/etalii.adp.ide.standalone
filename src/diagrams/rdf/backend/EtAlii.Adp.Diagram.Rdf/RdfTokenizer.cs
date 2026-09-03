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
    private readonly List<int> _lineStarts = [0];
    private int _position;

    public RdfTokenizer(string text)
    {
        _text = text;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                _lineStarts.Add(i + 1);
            }
        }
    }

    /// <summary>The zero-based line index holding <paramref name="offset"/>.</summary>
    public int LineOf(int offset)
    {
        var low = 0;
        var high = _lineStarts.Count - 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (_lineStarts[middle] <= offset)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    /// <summary>The offset at which the zero-based <paramref name="line"/> starts.</summary>
    public int StartOfLine(int line) => _lineStarts[line];

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
                return ReadString(start, character);
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

        if (IsNameChar(character) || character == ':')
        {
            return ReadName(start);
        }

        throw Error($"Unexpected character '{character}'.", start);
    }

    private void SkipTrivia()
    {
        while (_position < _text.Length)
        {
            var character = _text[_position];
            if (char.IsWhiteSpace(character))
            {
                _position++;
                continue;
            }

            if (character == '#')
            {
                while (_position < _text.Length && _text[_position] != '\n')
                {
                    _position++;
                }

                continue;
            }

            break;
        }
    }

    private RdfToken ReadIri(int start)
    {
        _position++; // <
        while (_position < _text.Length)
        {
            var character = _text[_position];
            if (character == '>')
            {
                _position++;
                return new RdfToken(RdfTokenKind.Iri, start, _position - start, _text[start.._position]);
            }

            if (character == '\\')
            {
                _position++; // The escaped character is consumed below, whatever it is.
            }

            if (character is '\n' or '\r')
            {
                throw Error("An IRI ran to the end of its line without its closing '>'.", start);
            }

            _position++;
        }

        throw Error("An IRI ran to the end of the file without its closing '>'.", start);
    }

    private RdfToken ReadString(int start, char quote)
    {
        var isLong = _position + 2 < _text.Length && _text[_position + 1] == quote && _text[_position + 2] == quote;
        if (isLong)
        {
            _position += 3;
            while (_position < _text.Length)
            {
                if (_text[_position] == '\\')
                {
                    _position += 2;
                    continue;
                }

                if (_text[_position] == quote
                    && _position + 2 < _text.Length
                    && _text[_position + 1] == quote
                    && _text[_position + 2] == quote)
                {
                    _position += 3;
                    return new RdfToken(RdfTokenKind.String, start, _position - start, _text[start.._position]);
                }

                _position++;
            }

            throw Error("A long string ran to the end of the file without its closing quotes.", start);
        }

        _position++; // The opening quote.
        while (_position < _text.Length)
        {
            var character = _text[_position];
            if (character == '\\')
            {
                _position += 2;
                continue;
            }

            if (character == quote)
            {
                _position++;
                return new RdfToken(RdfTokenKind.String, start, _position - start, _text[start.._position]);
            }

            if (character is '\n' or '\r')
            {
                throw Error("A string ran to the end of its line without its closing quote.", start);
            }

            _position++;
        }

        throw Error("A string ran to the end of the file without its closing quote.", start);
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

    private RdfToken ReadBlankNodeLabel(int start)
    {
        _position += 2; // _:
        ScanLocalName();
        return new RdfToken(RdfTokenKind.BlankNodeLabel, start, _position - start, _text[start.._position]);
    }

    private RdfToken ReadNumber(int start)
    {
        if (_text[_position] is '+' or '-')
        {
            _position++;
        }

        while (_position < _text.Length && char.IsAsciiDigit(_text[_position]))
        {
            _position++;
        }

        // A trailing dot is the statement terminator, not part of the number: '5.' is the
        // integer five and then the end of a statement, while '5.0' is a decimal.
        if (_position + 1 < _text.Length && _text[_position] == '.' && char.IsAsciiDigit(_text[_position + 1]))
        {
            _position++;
            while (_position < _text.Length && char.IsAsciiDigit(_text[_position]))
            {
                _position++;
            }
        }

        if (_position < _text.Length && _text[_position] is 'e' or 'E')
        {
            _position++;
            if (_position < _text.Length && _text[_position] is '+' or '-')
            {
                _position++;
            }

            while (_position < _text.Length && char.IsAsciiDigit(_text[_position]))
            {
                _position++;
            }
        }

        return new RdfToken(RdfTokenKind.Number, start, _position - start, _text[start.._position]);
    }

    private RdfToken ReadName(int start)
    {
        // The prefix part, up to a colon - or a bare word, which is a keyword or an error.
        while (_position < _text.Length && IsNameChar(_text[_position]))
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

        _position++; // The colon.
        ScanLocalName();
        return new RdfToken(RdfTokenKind.PrefixedName, start, _position - start, _text[start.._position]);
    }

    private void ScanLocalName()
    {
        while (_position < _text.Length)
        {
            var character = _text[_position];
            if (character == '\\' && _position + 1 < _text.Length)
            {
                _position += 2;
                continue;
            }

            if (character == '%' && _position + 2 < _text.Length)
            {
                _position += 3;
                continue;
            }

            if (character == '.')
            {
                // A dot continues a local name only when a name character follows; otherwise it
                // is the statement terminator sitting flush against the name.
                var next = _position + 1 < _text.Length ? _text[_position + 1] : ' ';
                if (IsNameChar(next) || next is ':' or '%' or '\\')
                {
                    _position++;
                    continue;
                }

                return;
            }

            if (IsNameChar(character) || character == ':')
            {
                _position++;
                continue;
            }

            return;
        }
    }

    private static bool IsNameChar(char character) =>
        char.IsLetterOrDigit(character) || character is '_' or '-';

    private RdfParseException Error(string message, int offset) =>
        new(message, LineOf(offset) + 1);
}
