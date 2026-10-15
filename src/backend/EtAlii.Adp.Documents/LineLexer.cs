using System.Text;

namespace EtAlii.Adp.Documents;

/// <summary>
/// The words of one line of a line-based language: whitespace-separated, a double-quoted string
/// one word however much whitespace it holds, and a line comment cut off as the language's
/// <see cref="CommentPolicy"/> says. Every token keeps its position, so an edit can replace one
/// word and leave every other character of the line as it was.
/// </summary>
/// <param name="comments">Where a comment starts.</param>
/// <param name="escapes">How a quote is written inside a quoted string.</param>
/// <param name="punctuation">Characters that are a token of their own wherever they stand, such as braces.</param>
public sealed class LineLexer(CommentPolicy comments, EscapePolicy escapes, string punctuation = "")
{
    /// <summary><paramref name="text"/> up to the first comment, as the comment policy places it.</summary>
    public string StripComment(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var inQuotes = false;
        for (var index = 0; index < text.Length; index++)
        {
            if (comments.OutsideQuotesOnly && text[index] == '"' && !IsEscapedQuote(text, index))
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (inQuotes)
            {
                continue;
            }

            foreach (var marker in comments.Markers)
            {
                if (!text.AsSpan(index).StartsWith(marker, StringComparison.Ordinal))
                {
                    continue;
                }

                if (comments.NotAfter is { } notAfter && index > 0 && text[index - 1] == notAfter)
                {
                    index += marker.Length - 1;
                    break;
                }

                return text[..index];
            }
        }

        return text;
    }

    /// <summary>The tokens of <paramref name="line"/> before its comment, with their positions.</summary>
    public IReadOnlyList<LineToken> Tokens(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var code = StripComment(line);
        var tokens = new List<LineToken>();
        var index = 0;
        while (index < code.Length)
        {
            if (char.IsWhiteSpace(code[index]))
            {
                index++;
                continue;
            }

            var start = index;
            if (code[index] == '"')
            {
                index++;
                var builder = new StringBuilder();
                while (index < code.Length)
                {
                    if (escapes == EscapePolicy.BackslashQuote && code[index] == '\\' && index + 1 < code.Length && code[index + 1] == '"')
                    {
                        builder.Append('"');
                        index += 2;
                        continue;
                    }

                    if (code[index] == '"')
                    {
                        index++;
                        break;
                    }

                    builder.Append(code[index]);
                    index++;
                }

                tokens.Add(new LineToken(builder.ToString(), start, index - start, Quoted: true));
                continue;
            }

            if (punctuation.Contains(code[index], StringComparison.Ordinal))
            {
                tokens.Add(new LineToken(code[index].ToString(), start, 1, Quoted: false));
                index++;
                continue;
            }

            while (index < code.Length && !char.IsWhiteSpace(code[index]) && code[index] != '"' && !punctuation.Contains(code[index], StringComparison.Ordinal))
            {
                index++;
            }

            tokens.Add(new LineToken(code[start..index], start, index - start, Quoted: false));
        }

        return tokens;
    }

    private bool IsEscapedQuote(string text, int index) =>
        escapes == EscapePolicy.BackslashQuote && index > 0 && text[index - 1] == '\\';
}
