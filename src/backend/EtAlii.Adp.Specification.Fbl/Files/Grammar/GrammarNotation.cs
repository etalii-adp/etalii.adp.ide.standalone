using System.Text.RegularExpressions;

namespace EtAlii.Adp.Specification.Fbl.Grammar;

/// <summary>
/// The check that a binding's grammar is written in ATD grammar notation (ATD §4.1) and that its terminals keep to
/// the regular expression subset (ATD §3.5), as FBL 0.9 §4.9 requires. It is a port of <c>lint_text</c> and
/// <c>regex_problems</c> of etalii.adp's <c>.github/scripts/atd-grammar.py</c>, which etalii.adp runs on every binding's
/// grammar, so that both refuse the same grammars with the same reasons. It finds constructs; it reads no grammar,
/// which <c>EtAlii.Lark</c> does.
/// </summary>
internal static partial class GrammarNotation
{
    private const string AdmittedEscapes = "dDsSwWtnr\\";

    /// <summary>
    /// Every construct of <paramref name="text"/> outside the notation and every terminal outside the regular expression
    /// subset, as sentences with their line; an <c>%import</c> may name only one of <paramref name="importable"/>.
    /// </summary>
    public static IReadOnlyList<string> Problems(string text, IReadOnlyCollection<string> importable)
    {
        var problems = new List<string>();
        var line = 1;
        var atLineStart = true;
        var position = 0;
        while (position < text.Length)
        {
            var match = Token().Match(text, position);
            if (!match.Success || match.Index != position)
            {
                problems.Add($"line {line}: '{text[position]}' is not ATD grammar notation.");
                position++;
                continue;
            }
            var value = match.Value;
            var where = $"line {line}";
            if (match.Groups["string"].Success && match.Groups["stringflags"].Length > 0)
            {
                problems.Add($"{where}: the string flag '{match.Groups["stringflags"].Value}' of {value} is not admitted (ATD §4.1).");
            }
            if (match.Groups["regexp"].Success)
            {
                var flags = match.Groups["regexpflags"].Value;
                if (flags.Length > 0) problems.Add($"{where}: the flag '{flags}' of {value} is not admitted (ATD §4.1).");
                var body = value[1..(value.Length - flags.Length - 1)];
                problems.AddRange(RegexProblems(body).Select(problem => $"{where}: the terminal {value} uses {problem}, outside the subset of ATD §3.5."));
            }
            if (match.Groups["directive"].Success) CheckDirective(text, match, importable, where, problems);
            if (match.Groups["word"].Success && atLineStart) CheckDefinition(text, match, where, problems);
            if (match.Groups["range"].Success) problems.Add($"{where}: the range '..' is not admitted (ATD §4.1).");
            if (match.Groups["punctuation"].Success && value == "~") problems.Add($"{where}: the repetition '~' is not admitted (ATD §4.1).");
            if (match.Groups["newline"].Success)
            {
                line++;
                atLineStart = true;
            }
            else if (!match.Groups["space"].Success && !match.Groups["comment"].Success)
            {
                atLineStart = false;
            }
            position = match.Index + match.Length;
        }
        return problems;
    }

    /// <summary>The files beside the document that <paramref name="text"/> imports, by name without <c>.lark</c>.</summary>
    public static IReadOnlyList<string> Imports(string text) =>
        [.. ImportTarget().Matches(text).Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal)];

    /// <summary>The rules <paramref name="text"/> defines, and the names it imports, which may be rules too.</summary>
    public static IReadOnlyList<string> RuleNames(string text) =>
        [.. RuleHead().Matches(text).Select(match => match.Groups[1].Value).Concat(ImportedNames().Matches(text).SelectMany(match => match.Groups[1].Value.Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries))).Distinct(StringComparer.Ordinal)];

    private static void CheckDirective(string text, Match match, IReadOnlyCollection<string> importable, string where, List<string> problems)
    {
        var value = match.Value;
        if (value is not ("%import" or "%ignore"))
        {
            problems.Add($"{where}: '{value}' is not admitted; only '%import' and '%ignore' are (ATD §4.1).");
            return;
        }
        if (value != "%import") return;
        var rest = text[(match.Index + match.Length)..].TrimStart(' ', '\t');
        var target = ImportHead().Match(rest);
        if (!target.Success || !importable.Contains(target.Groups[1].Value))
        {
            problems.Add($"{where}: an import from anywhere but a grammar file beside the FBL document is not admitted; write '%import .<file> (names)' (FBL §4.9).");
        }
    }

    private static void CheckDefinition(string text, Match match, string where, List<string> problems)
    {
        var head = Head().Match(match.Value);
        var after = text[(match.Index + match.Length)..].TrimStart(' ', '\t');
        if (!(after.StartsWith(':') || after.StartsWith('{'))) return;
        if (head.Groups[1].Value == "!") problems.Add($"{where}: the keep-all-tokens rule '{match.Value}' is not admitted (ATD §4.1).");
        if (head.Groups[3].Success && char.IsLower(head.Groups[2].Value[0])) problems.Add($"{where}: the rule priority of '{match.Value}' is not admitted (ATD §4.1).");
        if (after.StartsWith('{')) problems.Add($"{where}: the template '{match.Value}{{…}}' is not admitted (ATD §4.1).");
    }

    /// <summary>What a terminal's regular expression uses outside the subset of ATD §3.5, as phrases.</summary>
    private static IReadOnlyList<string> RegexProblems(string pattern)
    {
        var problems = new List<string>();
        var i = 0;
        var inClass = false;
        var afterAtom = false;
        while (i < pattern.Length)
        {
            var c = pattern[i];
            if (c == '\\')
            {
                if (i + 1 >= pattern.Length)
                {
                    problems.Add("a trailing backslash");
                    break;
                }
                var e = pattern[i + 1];
                if (char.IsAsciiDigit(e)) problems.Add($"the backreference '\\{e}'");
                else if (char.IsLetter(e) && !AdmittedEscapes.Contains(e, StringComparison.Ordinal)) problems.Add($"the escape '\\{e}'");
                i += 2;
                afterAtom = true;
                continue;
            }
            if (inClass)
            {
                if (c == ']') inClass = false;
                i++;
                continue;
            }
            if (c == '[')
            {
                inClass = true;
                if (At(pattern, i + 1) == '^') i++;
                if (At(pattern, i + 1) == ']') i++;
                i++;
                afterAtom = true;
                continue;
            }
            if (c is '^' or '$') problems.Add($"the anchor '{c}'");
            else if (c == '.') problems.Add("'.' outside a character class (write the class: engines differ on which line ends '.' takes)");
            else if (c == '(' && At(pattern, i + 1) == '?' && At(pattern, i + 2) != ':') problems.Add($"the group '{pattern.Substring(i, Math.Min(3, pattern.Length - i))}' (only '(…)' and '(?:…)' are admitted)");
            else if (c is '*' or '+' or '?' or '}' && At(pattern, i + 1) is '?' or '+' && afterAtom)
            {
                problems.Add($"the lazy or possessive quantifier '{c}{pattern[i + 1]}'");
                i++;
            }
            afterAtom = c is not ('(' or '|');
            i++;
        }
        return problems;
    }

    private static char At(string text, int index) => index < text.Length ? text[index] : '\0';

    /// <summary>Lark grammar text in tokens: enough to find the constructs the subset does not admit, never inside a string or a regular expression.</summary>
    [GeneratedRegex("""\G(?:(?<comment>//[^\n]*)|(?<string>"(?:[^"\\\n]|\\.)*"(?<stringflags>[a-z]*))|(?<regexp>/(?![/*])(?:[^/\\\n]|\\.)+/(?<regexpflags>[a-z]*))|(?<directive>%[a-z]+)|(?<word>[?!_]?[A-Za-z_][A-Za-z0-9_]*(?:\.\d+)?)|(?<range>\.\.)|(?<punctuation>->|[:|()\[\]{}?*+~.,])|(?<newline>\n)|(?<space>[ \t\r]+))""", RegexOptions.CultureInvariant)]
    private static partial Regex Token();

    [GeneratedRegex(@"^\.([a-z_]+)\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex ImportHead();

    [GeneratedRegex(@"%import\s+\.([a-z_]+)\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex ImportTarget();

    [GeneratedRegex(@"^([?!_]?)([A-Za-z_][A-Za-z0-9_]*)(\.\d+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex Head();

    [GeneratedRegex(@"^[?!]?([a-z_][a-z0-9_]*)(?:\.\d+)?[ \t]*:", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex RuleHead();

    [GeneratedRegex(@"%import\s+\.[a-z_]+\s*\(([^)]*)\)", RegexOptions.CultureInvariant)]
    private static partial Regex ImportedNames();
}
