using System.Text.RegularExpressions;
using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>
/// Reads the behavior tree out of a Markdown file: the first bullet list under the first heading
/// called Behavior. Never throws, and never reads anything else in the file.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything outside the list is the author's</b> - the title, the prose about the agent,
/// other sections, code blocks - and is neither modelled nor touched. It survives every edit by
/// never being spliced, which is how this module keeps the promise that a file it did not change
/// comes back byte for byte.
/// </para>
/// <para>
/// <b>Nesting is indentation, as Markdown means it.</b> An item indented further than the one
/// before it is that item's child; an item at or left of an earlier one closes it. A plain line
/// indented under an item is that item's notes. Tabs count as four columns, which is what
/// CommonMark's list rules make of them often enough for a hand-written file.
/// </para>
/// <para>
/// <b>An item without a keyword is read as a Do</b> rather than dropped, so a list written by hand
/// before the author knew the keywords still draws; the problem it records says how it was read.
/// </para>
/// </remarks>
public static partial class AbmParser
{
    /// <summary>The model of <paramref name="document"/>.</summary>
    public static AbmModel Parse(LineDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var lines = document.Lines.Select(line => line.Text).ToArray();
        var fenced = FencedLines(lines);

        // The section: the first Behavior heading, up to the next heading of its level or above.
        int? sectionLine = null;
        var level = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            if (!fenced[index] && Heading().Match(lines[index]) is { Success: true } heading && IsBehavior(heading.Groups["text"].Value))
            {
                sectionLine = index;
                level = heading.Groups["hashes"].Length;
                break;
            }
        }

        if (sectionLine is not { } start)
        {
            return AbmModel.Empty;
        }

        var sectionEnd = lines.Length - 1;
        for (var index = start + 1; index < lines.Length; index++)
        {
            if (!fenced[index] && Heading().Match(lines[index]) is { Success: true } heading && heading.Groups["hashes"].Length <= level)
            {
                sectionEnd = index - 1;
                break;
            }
        }

        // The list: its first item sets the column the roots sit at.
        var listStart = -1;
        for (var index = start + 1; index <= sectionEnd; index++)
        {
            if (!fenced[index] && Item().IsMatch(lines[index]))
            {
                listStart = index;
                break;
            }
        }

        if (listStart < 0)
        {
            return new AbmModel([], start, sectionEnd, 0, []);
        }

        var baseIndent = ColumnOf(lines[listStart]);
        var raws = new List<RawItem>();
        for (var index = listStart; index <= sectionEnd; index++)
        {
            var text = lines[index];
            if (text.Trim().Length == 0)
            {
                continue; // a blank line inside a list is a loose list, not its end
            }

            if (Item().Match(text) is { Success: true } item && ColumnOf(text) >= baseIndent)
            {
                raws.Add(new RawItem(index, item));
                continue;
            }

            if (ColumnOf(text) > baseIndent && raws.Count > 0)
            {
                raws[^1].NoteLines.Add(index);
                continue;
            }

            break; // a line at the list's own column that is not an item ends the list
        }

        return Build(raws, lines, start, sectionEnd, baseIndent);
    }

    /// <summary>The column <paramref name="text"/>'s first non-blank character sits at, a tab counting four.</summary>
    public static int ColumnOf(string text)
    {
        var column = 0;
        foreach (var character in text)
        {
            if (character == ' ')
            {
                column++;
            }
            else if (character == '\t')
            {
                column += 4 - (column % 4);
            }
            else
            {
                break;
            }
        }

        return column;
    }

    private static AbmModel Build(List<RawItem> raws, string[] lines, int sectionLine, int sectionEnd, int baseIndent)
    {
        var problems = new List<AbmProblem>();
        var parents = new int[raws.Count];
        var childCounts = new int[raws.Count];
        var ids = new string[raws.Count];
        var stack = new Stack<int>();
        var rootCount = 0;

        for (var index = 0; index < raws.Count; index++)
        {
            var indent = ColumnOf(raws[index].Match.Value);
            while (stack.Count > 0 && ColumnOf(raws[stack.Peek()].Match.Value) >= indent)
            {
                stack.Pop();
            }

            if (stack.Count == 0)
            {
                parents[index] = -1;
                ids[index] = (++rootCount).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                var parent = stack.Peek();
                parents[index] = parent;
                ids[index] = $"{ids[parent]}.{++childCounts[parent]}";
            }

            stack.Push(index);
        }

        // A node's last line: its notes, then whatever its last descendant reaches.
        var ends = raws.Select(raw => raw.NoteLines.Count > 0 ? raw.NoteLines[^1] : raw.Line).ToArray();
        for (var index = raws.Count - 1; index >= 0; index--)
        {
            if (parents[index] >= 0)
            {
                ends[parents[index]] = Math.Max(ends[parents[index]], ends[index]);
            }
        }

        var nodes = new List<AbmNode>(raws.Count);
        for (var index = 0; index < raws.Count; index++)
        {
            var raw = raws[index];
            var indent = ColumnOf(raw.Match.Value);
            var contentIndent = indent + 1 + ColumnWidth(raw.Match.Groups["space"].Value, indent + 1);
            (string kind, int retryCount, string label, string keywordText, bool hasKeyword) = ReadText(raw.Match.Groups["text"].Value.TrimEnd());
            if (!hasKeyword)
            {
                problems.Add(new AbmProblem(
                    AbmRuleIds.NoKeyword,
                    $"\"{Shorten(label)}\" has no keyword, so it is read as a Do. Start it with one, such as **Do:** or **Check:**.",
                    raw.Line));
            }

            var notes = raw.NoteLines.Count == 0
                ? ""
                : string.Join('\n', Enumerable.Range(raw.NoteLines[0], raw.NoteLines[^1] - raw.NoteLines[0] + 1).Select(line => DedentText(lines[line], contentIndent)).ToArray()).Trim('\n');

            nodes.Add(new AbmNode(
                ids[index],
                kind,
                label,
                retryCount,
                notes,
                hasKeyword,
                raw.Line,
                raw.NoteLines.Count > 0 ? new LineRange(raw.NoteLines[0], raw.NoteLines[^1]) : null,
                ends[index],
                indent,
                contentIndent,
                raw.Match.Groups["marker"].Value[0],
                keywordText,
                parents[index] >= 0 ? ids[parents[index]] : null,
                [.. Enumerable.Range(0, raws.Count).Where(child => parents[child] == index).Select(child => ids[child])]));
        }

        return new AbmModel(nodes, sectionLine, sectionEnd, baseIndent, problems);
    }

    /// <summary>The item's kind, label and keyword, or a Do labelled with the whole text when it carries no keyword.</summary>
    private static (string Kind, int RetryCount, string Label, string KeywordText, bool HasKeyword) ReadText(string text)
    {
        if (BoldLead().Match(text) is { Success: true } lead)
        {
            var keyword = lead.Groups["keyword"].Value.Trim();
            var label = lead.Groups["label"].Value;
            if (keyword.EndsWith(':'))
            {
                keyword = keyword[..^1].TrimEnd();
            }
            else if (label.StartsWith(':'))
            {
                label = label[1..];
            }

            if (AbmNodeKinds.FromKeyword(keyword) is { } known)
            {
                return (known.Kind.Id, known.RetryCount, label.Trim(), keyword, true);
            }
        }

        return (AbmNodeKinds.Action, 0, text.Trim(), "", false);
    }

    /// <summary>Removes up to <paramref name="columns"/> columns of leading whitespace.</summary>
    public static string DedentText(string text, int columns)
    {
        var column = 0;
        var index = 0;
        while (index < text.Length && column < columns && (text[index] == ' ' || text[index] == '\t'))
        {
            column += text[index] == '\t' ? 4 - (column % 4) : 1;
            index++;
        }

        return text[index..].TrimEnd();
    }

    private static int ColumnWidth(string whitespace, int startColumn)
    {
        var column = startColumn;
        foreach (var character in whitespace)
        {
            column += character == '\t' ? 4 - (column % 4) : 1;
        }

        return column - startColumn;
    }

    private static string Shorten(string text) => text.Length <= 60 ? text : text[..57] + "...";

    private static bool IsBehavior(string headingText) =>
        string.Equals(headingText.Trim(), "Behavior", StringComparison.OrdinalIgnoreCase)
        || string.Equals(headingText.Trim(), "Behaviour", StringComparison.OrdinalIgnoreCase);

    /// <summary>Which lines are inside a fenced code block, fences included - never read as headings or items.</summary>
    private static bool[] FencedLines(string[] lines)
    {
        var fenced = new bool[lines.Length];
        string? open = null;
        for (var index = 0; index < lines.Length; index++)
        {
            var trimmed = lines[index].TrimStart();
            if (open is null)
            {
                if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
                {
                    open = trimmed[..3];
                    fenced[index] = true;
                }

                continue;
            }

            fenced[index] = true;
            if (trimmed.StartsWith(open, StringComparison.Ordinal))
            {
                open = null;
            }
        }

        return fenced;
    }

    private sealed class RawItem(int line, Match match)
    {
        public int Line { get; } = line;

        public Match Match { get; } = match;

        public List<int> NoteLines { get; } = [];
    }

    [GeneratedRegex(@"^ {0,3}(?<hashes>#{1,6})\s+(?<text>.*?)\s*#*\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex Heading();

    [GeneratedRegex(@"^(?<indent>[ \t]*)(?<marker>[-*+])(?<space>[ \t]+)(?<text>(?![-*_](\s*[-*_]){2,}\s*$)\S.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex Item();

    [GeneratedRegex(@"^\*\*(?<keyword>[^*]+?)\*\*(?<label>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex BoldLead();
}
