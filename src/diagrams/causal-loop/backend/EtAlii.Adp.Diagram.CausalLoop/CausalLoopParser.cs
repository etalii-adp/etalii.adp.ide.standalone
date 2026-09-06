using System.Globalization;
using EtAlii.Adp.Backend.Hierarchy;

namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>
/// Reads a <c>.cld</c> document into the model it states.
/// </summary>
/// <remarks>
/// <para>
/// The grammar, one statement per line:
/// </para>
/// <code>
/// causal-loop 1
/// variable population "Population"
/// link population -&gt; births +
/// link births -&gt; population + delayed weight=2 "seasonal"
/// loop R1 "Births beget births" population births
/// </code>
/// <para>
/// Blank lines and <c>#</c> comments are ignored. A line the grammar does not recognise is
/// recorded as a problem rather than silently dropped - a diagram that quietly ignores a line
/// its author wrote is worse than one that says it could not read it.
/// </para>
/// <para>
/// <b>Polarity is read, never inferred.</b> <c>+</c> and <c>s</c> mean the same thing, as do
/// <c>-</c> and <c>o</c>, because both traditions are in live use (Requirement 2.2); and a link
/// with no polarity written is <see cref="CausalLoopPolarity.Unstated"/> rather than positive,
/// because the module's central check counts negatives and "unknown" is not "none"
/// (Requirement 3.6).
/// </para>
/// </remarks>
public static class CausalLoopParser
{
    /// <summary>The header a <c>.cld</c> document opens with.</summary>
    public const string Header = "causal-loop";

    /// <summary>Reads the document, collecting what it states and what it could not read.</summary>
    public static CausalLoopParseResult Parse(CausalLoopDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var variables = new List<CausalLoopVariable>();
        var links = new List<CausalLoopLink>();
        var loops = new List<CausalLoopLoop>();
        var problems = new List<CausalLoopParseProblem>();

        for (var index = 0; index < document.Lines.Count; index++)
        {
            var line = document.Lines[index];
            if (line.IsBlank || line.IsComment)
            {
                continue;
            }

            var range = new LineRange(index, index);
            var tokens = Tokenize(line.Text);
            if (tokens.Count == 0)
            {
                continue;
            }

            switch (tokens[0])
            {
                case Header:
                    break;

                case "variable" when tokens.Count >= 2:
                    variables.Add(new CausalLoopVariable(tokens[1], tokens.Count >= 3 ? tokens[2] : "", range));
                    break;

                case "link":
                    if (TryReadLink(tokens, range, out var link, out var linkProblem))
                    {
                        links.Add(link!);
                    }
                    else
                    {
                        problems.Add(new CausalLoopParseProblem(range, linkProblem!));
                    }

                    break;

                case "loop" when tokens.Count >= 4:
                    loops.Add(new CausalLoopLoop(tokens[1], tokens[2], tokens[3..], range));
                    break;

                default:
                    problems.Add(new CausalLoopParseProblem(range, $"'{tokens[0]}' is not a statement this reading knows."));
                    break;
            }
        }

        return new CausalLoopParseResult(new CausalLoopModel(variables, links, loops), problems);
    }

    /// <summary>Reads what a polarity token says, or <see cref="CausalLoopPolarity.Unstated"/> where it says nothing.</summary>
    public static CausalLoopPolarity PolarityOf(string token) => token switch
    {
        "+" or "s" or "S" => CausalLoopPolarity.Positive,
        "-" or "o" or "O" => CausalLoopPolarity.Negative,
        _ => CausalLoopPolarity.Unstated,
    };

    /// <summary>
    /// <c>link &lt;from&gt; -&gt; &lt;to&gt; [polarity] [delayed] [flipped] [weight=n] ["label"]</c>.
    /// </summary>
    private static bool TryReadLink(IReadOnlyList<string> tokens, LineRange range, out CausalLoopLink? link, out string? problem)
    {
        link = null;
        problem = null;

        if (tokens.Count < 4 || tokens[2] != "->")
        {
            problem = "A link reads 'link <from> -> <to>', optionally followed by a polarity, 'delayed', 'flipped', a weight and a label.";
            return false;
        }

        var polarity = CausalLoopPolarity.Unstated;
        var delayed = false;
        var flipped = false;
        double? weight = null;
        var label = "";

        foreach (var token in tokens.Skip(4))
        {
            if (token is "delayed")
            {
                delayed = true;
            }
            else if (token is "flipped")
            {
                flipped = true;
            }
            else if (token.StartsWith("weight=", StringComparison.Ordinal))
            {
                if (!double.TryParse(token["weight=".Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    problem = $"'{token}' does not read as a weight.";
                    return false;
                }

                weight = parsed;
            }
            else if (PolarityOf(token) != CausalLoopPolarity.Unstated)
            {
                polarity = PolarityOf(token);
            }
            else
            {
                label = token;
            }
        }

        link = new CausalLoopLink(tokens[1], tokens[3], polarity, delayed, flipped, weight, label, range);
        return true;
    }

    /// <summary>
    /// Splits a line into tokens, keeping a double-quoted run together as one and dropping its
    /// quotes - which is how a label with a space in it survives.
    /// </summary>
    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
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
                    tokens.Add(text[(index + 1)..]);
                    break;
                }

                tokens.Add(text[(index + 1)..close]);
                index = close + 1;
                continue;
            }

            var end = index;
            while (end < text.Length && !char.IsWhiteSpace(text[end]))
            {
                end++;
            }

            tokens.Add(text[index..end]);
            index = end;
        }

        return tokens;
    }
}
