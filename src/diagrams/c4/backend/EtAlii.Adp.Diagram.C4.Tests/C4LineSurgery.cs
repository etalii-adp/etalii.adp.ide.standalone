using System.Text;
using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// The line surgery the C4 commands wrote with before c4-disl-fbl task 15, when every edit became a model change the
/// library plans into splices: the DSL tokenizer, the argument replacement and the line replacement, kept verbatim
/// for the edits' oracle (<see cref="Parity.HandWrittenC4Edits"/>) and for the tests that edit a document by hand.
/// </summary>
internal static class C4LineSurgery
{
    /// <summary>
    /// The tokens of <paramref name="line"/> with their positions, so an edit can replace one
    /// argument and leave every other character of the line exactly as it was - which is the
    /// whole of Requirement 3.2.
    /// </summary>
    public static IReadOnlyList<LineToken> SplitWithSpans(string line) => C4Tokens.Lexer.Tokens(line);

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

    /// <summary>
    /// Replaces line <paramref name="number"/> (1-based) with <paramref name="text"/>. Every
    /// other line keeps the bytes it arrived as, which is the whole of Requirement 3.2.
    /// </summary>
    public static void ReplaceLine(this LineDocument document, uint number, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var index = (int)number - 1;
        document.Replace(new LineRange(index, index), [text]);
    }
}
