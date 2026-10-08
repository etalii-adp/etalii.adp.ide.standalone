namespace EtAlii.Adp.Specification.Fbl.Text;

using System.Text;

/// <summary>
/// A body's bytes with FBL's view of them (FBL §2.6): UTF-8 with or without a byte-order mark, lines
/// and their endings (CRLF, LF or a lone CR), and conversion from a byte offset to a 1-based line and a
/// code-point column. Every offset is a UTF-8 byte offset into the whole file, the mark included.
/// </summary>
public sealed class BodyText
{
    private static readonly UTF8Encoding _strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly List<TextLine> _lines = [];

    public BodyText(byte[] bytes)
    {
        Bytes = bytes;
        BomLength = bytes is [0xEF, 0xBB, 0xBF, ..] ? 3 : 0;
        try
        {
            _ = _strict.GetCharCount(bytes, BomLength, bytes.Length - BomLength);
            IsValidUtf8 = true;
        }
        catch (DecoderFallbackException e)
        {
            IsValidUtf8 = false;
            InvalidOffset = BomLength + Math.Max(0, e.Index);
        }
        BuildLines();
    }

    public byte[] Bytes { get; }

    public int Length => Bytes.Length;

    /// <summary>3 when the body starts with a UTF-8 byte-order mark, else 0. The mark belongs to no node.</summary>
    public int BomLength { get; }

    public bool IsValidUtf8 { get; }

    public int InvalidOffset { get; }

    public IReadOnlyList<TextLine> Lines => _lines;

    /// <summary>The bytes as text, without the byte-order mark. Only meaningful for valid UTF-8.</summary>
    public string Text(int start, int end) => Encoding.UTF8.GetString(Bytes, start, end - start);

    public string Text(Span span) => Text(span.Start, span.End);

    /// <summary>
    /// The ending that ends the most lines, CRLF winning a tie with LF; a lone CR counts as neither
    /// (FBL §6.3). Null when no line has an ending.
    /// </summary>
    public string? DominantEnding
    {
        get
        {
            var crlf = 0;
            var lf = 0;
            var cr = 0;
            foreach (var line in _lines)
            {
                switch (line.Ending)
                {
                    case "\r\n": crlf++; break;
                    case "\n": lf++; break;
                    case "\r": cr++; break;
                }
            }
            if (crlf == 0 && lf == 0) return cr > 0 ? "\r" : null;
            return crlf >= lf ? "\r\n" : "\n";
        }
    }

    /// <summary>The line ending a splice at <paramref name="offset"/> writes (FBL §6.3).</summary>
    public string NewlineAt(int offset, string fallback)
    {
        var line = _lines[LineIndexAt(offset)];
        if (line.Ending.Length > 0) return line.Ending;
        return DominantEnding ?? fallback;
    }

    /// <summary>The 0-based index of the line holding <paramref name="offset"/>.</summary>
    public int LineIndexAt(int offset)
    {
        var lo = 0;
        var hi = _lines.Count - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (_lines[mid].Start <= offset) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    /// <summary>The 1-based line and code-point column of <paramref name="offset"/>.</summary>
    public (int Line, int Column) Position(int offset)
    {
        var index = LineIndexAt(offset);
        var start = _lines[index].Start;
        if (index == 0) start = Math.Max(start, BomLength);
        var column = 1;
        for (var i = start; i < offset && i < Bytes.Length; i++)
        {
            if ((Bytes[i] & 0xC0) != 0x80) column++;
        }
        return (index + 1, column);
    }

    /// <summary>The number of code points in a span, for a finding's length.</summary>
    public int CodePoints(int start, int end)
    {
        var count = 0;
        for (var i = start; i < end && i < Bytes.Length; i++)
        {
            if ((Bytes[i] & 0xC0) != 0x80) count++;
        }
        return count;
    }

    private void BuildLines()
    {
        var start = 0;
        var i = 0;
        var bytes = Bytes;
        while (i < bytes.Length)
        {
            var b = bytes[i];
            if (b == (byte)'\r')
            {
                if (i + 1 < bytes.Length && bytes[i + 1] == (byte)'\n')
                {
                    _lines.Add(new TextLine(start, i, i + 2, "\r\n"));
                    i += 2;
                }
                else
                {
                    _lines.Add(new TextLine(start, i, i + 1, "\r"));
                    i += 1;
                }
                start = i;
            }
            else if (b == (byte)'\n')
            {
                _lines.Add(new TextLine(start, i, i + 1, "\n"));
                i += 1;
                start = i;
            }
            else
            {
                i++;
            }
        }
        if (start < bytes.Length || _lines.Count == 0)
        {
            _lines.Add(new TextLine(start, bytes.Length, bytes.Length, ""));
        }
    }
}

/// <summary>One line: <see cref="Start"/> to <see cref="ContentEnd"/> is the line, up to <see cref="End"/> its ending.</summary>
public readonly record struct TextLine(int Start, int ContentEnd, int End, string Ending);
