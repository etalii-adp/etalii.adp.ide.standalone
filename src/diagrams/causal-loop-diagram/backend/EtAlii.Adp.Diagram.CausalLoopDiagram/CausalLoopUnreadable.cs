using System.Globalization;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// The tool's sentence for a statement the binding does not read (causal-loop-disl-fbl Requirement 4.4,
/// finding B2): one of the four the module has always given, chosen by what the statement starts like.
/// </summary>
/// <remarks>
/// <para>
/// <b>It stands in for a language gap</b> (the definition's companion, gap 1). The binding's last rule
/// takes every statement no other rule reads, and the definition reports it once on its line under
/// <c>causal-loop.unreadable-line</c>; FBL can say what a rule matches, not why a line that starts
/// like a statement did not match it. Whether a line is read is the binding's alone: this class is
/// asked only about a line the binding already left unread, and only words it.
/// </para>
/// <para>
/// It goes when FBL can give a rule a reason for a line that almost matched.
/// </para>
/// </remarks>
public static class CausalLoopUnreadable
{
    /// <summary>The sentence for <paramref name="statement"/>, a line the binding does not read.</summary>
    public static string SentenceFor(string statement)
    {
        ArgumentNullException.ThrowIfNull(statement);
        var words = Words(statement);
        switch (words.FirstOrDefault() ?? statement.Trim())
        {
            case "variable" when words is [_, { Length: 0 }, ..]:
                return "A variable needs an id of at least one character.";

            case "link" when words.Count >= 4 && words[2] == "->":
                foreach (var word in words.Skip(4))
                {
                    if (word.StartsWith("weight=", StringComparison.Ordinal)
                        && !double.TryParse(word["weight=".Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    {
                        return $"'{word}' does not read as a weight.";
                    }
                }

                return LinkShape;

            case "link":
                return LinkShape;

            case var first:
                return $"'{first}' is not a statement this reading knows.";
        }
    }

    private const string LinkShape = "A link reads 'link <from> -> <to>', optionally followed by a polarity, 'delayed', 'flipped', a weight and a label.";

    /// <summary>
    /// The statement's words: split at whitespace, a double-quoted run kept together as one word
    /// without its quotes, and an unclosed quote running to the end of the line.
    /// </summary>
    internal static List<string> Words(string text)
    {
        var words = new List<string>();
        var index = 0;
        while (index < text.Length)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                index++;
                continue;
            }

            if (text[index] == '"')
            {
                var close = text.IndexOf('"', index + 1);
                if (close < 0)
                {
                    words.Add(text[(index + 1)..]);
                    break;
                }

                words.Add(text[(index + 1)..close]);
                index = close + 1;
                continue;
            }

            var end = index;
            while (end < text.Length && !char.IsWhiteSpace(text[end]))
            {
                end++;
            }

            words.Add(text[index..end]);
            index = end;
        }

        return words;
    }
}
