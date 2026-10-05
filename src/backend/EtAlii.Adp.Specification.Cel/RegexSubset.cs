namespace EtAlii.Adp.Specification.Cel;

using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// The common subset of RE2, .NET, Java and ECMAScript regular expressions FBL §2.5 allows. A
/// validator rejects exactly what that section excludes: backreferences, lookaround, atomic groups,
/// possessive quantifiers, inline flags and Unicode property classes.
/// </summary>
public static class RegexSubset
{
    /// <summary>Null when <paramref name="expression"/> is in the subset; otherwise a sentence naming the construct.</summary>
    public static string? Check(string expression)
    {
        var inClass = false;
        for (var i = 0; i < expression.Length; i++)
        {
            var c = expression[i];
            if (c == '\\')
            {
                if (i + 1 >= expression.Length) return "The expression ends with a lone backslash.";
                var next = expression[i + 1];
                if (char.IsAsciiDigit(next) && next != '0') return $"'\\{next}' is a backreference, which FBL's regular expressions do not allow.";
                if (next == 'k') return "'\\k' is a named backreference, which FBL's regular expressions do not allow.";
                if (next is 'p' or 'P') return $"'\\{next}' is a Unicode property class, which FBL's regular expressions do not allow.";
                i++;
                continue;
            }
            if (inClass)
            {
                if (c == ']') inClass = false;
                continue;
            }
            switch (c)
            {
                case '[':
                    inClass = true;
                    if (i + 1 < expression.Length && expression[i + 1] == '^') i++;
                    if (i + 1 < expression.Length && expression[i + 1] == ']') i++;
                    break;
                case '(':
                    if (i + 1 < expression.Length && expression[i + 1] == '?')
                    {
                        var rest = expression[(i + 2)..];
                        if (rest.StartsWith(':')) break;
                        if (rest.StartsWith("<=") || rest.StartsWith("<!")) return "Lookbehind is not allowed in FBL's regular expressions.";
                        if (rest.StartsWith('<') || rest.StartsWith("P<"))
                        {
                            if (rest.StartsWith("P<")) return "'(?P<name>' is not in the common subset; write '(?<name>'.";
                            break;
                        }
                        if (rest.StartsWith('=') || rest.StartsWith('!')) return "Lookahead is not allowed in FBL's regular expressions.";
                        if (rest.StartsWith('>')) return "Atomic groups are not allowed in FBL's regular expressions.";
                        return "Inline flags are not allowed in FBL's regular expressions; use the rule's caseInsensitive.";
                    }
                    break;
                case '*':
                case '+':
                case '?':
                case '}':
                    if (i + 1 < expression.Length && expression[i + 1] == '+') return "Possessive quantifiers are not allowed in FBL's regular expressions.";
                    break;
            }
        }
        if (inClass) return "A character class is not closed.";
        try
        {
            _ = new Regex(expression, RegexOptions.CultureInvariant);
        }
        catch (ArgumentException e)
        {
            return $"The expression does not compile: {e.Message}";
        }
        return null;
    }

    /// <summary>
    /// The .NET form of a subset expression: <c>\d</c> and <c>\w</c> mean their ASCII classes, as in
    /// RE2 and ECMAScript, and case-insensitivity covers ASCII letters only.
    /// </summary>
    public static string ToDotNet(string expression, bool caseInsensitive)
    {
        var builder = new StringBuilder(expression.Length + 16);
        var inClass = false;
        for (var i = 0; i < expression.Length; i++)
        {
            var c = expression[i];
            if (c == '\\' && i + 1 < expression.Length)
            {
                var next = expression[i + 1];
                i++;
                switch (next)
                {
                    case 'd': builder.Append(inClass ? "0-9" : "[0-9]"); break;
                    case 'D': builder.Append(inClass ? @"\D" : "[^0-9]"); break;
                    case 'w': builder.Append(inClass ? "A-Za-z0-9_" : "[A-Za-z0-9_]"); break;
                    case 'W': builder.Append(inClass ? @"\W" : "[^A-Za-z0-9_]"); break;
                    default: builder.Append('\\').Append(next); break;
                }
                continue;
            }
            if (inClass)
            {
                if (c == ']') inClass = false;
                builder.Append(AsciiCase(c, caseInsensitive, true));
                continue;
            }
            if (c == '[')
            {
                inClass = true;
                builder.Append(c);
                if (i + 1 < expression.Length && expression[i + 1] == '^') builder.Append(expression[++i]);
                if (i + 1 < expression.Length && expression[i + 1] == ']') builder.Append(expression[++i]);
                continue;
            }
            if (c == '(' && i + 2 < expression.Length && expression[i + 1] == '?' && expression[i + 2] == '<')
            {
                var close = expression.IndexOf('>', i);
                builder.Append(expression, i, close - i + 1);
                i = close;
                continue;
            }
            builder.Append(AsciiCase(c, caseInsensitive, false));
        }
        return builder.ToString();
    }

    private static string AsciiCase(char c, bool caseInsensitive, bool inClass)
    {
        if (!caseInsensitive || !char.IsAsciiLetter(c)) return c.ToString();
        var lower = char.ToLowerInvariant(c);
        var upper = char.ToUpperInvariant(c);
        return inClass ? $"{lower}{upper}" : $"[{lower}{upper}]";
    }
}

/// <summary>A compiled subset expression with a match timeout (FBL §16): a match that takes too long is a finding, not a hang.</summary>
public sealed class BoundedRegex
{
    public BoundedRegex(string expression, bool caseInsensitive, TimeSpan timeout)
    {
        Expression = expression;
        Regex = new Regex(RegexSubset.ToDotNet(expression, caseInsensitive), RegexOptions.CultureInvariant, timeout);
    }

    public string Expression { get; }

    public Regex Regex { get; }

    /// <summary>The match, null when there is none; throws <see cref="RegexMatchTimeoutException"/> when the bound is exceeded.</summary>
    public Match? Match(string input)
    {
        var match = Regex.Match(input);
        return match.Success ? match : null;
    }

    public bool IsMatch(string input) => Regex.IsMatch(input);
}
