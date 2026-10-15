using System.Text;
using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// Splits a line of DSL into its words. The DSL's arguments are whitespace-separated except
/// that a quoted string is one argument however much whitespace it contains, which is what
/// makes <c>softwareSystem "Internet Banking System" "Lets customers view accounts"</c> three
/// tokens rather than nine.
/// </summary>
public static class C4Tokens
{
    /// <summary>
    /// The DSL's line lexer: <c>//</c> and <c>#</c> comments outside quoted strings, <c>\"</c> for a
    /// quote inside one, and braces as tokens of their own.
    /// </summary>
    internal static readonly LineLexer Lexer = new(new CommentPolicy(["#", "//"], OutsideQuotesOnly: true), EscapePolicy.BackslashQuote, "{}");

    /// <summary>
    /// The tokens of <paramref name="line"/> with their positions, so an edit can replace one
    /// argument and leave every other character of the line exactly as it was - which is the
    /// whole of Requirement 3.2.
    /// </summary>
    public static IReadOnlyList<LineToken> SplitWithSpans(string line) => Lexer.Tokens(line);

    /// <summary>
    /// <paramref name="line"/> with the argument at <paramref name="argumentIndex"/> (counted
    /// after the keyword) set to <paramref name="value"/>. Arguments the line does not have yet
    /// are appended, with empty ones in between - so setting a technology on a container that
    /// has only a name gives it an empty description rather than shifting the technology into
    /// the description's place.
    /// </summary>
    /// <param name="line">The line to modify.</param>
    /// <param name="keywordIndex">Index of the token the arguments follow.</param>
    /// <param name="argumentIndex">Index of the argument to replace.</param>
    /// <param name="value">The value to set the argument to.</param>
    public static string ReplaceArgument(string line, int keywordIndex, int argumentIndex, string value)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(value);

        var tokens = SplitWithSpans(line);
        var target = keywordIndex + 1 + argumentIndex;
        var quoted = QuotedString.Write(value);

        // The arguments stop at an opening brace: `container "A" "b" {` has two, not three.
        var end = tokens.Count;
        for (var index = keywordIndex + 1; index < tokens.Count; index++)
        {
            if (!tokens[index].Quoted && tokens[index].Value is "{" or "}")
            {
                end = index;
                break;
            }
        }

        if (target < end)
        {
            var token = tokens[target];
            return line[..token.Start] + quoted + line[(token.Start + token.Length)..];
        }

        // Append, padding any gap with empty strings so positions keep their meaning.
        var insertAt = end < tokens.Count ? tokens[end].Start : line.Length;
        var padding = string.Concat(Enumerable.Repeat(" \"\"", Math.Max(0, target - end)));
        var tail = line[insertAt..];
        var head = line[..insertAt].TrimEnd();
        return head + padding + " " + quoted + (tail.Length > 0 ? " " + tail.TrimStart() : "");
    }

    /// <summary>
    /// <paramref name="code"/> as tokens, with quotes removed from quoted ones and <c>\"</c>
    /// unescaped inside them. An unterminated quote yields what there is rather than throwing:
    /// the parser reports a malformed line better than a tokenizer can.
    /// </summary>
    public static IReadOnlyList<string> Split(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var started = false;

        for (var index = 0; index < code.Length; index++)
        {
            var character = code[index];

            if (inQuotes)
            {
                if (character == '\\' && index + 1 < code.Length && code[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                    continue;
                }

                if (character == '"')
                {
                    inQuotes = false;
                    continue;
                }

                current.Append(character);
                continue;
            }

            if (character == '"')
            {
                inQuotes = true;
                started = true;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (started)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    started = false;
                }

                continue;
            }

            // Braces are their own tokens so "model {" and "model{" read the same.
            if (character is '{' or '}')
            {
                if (started)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    started = false;
                }

                tokens.Add(character.ToString());
                continue;
            }

            current.Append(character);
            started = true;
        }

        if (started)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }
}
