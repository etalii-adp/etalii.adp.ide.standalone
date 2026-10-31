using System.Text.RegularExpressions;
using EtAlii.Lark;

namespace EtAlii.Adp.Specification.Fbl.Grammar;

/// <summary>
/// Lexes the text a body read in part skips (FBL 0.9 §4.9, <i>Reading in part</i>, step 2), with every terminal of the
/// grammar as a candidate: at each position the longest match wins, then the terminal of the higher priority, then a
/// string terminal over a pattern one (ATD §3.6); a character no terminal matches is passed.
/// </summary>
internal sealed class GrammarScanner
{
    private readonly IReadOnlyList<ScannedTerminal> _terminals;

    public GrammarScanner(IEnumerable<TerminalDef> terminals, RegexOptions options) =>
        _terminals = [.. terminals.Select(t => new ScannedTerminal(t.Name, new Regex(@"\G(?:" + t.Pattern.ToRegexp() + ")", options), t.Priority, t.Pattern is PatternStr))];

    /// <summary>
    /// The end of the first token of a terminal of <paramref name="sync"/> lexed from <paramref name="from"/>, in UTF-16
    /// positions of <paramref name="text"/>; null when no such token follows.
    /// </summary>
    public int? EndOfSync(string text, int from, IReadOnlySet<string> sync)
    {
        var position = from;
        while (position < text.Length)
        {
            if (Longest(text, position) is not { } token)
            {
                position += char.IsHighSurrogate(text[position]) && position + 1 < text.Length && char.IsLowSurrogate(text[position + 1]) ? 2 : 1;
                continue;
            }
            position += token.Length;
            if (sync.Contains(token.Terminal.Name)) return position;
        }
        return null;
    }

    private (ScannedTerminal Terminal, int Length)? Longest(string text, int position)
    {
        (ScannedTerminal Terminal, int Length)? best = null;
        foreach (var terminal in _terminals)
        {
            var match = terminal.Pattern.Match(text, position);
            if (!match.Success || match.Length == 0) continue;
            if (best is not { } chosen || Wins(terminal, match.Length, chosen.Terminal, chosen.Length)) best = (terminal, match.Length);
        }
        return best;
    }

    private static bool Wins(ScannedTerminal terminal, int length, ScannedTerminal chosen, int chosenLength) =>
        length != chosenLength ? length > chosenLength
        : terminal.Priority != chosen.Priority ? terminal.Priority > chosen.Priority
        : terminal.IsString && !chosen.IsString;

    private sealed record ScannedTerminal(string Name, Regex Pattern, int Priority, bool IsString);
}
