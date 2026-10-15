namespace EtAlii.Adp.Documents;

/// <summary>
/// The terminals Turtle and SPARQL share, read from a text by offset: the whitespace and comments
/// between tokens, strings, IRIs and the local part of a name. Each reader says where its terminal
/// ends; the tokenizer that called it makes the token, and its own error when one did not close.
/// </summary>
/// <remarks>
/// In core rather than in the rdf module because the sparql module reads the same terminals and
/// may not reference rdf (code-structure FR-044). The two parsers above the tokenizers stay
/// separate. The numbers are <see cref="NumericLiteral"/>, and what a string or an IRI means once
/// read is <see cref="RdfEscapes"/>.
/// </remarks>
public static class TerminalScanner
{
    /// <summary>The offset of the next token: past whitespace, and past every <c>#</c> comment to its line's end.</summary>
    public static int SkipTrivia(string text, int position)
    {
        while (position < text.Length)
        {
            var character = text[position];
            if (char.IsWhiteSpace(character))
            {
                position++;
                continue;
            }

            if (character == '#')
            {
                while (position < text.Length && text[position] != '\n')
                {
                    position++;
                }

                continue;
            }

            break;
        }

        return position;
    }

    /// <summary>
    /// A string whose opening quote is at <paramref name="start"/>: short, closed by the same quote
    /// on its own line, or long, opened and closed by three. An escaped character never closes it.
    /// </summary>
    /// <remarks>
    /// A short string ends at a carriage return as it does at a line feed: both grammars leave
    /// either out of a short string literal (behaviour changes B-02).
    /// </remarks>
    public static TerminalScan String(string text, int start)
    {
        var quote = text[start];
        var position = start;
        var isLong = position + 2 < text.Length && text[position + 1] == quote && text[position + 2] == quote;
        if (isLong)
        {
            position += 3;
            while (position < text.Length)
            {
                if (text[position] == '\\')
                {
                    position += 2;
                    continue;
                }

                if (text[position] == quote
                    && position + 2 < text.Length
                    && text[position + 1] == quote
                    && text[position + 2] == quote)
                {
                    return new TerminalScan(position + 3, TerminalProblem.None);
                }

                position++;
            }

            return new TerminalScan(position, TerminalProblem.FileEnded);
        }

        position++; // The opening quote.
        while (position < text.Length)
        {
            var character = text[position];
            if (character == '\\')
            {
                position += 2;
                continue;
            }

            if (character == quote)
            {
                return new TerminalScan(position + 1, TerminalProblem.None);
            }

            if (character is '\n' or '\r')
            {
                return new TerminalScan(position, TerminalProblem.LineEnded);
            }

            position++;
        }

        return new TerminalScan(position, TerminalProblem.FileEnded);
    }

    /// <summary>
    /// An IRI whose <c>&lt;</c> is at <paramref name="start"/>, closed by <c>&gt;</c> on its own line.
    /// A backslash escapes the character after it, whatever it is.
    /// </summary>
    public static TerminalScan Iri(string text, int start)
    {
        var position = start + 1; // <
        while (position < text.Length)
        {
            var character = text[position];
            if (character == '>')
            {
                return new TerminalScan(position + 1, TerminalProblem.None);
            }

            if (character == '\\')
            {
                position++; // The escaped character is consumed below, whatever it is.
            }

            if (character is '\n' or '\r')
            {
                return new TerminalScan(position, TerminalProblem.LineEnded);
            }

            position++;
        }

        return new TerminalScan(position, TerminalProblem.FileEnded);
    }

    /// <summary>
    /// The offset past the local part of a name that starts at <paramref name="position"/>: name
    /// characters, colons, <c>\</c> escapes and <c>%</c> hex pairs, and a dot only when the name
    /// goes on after it.
    /// </summary>
    public static int LocalName(string text, int position)
    {
        while (position < text.Length)
        {
            var character = text[position];
            if (character == '\\' && position + 1 < text.Length)
            {
                position += 2;
                continue;
            }

            if (character == '%' && position + 2 < text.Length)
            {
                position += 3;
                continue;
            }

            if (character == '.')
            {
                // A dot continues a local name only when a name character follows; otherwise it
                // is the statement terminator sitting flush against the name.
                var next = position + 1 < text.Length ? text[position + 1] : ' ';
                if (IsNameChar(next) || next is ':' or '%' or '\\')
                {
                    position++;
                    continue;
                }

                return position;
            }

            if (IsNameChar(character) || character == ':')
            {
                position++;
                continue;
            }

            return position;
        }

        return position;
    }

    /// <summary>Whether <paramref name="character"/> may stand inside a name: a letter, a digit, <c>_</c> or <c>-</c>.</summary>
    public static bool IsNameChar(char character) =>
        char.IsLetterOrDigit(character) || character is '_' or '-';
}
