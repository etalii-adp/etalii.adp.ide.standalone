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
