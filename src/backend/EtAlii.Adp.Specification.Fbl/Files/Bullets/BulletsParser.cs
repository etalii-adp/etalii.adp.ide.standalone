using System.Text;
using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Fbl.Lines;
using EtAlii.Adp.Specification.Fbl.Text;

namespace EtAlii.Adp.Specification.Fbl.Bullets;

/// <summary>
/// The reading of a body of the <c>bullets</c> family into its kinds of line (FBL 0.7 §4.8): list items
/// nested by their indentation, headings, statements, and the fences and thematic breaks that are none
/// of these. It mirrors <c>bullets_of</c> in etalii.adp's <c>.github/scripts/validate-examples.py</c>.
/// </summary>
internal sealed partial class BulletsParser(BodyText text)
{
    [GeneratedRegex(@"^[ \t]*(?:(?:-[ \t]*){3,}|(?:\*[ \t]*){3,}|(?:_[ \t]*){3,})$", RegexOptions.CultureInvariant)]
    private static partial Regex ThematicBreak();

    [GeneratedRegex("^(`{3,}|~{3,})", RegexOptions.CultureInvariant)]
    private static partial Regex FenceOpening();

    [GeneratedRegex(@"^(#{1,6})(?:[ \t]|$)", RegexOptions.CultureInvariant)]
    private static partial Regex HeadingMark();

    [GeneratedRegex(@"(?:^|[ \t]+)#+$", RegexOptions.CultureInvariant)]
    private static partial Regex ClosingHashes();

    /// <summary>Every item and statement, in document order.</summary>
    public List<Statement> Entries { get; } = [];

    public List<Heading> Headings { get; } = [];

    /// <summary>The content of every line that is not blank: every byte of the body is in one, or is whitespace.</summary>
    public List<Span> Leaves { get; } = [];

    public void Parse()
    {
        var open = new List<Bullet>();
        (char Character, int Length, Bullet? Owner)? fence = null;
        for (var index = 0; index < text.Lines.Count; index++)
        {
            var line = text.Lines[index];
            var start = index == 0 ? Math.Max(line.Start, text.BomLength) : line.Start;
            var first = start;
            while (first < line.ContentEnd && text.Bytes[first] is (byte)' ' or (byte)'\t') first++;
            if (first < line.ContentEnd) Leaves.Add(new Span(first, line.ContentEnd));
            var rest = text.Text(first, line.ContentEnd);
            if (fence is { } inside)
            {
                if (Closes(rest, inside.Character, inside.Length)) fence = null;
                inside.Owner?.AddLine(index);
                continue;
            }
            if (first == line.ContentEnd)
            {
                if (open.Count > 0) open[^1].AddBlank(index);
                continue;
            }
            var indentation = Columns.Of(text.Text(start, first));
            while (open.Count > 0 && open[^1].MarkerColumn >= indentation) open.RemoveAt(open.Count - 1);
            var whole = text.Text(start, line.ContentEnd);
            if (rest[0] is '-' or '*' or '+' && (rest.Length == 1 || rest[1] is ' ' or '\t') && !ThematicBreak().IsMatch(whole))
            {
                var item = Item(index, start, first, indentation, rest, open.Count > 0 ? open[^1] : null);
                Entries.Add(item);
                open.Add(item);
                continue;
            }
            var opens = FenceOpening().Match(rest);
            if (open.Count > 0)
            {
                open[^1].AddLine(index);
                if (opens.Success) fence = (opens.Groups[1].Value[0], opens.Groups[1].Length, open[^1]);
                continue;
            }
            var heading = indentation <= 3 ? HeadingMark().Match(rest) : Match.Empty;
            if (heading.Success)
            {
                var title = ClosingHashes().Replace(rest[heading.Groups[1].Length..].Trim(' ', '\t'), "").Trim(' ', '\t');
                Headings.Add(new Heading(index, heading.Groups[1].Length, title));
            }
            else if (opens.Success)
            {
                fence = (opens.Groups[1].Value[0], opens.Groups[1].Length, null);
            }
            else if (!ThematicBreak().IsMatch(whole))
            {
                Entries.Add(Statement(index, start, first, rest));
            }
        }
        foreach (var item in Entries.OfType<Bullet>().Reverse()) item.Close(text);
    }

    private static bool Closes(string rest, char character, int length)
    {
        var run = 0;
        while (run < rest.Length && rest[run] == character) run++;
        return run >= length && rest[run..].All(c => c is ' ' or '\t');
    }

    private Bullet Item(int index, int start, int first, int indentation, string rest, Bullet? parent)
    {
        var line = text.Lines[index];
        var after = rest[1..];
        var itemText = after.TrimStart(' ', '\t');
        var whitespace = after[..^itemText.Length];
        var textStart = first + 1 + Encoding.UTF8.GetByteCount(whitespace);
        var item = new Bullet
        {
            FirstLine = index,
            Content = itemText,
            Offsets = LinesParser.ByteOffsets(itemText, textStart),
            Own = new Span(first, line.ContentEnd),
            LineSpan = new Span(start, line.End),
            Indent = first - start,
            Parent = parent,
            MarkerColumn = indentation,
            ContentColumn = itemText.Length > 0 ? indentation + 1 + Columns.Of(whitespace) : indentation + 2,
            Marker = rest[0],
            IndentText = text.Text(start, first),
            FirstLineSpan = new Span(textStart, line.ContentEnd),
        };
        parent?.Nest(item);
        return item;
    }

    private Statement Statement(int index, int start, int first, string rest)
    {
        var line = text.Lines[index];
        var trimmed = rest.TrimEnd(' ', '\t');
        var statement = new Statement
        {
            FirstLine = index,
            Content = rest,
            Offsets = LinesParser.ByteOffsets(rest, first),
            Own = new Span(first, first + Encoding.UTF8.GetByteCount(trimmed)),
            LineSpan = new Span(start, line.End),
            Indent = first - start,
        };
        statement.FirstLineSpan = statement.Own;
        return statement;
    }
}
