using System.Text;
using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Fbl.Rules;
using EtAlii.Adp.Specification.Fbl.Text;

namespace EtAlii.Adp.Specification.Fbl.Lines;

/// <summary>
/// A statement of the <c>lines</c> or <c>blocks</c> family (FBL §4.6, §4.7): one line, or, when it
/// opens a block, the lines up to and including the one holding the matching <c>}</c>.
/// </summary>
internal class Statement : Entry
{
    public required int FirstLine { get; init; }

    /// <summary>The statement's first line from its first non-whitespace byte, as the rules' <c>line</c> expressions see it.</summary>
    public required string Content { get; init; }

    /// <summary>The byte offset of every UTF-16 index of <see cref="Content"/>, and one past its end.</summary>
    public required int[] Offsets { get; init; }

    public bool Opens { get; set; }

    public bool IsHeader { get; set; }

    /// <summary>The span of the first line's content, without trailing whitespace: what <c>re-emit-line</c> replaces.</summary>
    public Span FirstLineSpan { get; set; }
}

/// <summary>
/// The reading of a body of the <c>lines</c> or <c>blocks</c> family (FBL §4.6, §4.7) into statements:
/// a line that is not blank and not a comment is one, and in <c>blocks</c> a statement that ends with
/// <c>{</c> holds the lines up to the matching <c>}</c>.
/// </summary>
internal sealed class LinesParser(BodyText text, BoundedRegex? comment, bool blocks)
{
    /// <summary>The indexes of the lines that are comments.</summary>
    private readonly HashSet<int> _commentLines = [];

    public List<Statement> Statements { get; } = [];

    public List<Span> Leaves { get; } = [];

    public sealed class LinesError(int offset, string message) : Exception(message)
    {
        public int Offset { get; } = offset;
    }

    public void Parse()
    {
        var lines = text.Lines;
        var stack = new Stack<Statement>();
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var start = index == 0 ? Math.Max(line.Start, text.BomLength) : line.Start;
            var first = start;
            while (first < line.ContentEnd && text.Bytes[first] is (byte)' ' or (byte)'\t') first++;
            if (first == line.ContentEnd) continue;
            var content = text.Text(first, line.ContentEnd);
            if (IsComment(text.Text(start, line.ContentEnd)))
            {
                _commentLines.Add(index);
                Leaves.Add(new Span(first, line.ContentEnd));
                continue;
            }
            var trimmed = content.TrimEnd(' ', '\t');
            if (blocks && trimmed == "}")
            {
                if (stack.Count == 0)
                {
                    throw new LinesError(first, "The braces of the body do not balance: this '}' closes no block.");
                }
                var opener = stack.Pop();
                opener.LineSpan = new Span(opener.LineSpan!.Value.Start, line.End);
                opener.Own = opener.Own with { End = first + 1 };
                continue;
            }
            var statement = new Statement
            {
                FirstLine = index,
                Content = content,
                Offsets = ByteOffsets(content, first),
                Own = new Span(first, first + Encoding.UTF8.GetByteCount(trimmed)),
                Indent = first - line.Start,
                Parent = stack.Count > 0 ? stack.Peek() : null,
            };
            statement.FirstLineSpan = statement.Own;
            statement.LineSpan = new Span(LeadingCommentStart(index), line.End);
            statement.Parent?.Children.Add(statement);
            Statements.Add(statement);
            Leaves.Add(new Span(first, line.ContentEnd));
            if (blocks)
            {
                (int net, bool endsWithOpen) = Braces(content);
                if (endsWithOpen && net == 1)
                {
                    statement.Opens = true;
                    stack.Push(statement);
                }
                else if (net != 0)
                {
                    throw new LinesError(first, "The braces of the body do not balance on this line.");
                }
            }
        }
        if (stack.Count > 0)
        {
            throw new LinesError(stack.Peek().Own.Start, "The braces of the body do not balance: this block is never closed.");
        }
    }

    private int LeadingCommentStart(int index)
    {
        var start = text.Lines[index].Start;
        for (var above = index - 1; above >= 0 && _commentLines.Contains(above); above--)
        {
            start = text.Lines[above].Start;
        }
        return start;
    }

    private static (int Net, bool EndsWithOpen) Braces(string content)
    {
        var net = 0;
        var inString = false;
        var last = '\0';
        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (inString)
            {
                if (c == '\\' && i + 1 < content.Length) i++;
                else if (c == '"') inString = false;
                last = c;
                continue;
            }
            if (c == '"') inString = true;
            else if (c == '{') net++;
            else if (c == '}') net--;
            if (c is not (' ' or '\t')) last = c;
        }
        return (net, !inString && last == '{');
    }

    internal static int[] ByteOffsets(string content, int start)
    {
        var offsets = new int[content.Length + 1];
        var offset = start;
        for (var i = 0; i < content.Length; i++)
        {
            offsets[i] = offset;
            if (char.IsHighSurrogate(content[i]) && i + 1 < content.Length)
            {
                offsets[i + 1] = offset;
                offset += 4;
                i++;
                continue;
            }
            offset += Encoding.UTF8.GetByteCount(content.AsSpan(i, 1));
        }
        offsets[content.Length] = offset;
        return offsets;
    }

    /// <summary>Whether a whole line is a comment; an expression that takes too long on it reads it as a statement.</summary>
    private bool IsComment(string line)
    {
        try
        {
            return comment is not null && comment.IsMatch(line);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
