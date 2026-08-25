using System.Text;

namespace EtAlii.Adp.C4;

/// <summary>
/// Splits a line of DSL into its words. The DSL's arguments are whitespace-separated except
/// that a quoted string is one argument however much whitespace it contains, which is what
/// makes <c>softwareSystem "Internet Banking System" "Lets customers view accounts"</c> three
/// tokens rather than nine.
/// </summary>
public static class C4Tokens
{
    /// <summary>One token and where it sits in the line it came from, quotes included.</summary>
    /// <param name="Value">The token's text, unquoted and unescaped.</param>
    /// <param name="Start">Index of its first character in the line - the opening quote, when it has one.</param>
    /// <param name="Length">How many characters it occupies, the quotes included.</param>
    /// <param name="Quoted">Whether it was written in quotes.</param>
    public readonly record struct C4Token(string Value, int Start, int Length, bool Quoted);

    /// <summary>
    /// The tokens of <paramref name="line"/> with their positions, so an edit can replace one
    /// argument and leave every other character of the line exactly as it was - which is the
    /// whole of Requirement 3.2.
    /// </summary>
    public static IReadOnlyList<C4Token> SplitWithSpans(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var code = C4Line.StripComment(line);
        var tokens = new List<C4Token>();
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
                    if (code[index] == '\\' && index + 1 < code.Length && code[index + 1] == '"')
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

                tokens.Add(new C4Token(builder.ToString(), start, index - start, Quoted: true));
                continue;
            }

            if (code[index] is '{' or '}')
            {
                tokens.Add(new C4Token(code[index].ToString(), start, 1, Quoted: false));
                index++;
                continue;
            }

            while (index < code.Length && !char.IsWhiteSpace(code[index]) && code[index] is not ('{' or '}' or '"'))
            {
                index++;
            }

            tokens.Add(new C4Token(code[start..index], start, index - start, Quoted: false));
        }

        return tokens;
    }

    /// <summary>
    /// <paramref name="line"/> with the argument at <paramref name="argumentIndex"/> (counted
    /// after the keyword) set to <paramref name="value"/>. Arguments the line does not have yet
    /// are appended, with empty ones in between - so setting a technology on a container that
    /// has only a name gives it an empty description rather than shifting the technology into
    /// the description's place.
    /// </summary>
    /// <param name="keywordIndex">Index of the token the arguments follow.</param>
    public static string ReplaceArgument(string line, int keywordIndex, int argumentIndex, string value)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(value);

        var tokens = SplitWithSpans(line);
        var target = keywordIndex + 1 + argumentIndex;
        var quoted = "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

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
