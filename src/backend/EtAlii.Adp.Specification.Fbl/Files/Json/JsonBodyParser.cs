using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Fbl.Rules;
using EtAlii.Adp.Specification.Fbl.Text;

namespace EtAlii.Adp.Specification.Fbl.Json;

/// <summary>
/// The lossless reading of a JSON body (FBL §4.4) on the platform's <see cref="Utf8JsonReader"/>, which
/// judges RFC 8259 and gives each token's byte offset: members and items with their own spans and the
/// separator after each, comments refused, every name and scalar a leaf.
/// </summary>
internal sealed partial class JsonBodyParser(BodyText text)
{
    /// <summary>Deeper than any body a person writes, and shallow enough that the reader, not the stack, stops a hostile one.</summary>
    private const int MaxDepth = 1024;

    public List<Span> Leaves { get; } = [];

    /// <summary>Duplicate names found: the name spans of the second and later occurrences in an object.</summary>
    public List<(string Name, Span Span)> Duplicates { get; } = [];

    public sealed class JsonError(int offset, string message) : Exception(message)
    {
        public int Offset { get; } = offset;
    }

    /// <summary>A container being read, with the member name its next value goes under.</summary>
    private sealed class Open(ValueKind kind, int start)
    {
        public ValueKind Kind { get; } = kind;

        public int Start { get; } = start;

        public List<TreeEntry> Entries { get; } = [];

        public HashSet<string> Names { get; } = new(StringComparer.Ordinal);

        public (string Name, Span Span)? Member { get; set; }
    }

    public TreeValue Parse()
    {
        var bom = text.BomLength;
        var reader = new Utf8JsonReader(text.Bytes.AsSpan(bom), new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow, MaxDepth = MaxDepth });
        var open = new Stack<Open>();
        TreeValue? root = null;
        try
        {
            while (reader.Read())
            {
                var at = bom + (int)reader.TokenStartIndex;
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        open.Push(new Open(ValueKind.Mapping, at));
                        break;
                    case JsonTokenType.StartArray:
                        open.Push(new Open(ValueKind.Sequence, at));
                        break;
                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                    {
                        var closed = open.Pop();
                        var value = new TreeValue { Kind = closed.Kind, Style = ValueStyle.Block, Span = new Span(closed.Start, at + 1) };
                        value.Entries.AddRange(closed.Entries);
                        root = Place(open, value) ?? root;
                        break;
                    }
                    case JsonTokenType.PropertyName:
                        open.Peek().Member = (String(ref reader), Leaf(at, at + reader.ValueSpan.Length + 2));
                        break;
                    case JsonTokenType.String:
                    {
                        var value = String(ref reader);
                        var span = Leaf(at, at + reader.ValueSpan.Length + 2);
                        root = Place(open, new TreeValue { Kind = ValueKind.Scalar, Style = ValueStyle.JsonString, Span = span, Text = value, Typed = value }) ?? root;
                        break;
                    }
                    default:
                    {
                        var raw = Encoding.UTF8.GetString(reader.ValueSpan);
                        var span = Leaf(at, at + reader.ValueSpan.Length);
                        root = Place(open, new TreeValue { Kind = ValueKind.Scalar, Style = ValueStyle.JsonOther, Span = span, Text = raw, Typed = Typed(reader.TokenType, raw) }) ?? root;
                        break;
                    }
                }
            }
        }
        catch (JsonException error)
        {
            throw new JsonError(Offset(error), Sentence(error.Message));
        }
        return root ?? throw new JsonError(text.Length, "The body holds no JSON value.");
    }

    /// <summary>Puts a value read whole into the container being read, as a member or an item; a value outside every container is the root, which is returned.</summary>
    private TreeValue? Place(Stack<Open> open, TreeValue value)
    {
        if (!open.TryPeek(out var container)) return value;
        TreeEntry entry;
        if (container.Member is ({ } name, var key))
        {
            entry = new TreeEntry { Name = name, KeySpan = key, Value = value, Own = new Span(key.Start, value.Span.End), Indent = Column(key.Start) };
            container.Member = null;
            if (container.Names.Add(name)) container.Entries.Add(entry);
            else Duplicates.Add((name, key));
        }
        else
        {
            entry = new TreeEntry { Value = value, Own = value.Span, Indent = Column(value.Span.Start) };
            container.Entries.Add(entry);
        }
        entry.Separator = SeparatorAfter(value.Span.End);
        return null;
    }

    private Span Leaf(int start, int end)
    {
        var span = new Span(start, end);
        Leaves.Add(span);
        return span;
    }

    /// <summary>The offset of the comma after a value, or -1 when whitespace leads to the container's end instead.</summary>
    private int SeparatorAfter(int end)
    {
        var bytes = text.Bytes;
        while (end < bytes.Length && bytes[end] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') end++;
        return end < bytes.Length && bytes[end] == (byte)',' ? end : -1;
    }

    private int Column(int offset)
    {
        var line = text.Lines[text.LineIndexAt(offset)];
        return offset - Math.Max(line.Start, text.LineIndexAt(offset) == 0 ? text.BomLength : 0);
    }

    private static object? Typed(JsonTokenType token, string raw) => token switch
    {
        JsonTokenType.True => true,
        JsonTokenType.False => false,
        JsonTokenType.Null => null,
        _ when long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole) => whole,
        _ => double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// A string's text. The reader refuses to decode an escaped surrogate without its pair, which
    /// RFC 8259 §8.2 lets a text hold, so such a string is unescaped here as it is written.
    /// </summary>
    private static string String(ref Utf8JsonReader reader)
    {
        try
        {
            return reader.GetString()!;
        }
        catch (InvalidOperationException)
        {
            return Unescape(Encoding.UTF8.GetString(reader.ValueSpan));
        }
    }

    private static string Unescape(string escaped)
    {
        var text = new StringBuilder();
        for (var i = 0; i < escaped.Length; i++)
        {
            if (escaped[i] != '\\')
            {
                text.Append(escaped[i]);
                continue;
            }
            var escape = escaped[++i];
            text.Append(escape switch
            {
                'b' => '\b',
                'f' => '\f',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                'u' => (char)int.Parse(escaped.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                _ => escape,
            });
            if (escape == 'u') i += 4;
        }
        return text.ToString();
    }

    /// <summary>The byte offset the reader stopped at: it counts lines by their line feeds and bytes within the line, from after the byte-order mark.</summary>
    private int Offset(JsonException error)
    {
        var offset = text.BomLength;
        for (var line = 0L; line < (error.LineNumber ?? 0) && offset < text.Length; offset++)
        {
            if (text.Bytes[offset] == (byte)'\n') line++;
        }
        return Math.Min(text.Length, offset + (int)(error.BytePositionInLine ?? 0));
    }

    /// <summary>The reader's own sentence, without the line and position it appends; the reading reports the offset itself.</summary>
    private static string Sentence(string message) => Position().Replace(message, "");

    [GeneratedRegex(@"\s*LineNumber: \d+ \| BytePositionInLine: \d+\.$")]
    private static partial Regex Position();
}
