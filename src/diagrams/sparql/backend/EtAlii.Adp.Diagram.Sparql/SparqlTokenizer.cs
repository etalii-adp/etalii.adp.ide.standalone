namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// Cuts a query's text into tokens, each addressed by its exact character slice. Positions serve
/// two purposes only - reporting error lines, and slicing expression text as written - because
/// nothing in this module ever writes the query back.
/// </summary>
/// <remarks>
/// Hand-rolled, deliberately: the second parser tier needs exact source slices for expressions
/// and property paths, and a borrowed parser would hand back trees instead. Like its Turtle
/// sibling, it accepts names a little more loosely than the grammar's character tables - it is
/// not a validator, and a name the grammar would refuse still tokenizes so the parser can say
/// something better than "unexpected character".
/// </remarks>
internal sealed class SparqlTokenizer
{
    private readonly string _text;
    private readonly List<int> _lineStarts = [0];
    private int _position;

    public SparqlTokenizer(string text)
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
                if (TryReadIri(start, out var iri))
                {
                    return iri;
                }

                return ReadOperator(start);
            case '"' or '\'':
                return ReadString(start, character);
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

        if (IsNameStartChar(character))
        {
            return ReadNameOrPrefixedName(start);
        }

        return ReadOperator(start);
    }

    private void SkipTrivia()
    {
        while (_position < _text.Length)
        {
            var character = _text[_position];
            if (char.IsWhiteSpace(character))
            {
                _position++;
            }
            else if (character == '#')
            {
                while (_position < _text.Length && _text[_position] != '\n')
                {
                    _position++;
                }
            }
            else
            {
                break;
            }
        }
    }

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

    private SparqlToken ReadString(int start, char quote)
    {
        var longQuote = _position + 2 < _text.Length && _text[_position + 1] == quote && _text[_position + 2] == quote;
        _position += longQuote ? 3 : 1;

        while (_position < _text.Length)
        {
            var character = _text[_position];
            if (character == '\\' && _position + 1 < _text.Length)
            {
                _position += 2;
                continue;
            }

            if (character == quote)
            {
                if (!longQuote)
                {
                    _position++;
                    return Slice(SparqlTokenKind.String, start);
                }

                if (_position + 2 < _text.Length && _text[_position + 1] == quote && _text[_position + 2] == quote)
                {
                    _position += 3;
                    return Slice(SparqlTokenKind.String, start);
                }
            }

            if (!longQuote && character == '\n')
            {
                throw Error("The string that starts here is not closed on its line.", start);
            }

            _position++;
        }

        throw Error("The string that starts here is never closed.", start);
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

        if (_position == start + 1)
        {
            throw Error("A lone '@' is not a SPARQL token; a language tag is '@' followed by letters.", start);
        }

        return Slice(SparqlTokenKind.LanguageTag, start);
    }

    private SparqlToken ReadBlankNodeLabel(int start)
    {
        _position += 2;
        while (_position < _text.Length && IsNameChar(_text[_position]))
        {
            _position++;
        }

        return Slice(SparqlTokenKind.BlankNodeLabel, start);
    }

    private SparqlToken ReadNumber(int start)
    {
        while (_position < _text.Length && char.IsAsciiDigit(_text[_position]))
        {
            _position++;
        }

        if (_position < _text.Length && _text[_position] == '.' && _position + 1 < _text.Length && char.IsAsciiDigit(_text[_position + 1]))
        {
            _position++;
            while (_position < _text.Length && char.IsAsciiDigit(_text[_position]))
            {
                _position++;
            }
        }

        if (_position < _text.Length && (_text[_position] is 'e' or 'E'))
        {
            var exponent = _position + 1;
            if (exponent < _text.Length && (_text[exponent] is '+' or '-'))
            {
                exponent++;
            }

            if (exponent < _text.Length && char.IsAsciiDigit(_text[exponent]))
            {
                _position = exponent;
                while (_position < _text.Length && char.IsAsciiDigit(_text[_position]))
                {
                    _position++;
                }
            }
        }

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
            _position++;
            while (_position < _text.Length && IsNameChar(_text[_position]))
            {
                _position++;
            }

            return Slice(SparqlTokenKind.PrefixedName, start);
        }

        var value = _text[start.._position];
        if (value is "true" or "false")
        {
            return new SparqlToken(SparqlTokenKind.Boolean, start, _position - start, value);
        }

        return new SparqlToken(SparqlTokenKind.Name, start, _position - start, value);
    }

    private SparqlToken ReadOperator(int start)
    {
        var character = _text[_position];

        if (character == ':')
        {
            // The default prefix's bare form, ':thing' - or a lone ':' the parser will refuse
            // in context.
            _position++;
            while (_position < _text.Length && IsNameChar(_text[_position]))
            {
                _position++;
            }

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
        new(message, LineOf(offset) + 1);

    private static bool IsNameStartChar(char character) =>
        char.IsLetter(character) || character == '_';

    private static bool IsNameChar(char character) =>
        char.IsLetterOrDigit(character) || character is '_' or '-';
}
