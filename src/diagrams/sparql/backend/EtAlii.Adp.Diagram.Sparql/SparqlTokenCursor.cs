namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// The parser's position in a query's tokens, and the token skim the second tier rests on:
/// bracket matching, a keyword at depth zero, the variables in a range and the source text a
/// range was written as. The grammar decides what a token means; the cursor only knows where the
/// tokens are and how brackets nest, so an expression or a path can be passed over and sliced
/// without a tree ever being built for it.
/// </summary>
/// <remarks>
/// '(', '{' and '[' are counted together as one nesting, as they always were: strings and IRIs are
/// already single tokens, so a bracket inside one cannot be mistaken for structure.
/// </remarks>
internal sealed class SparqlTokenCursor
{
    private readonly string _text;
    private readonly SparqlTokenizer _tokenizer;
    private readonly List<SparqlToken> _tokens;

    public SparqlTokenCursor(string text)
    {
        _text = text;
        _tokenizer = new SparqlTokenizer(text);
        _tokens = _tokenizer.Tokenize();
    }

    /// <summary>The index of the token the parser stands on.</summary>
    public int Index { get; set; }

    /// <summary>The token at <paramref name="index"/>.</summary>
    public SparqlToken this[int index] => _tokens[index];

    public SparqlToken Current => _tokens[Index];

    public SparqlToken Next => Index + 1 < _tokens.Count ? _tokens[Index + 1] : _tokens[^1];

    /// <summary>Moves one token on, never past the end-of-file token.</summary>
    public void Advance() => Index = Math.Min(Index + 1, _tokens.Count - 1);

    public SparqlToken Expect(SparqlTokenKind kind, string described)
    {
        var token = Current;
        if (token.Kind != kind)
        {
            throw Error($"Expected {described}, found '{(token.Kind == SparqlTokenKind.EndOfFile ? "the end of the file" : token.Value)}'.", token);
        }

        Advance();
        return token;
    }

    public void ExpectPunct(string punctuation)
    {
        if (!Current.IsPunct(punctuation))
        {
            throw Error($"Expected '{punctuation}', found '{(Current.Kind == SparqlTokenKind.EndOfFile ? "the end of the file" : Current.Value)}'.", Current);
        }

        Advance();
    }

    /// <summary>The index of the token closing the bracket at <paramref name="openIndex"/> - '(' , '{' and '[' counted together, strings and IRIs already single tokens.</summary>
    public int FindBalancedEnd(int openIndex)
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
    public void SkipBalanced(string expectedOpen)
    {
        if (!Current.IsPunct(expectedOpen))
        {
            throw Error($"Expected '{expectedOpen}', found '{Current.Value}'.", Current);
        }

        Index = FindBalancedEnd(Index) + 1;
    }

    /// <summary>The first <paramref name="keyword"/> between the bounds sitting at bracket depth zero relative to <paramref name="start"/>.</summary>
    public int FindTopLevelKeyword(int start, int endExclusive, string keyword)
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

    /// <summary>The bracket depth the tokens from <paramref name="start"/> up to <paramref name="endExclusive"/> leave open.</summary>
    public int DepthBetween(int start, int endExclusive)
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

    /// <summary>The names of the variables between the bounds, each once, in the order first written.</summary>
    public List<string> VariablesBetween(int start, int endInclusive)
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
    public string SliceTokens(int firstIndex, int lastIndex)
    {
        var first = _tokens[firstIndex];
        var last = _tokens[Math.Max(firstIndex, lastIndex)];
        return _text[first.Start..last.End];
    }

    /// <summary>The one-based line <paramref name="token"/> starts on.</summary>
    public int LineOf(SparqlToken token) => LineAt(token.Start);

    /// <summary>The one-based line holding the character at <paramref name="offset"/>.</summary>
    public int LineAt(int offset) => _tokenizer.LineOf(offset) + 1;

    public SparqlParseException Error(string message, SparqlToken at) =>
        new(message, LineOf(at));
}
