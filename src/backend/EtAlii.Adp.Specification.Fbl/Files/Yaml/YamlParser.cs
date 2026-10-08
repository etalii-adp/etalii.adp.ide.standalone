using System.Text;
using EtAlii.Adp.Specification.Fbl.Rules;
using EtAlii.Adp.Specification.Fbl.Text;

namespace EtAlii.Adp.Specification.Fbl.Yaml;

/// <summary>
/// The lossless reading of a YAML body's first document (FBL §4.3): block mappings and sequences,
/// plain, quoted and block scalars, flow collections as one value, anchors and aliases, every node
/// with its span as written. Well-formedness is YamlDotNet's to judge before this runs; this parser
/// takes the structure and the spans, which YamlDotNet's character marks do not give.
/// </summary>
internal sealed class YamlParser(BodyText text)
{
    private readonly byte[] _bytes = text.Bytes;
    private readonly Dictionary<string, TreeValue> _anchors = new(StringComparer.Ordinal);
    private int _line;
    private int _end;

    public List<Span> Leaves { get; } = [];

    /// <summary>Duplicate keys found: the key spans of the second and later occurrences.</summary>
    public List<(string Name, Span Span)> Duplicates { get; } = [];

    /// <summary>Where the first document ends; everything after it is unbound (FBL §4.3).</summary>
    private int DocumentEnd { get; set; }

    public sealed class YamlError(int offset, string message) : Exception(message)
    {
        public int Offset { get; } = offset;
    }

    public TreeValue ParseDocument()
    {
        var lines = text.Lines;
        _line = 0;
        while (_line < lines.Count)
        {
            var p = FirstNonSpace(_line);
            if (p < lines[_line].ContentEnd && _bytes[p] == (byte)'%') { _line++; continue; }
            if (IsMarker(_line, "---"))
            {
                _line++;
                break;
            }
            if (IsSignificant(_line)) break;
            _line++;
        }
        _end = lines.Count;
        for (var i = _line; i < lines.Count; i++)
        {
            if (IsMarker(i, "---") || IsMarker(i, "..."))
            {
                _end = i;
                break;
            }
        }
        DocumentEnd = _end < lines.Count ? lines[_end].Start : text.Length;
        if (DocumentEnd < text.Length) Leaves.Add(new Span(DocumentEnd, text.Length));
        var root = ParseBlockNode(-1, false) ?? Empty(text.BomLength);
        var rest = NextSignificant(_line);
        if (rest < _end) throw new YamlError(FirstNonSpace(rest), "This line does not continue the structure above it.");
        return root;
    }

    // ---- lines ----

    private int LineStart(int line) => line == 0 ? Math.Max(text.Lines[0].Start, text.BomLength) : text.Lines[line].Start;

    private int ContentEnd(int line) => text.Lines[line].ContentEnd;

    private int FirstNonSpace(int line)
    {
        var p = LineStart(line);
        var end = ContentEnd(line);
        while (p < end && _bytes[p] is (byte)' ' or (byte)'\t') p++;
        return p;
    }

    private bool IsSignificant(int line)
    {
        var p = FirstNonSpace(line);
        return p < ContentEnd(line) && _bytes[p] != (byte)'#';
    }

    private int NextSignificant(int from)
    {
        var line = from;
        while (line < _end && !IsSignificant(line)) line++;
        return line;
    }

    private bool IsMarker(int line, string marker)
    {
        var start = LineStart(line);
        var end = ContentEnd(line);
        if (end - start < 3 || text.Text(start, start + 3) != marker) return false;
        return start + 3 == end || _bytes[start + 3] is (byte)' ' or (byte)'\t';
    }

    private int Column(int line, int offset) => offset - LineStart(line);

    private bool IsDash(int line, int p) =>
        _bytes[p] == (byte)'-' && (p + 1 == ContentEnd(line) || _bytes[p + 1] is (byte)' ' or (byte)'\t');

    private bool AtLineEnd(int line, int p)
    {
        while (p < ContentEnd(line) && _bytes[p] is (byte)' ' or (byte)'\t') p++;
        return p >= ContentEnd(line) || (_bytes[p] == (byte)'#' && (p == LineStart(line) || _bytes[p - 1] is (byte)' ' or (byte)'\t'));
    }

    private int SkipSpaces(int line, int p)
    {
        while (p < ContentEnd(line) && _bytes[p] is (byte)' ' or (byte)'\t') p++;
        return p;
    }

    private static TreeValue Empty(int offset) => new() { Kind = ValueKind.Scalar, Style = ValueStyle.Empty, Span = new Span(offset, offset), Typed = null };

    // ---- block structure ----

    private TreeValue? ParseBlockNode(int parentIndent, bool sequenceAtParent)
    {
        var line = NextSignificant(_line);
        if (line >= _end) return null;
        var p = FirstNonSpace(line);
        var column = Column(line, p);
        if (column < parentIndent) return null;
        if (column == parentIndent && !(sequenceAtParent && IsDash(line, p))) return null;
        _line = line;
        if (IsDash(line, p)) return ParseSequence(line, p, column);
        if (KeyAt(line, p) is not null) return ParseMapping(line, p, column);
        return ParseInline(line, p, parentIndent);
    }

    private TreeValue ParseSequence(int line, int p, int column)
    {
        var value = new TreeValue { Kind = ValueKind.Sequence, Style = ValueStyle.Block, Span = new Span(p, p) };
        while (true)
        {
            var dash = p;
            var q = SkipSpaces(line, p + 1);
            TreeValue item;
            if (AtLineEnd(line, q))
            {
                _line = line + 1;
                item = ParseBlockNode(column, false) ?? Empty(dash + 1);
            }
            else
            {
                _line = line;
                item = ParseContentAfterIndicator(line, q, column);
            }
            value.Entries.Add(new TreeEntry { Value = item, Own = new Span(dash, End(item, dash + 1)), Indent = column });
            var next = NextSignificant(_line);
            if (next >= _end) break;
            var np = FirstNonSpace(next);
            if (Column(next, np) != column || !IsDash(next, np)) break;
            line = next;
            p = np;
            _line = next;
        }
        value.Span = new Span(value.Entries[0].Own.Start, value.Entries[^1].Own.End);
        return value;
    }

    private TreeValue ParseContentAfterIndicator(int line, int q, int parentIndent)
    {
        var column = Column(line, q);
        if (IsDash(line, q)) return ParseSequence(line, q, column);
        if (KeyAt(line, q) is not null) return ParseMapping(line, q, column);
        return ParseInline(line, q, parentIndent);
    }

    private TreeValue ParseMapping(int line, int p, int column)
    {
        var value = new TreeValue { Kind = ValueKind.Mapping, Style = ValueStyle.Block, Span = new Span(p, p) };
        var names = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var key = KeyAt(line, p) ?? throw new YamlError(p, "A mapping key was expected here.");
            Leaves.Add(key.Span);
            var q = SkipSpaces(line, key.Colon + 1);
            TreeValue member;
            if (AtLineEnd(line, q))
            {
                _line = line + 1;
                member = ParseBlockNode(column, true) ?? Empty(key.Colon + 1);
            }
            else
            {
                _line = line;
                member = ParseInline(line, q, column);
            }
            var entry = new TreeEntry { Name = key.Name, KeySpan = key.Span, Value = member, Own = key.Span with { End = End(member, key.Colon + 1) }, Indent = column };
            if (key.Name == "<<" && member.Merged.Count > 0) value.Merged.AddRange(member.Merged);
            if (names.Add(key.Name)) value.Entries.Add(entry);
            else Duplicates.Add((key.Name, key.Span));
            var next = NextSignificant(_line);
            if (next >= _end) break;
            var np = FirstNonSpace(next);
            var nextColumn = Column(next, np);
            if (nextColumn > column) throw new YamlError(np, "This line is indented deeper than the mapping it is in allows.");
            if (nextColumn < column || KeyAt(next, np) is null) break;
            line = next;
            p = np;
            _line = next;
        }
        value.Span = new Span(value.Entries.Count > 0 ? value.Entries[0].Own.Start : p, value.Entries.Count > 0 ? value.Entries[^1].Own.End : p);
        return value;
    }

    private static int End(TreeValue value, int fallback) => value.Style == ValueStyle.Empty ? fallback : value.Span.End;

    private sealed record Key(string Name, Span Span, int Colon);

    /// <summary>A mapping key at <paramref name="p"/>: plain, or quoted on one line, followed by <c>:</c> and a space or the line's end.</summary>
    private Key? KeyAt(int line, int p)
    {
        var end = ContentEnd(line);
        var b = _bytes[p];
        if (b is (byte)'"' or (byte)'\'')
        {
            var k = p + 1;
            while (k < end)
            {
                if (_bytes[k] == (byte)'\\' && b == (byte)'"') { k += 2; continue; }
                if (_bytes[k] == b)
                {
                    if (b == (byte)'\'' && k + 1 < end && _bytes[k + 1] == (byte)'\'') { k += 2; continue; }
                    break;
                }
                k++;
            }
            if (k >= end) return null;
            var colon = SkipSpaces(line, k + 1);
            if (colon >= end || _bytes[colon] != (byte)':' || !(colon + 1 == end || _bytes[colon + 1] is (byte)' ' or (byte)'\t')) return null;
            var inner = text.Text(p + 1, k);
            var name = b == (byte)'"' ? YamlScalars.DecodeDouble(inner) : YamlScalars.DecodeSingle(inner);
            return new Key(name, new Span(p, k + 1), colon);
        }
        if (b is (byte)'[' or (byte)'{' or (byte)'#' or (byte)'&' or (byte)'*' or (byte)'!' or (byte)'|' or (byte)'>' or (byte)'%' or (byte)'@' or (byte)'`') return null;
        if (b == (byte)'?' && (p + 1 == end || _bytes[p + 1] is (byte)' ' or (byte)'\t'))
        {
            throw new YamlError(p, "Explicit keys ('? ') are not read by this host.");
        }
        if (IsDash(line, p)) return null;
        for (var k = p; k < end; k++)
        {
            var c = _bytes[k];
            if (c == (byte)'#' && k > p && _bytes[k - 1] is (byte)' ' or (byte)'\t') return null;
            if (c != (byte)':' || !(k + 1 == end || _bytes[k + 1] is (byte)' ' or (byte)'\t')) continue;
            var keyEnd = k;
            while (keyEnd > p && _bytes[keyEnd - 1] is (byte)' ' or (byte)'\t') keyEnd--;
            if (keyEnd == p) return null;
            return new Key(text.Text(p, keyEnd), new Span(p, keyEnd), k);
        }
        return null;
    }

    // ---- values ----

    private TreeValue ParseInline(int line, int q, int parentIndent)
    {
        string? anchor = null;
        while (_bytes[q] is (byte)'&' or (byte)'!')
        {
            var start = q;
            while (q < ContentEnd(line) && _bytes[q] is not ((byte)' ' or (byte)'\t')) q++;
            if (_bytes[start] == (byte)'&') anchor = text.Text(start + 1, q);
            q = SkipSpaces(line, q);
            if (AtLineEnd(line, q))
            {
                _line = line + 1;
                var block = ParseBlockNode(parentIndent, true) ?? Empty(q);
                if (anchor is not null) _anchors[anchor] = block;
                return block;
            }
        }
        var value = _bytes[q] switch
        {
            (byte)'*' => ParseAlias(line, q),
            (byte)'"' or (byte)'\'' => ParseQuoted(line, q),
            (byte)'|' or (byte)'>' => ParseBlockScalar(line, q, parentIndent),
            (byte)'[' or (byte)'{' => ParseFlow(line, q),
            _ => ParsePlain(line, q, parentIndent),
        };
        if (anchor is not null) _anchors[anchor] = value;
        return value;
    }

    private void ExpectLineEnd(int line, int p)
    {
        if (!AtLineEnd(line, p)) throw new YamlError(p, "Unexpected content after a value.");
    }

    private TreeValue ParseAlias(int line, int q)
    {
        var p = q + 1;
        while (p < ContentEnd(line) && _bytes[p] is not ((byte)' ' or (byte)'\t' or (byte)',' or (byte)']' or (byte)'}')) p++;
        var name = text.Text(q + 1, p);
        if (!_anchors.TryGetValue(name, out var target)) throw new YamlError(q, $"The alias *{name} names no anchor.");
        ExpectLineEnd(line, p);
        _line = line + 1;
        var span = new Span(q, p);
        Leaves.Add(span);
        var value = new TreeValue
        {
            Kind = ValueKind.Scalar,
            Style = ValueStyle.Plain,
            Span = span,
            Text = target.Text,
            Typed = target.Typed,
            Flow = target.Kind == ValueKind.Scalar ? target.Flow : TreeFamily.Cel(target),
            ViaAlias = true,
        };
        if (target.Kind == ValueKind.Mapping) value.Merged.Add(target);
        return value;
    }

    private TreeValue ParseQuoted(int line, int q)
    {
        var quote = _bytes[q];
        var p = q + 1;
        var current = line;
        while (true)
        {
            if (p >= ContentEnd(current))
            {
                current++;
                if (current >= _end) throw new YamlError(q, "A quoted scalar is never closed.");
                p = LineStart(current);
                continue;
            }
            var b = _bytes[p];
            if (quote == (byte)'"' && b == (byte)'\\') { p += 2; continue; }
            if (b == quote)
            {
                if (quote == (byte)'\'' && p + 1 < ContentEnd(current) && _bytes[p + 1] == (byte)'\'') { p += 2; continue; }
                break;
            }
            p++;
        }
        var span = new Span(q, p + 1);
        ExpectLineEnd(current, p + 1);
        _line = current + 1;
        Leaves.Add(span);
        var inner = text.Text(q + 1, p);
        var decoded = quote == (byte)'"' ? YamlScalars.DecodeDouble(inner) : YamlScalars.DecodeSingle(inner);
        return new TreeValue { Kind = ValueKind.Scalar, Style = quote == (byte)'"' ? ValueStyle.Double : ValueStyle.Single, Span = span, Text = decoded, Typed = decoded };
    }

    private TreeValue ParsePlain(int line, int q, int parentIndent)
    {
        var end = PlainEnd(line, q);
        var parts = new List<string> { text.Text(q, end) };
        var last = end;
        var current = line;
        var blank = 0;
        for (var next = line + 1; next < _end; next++)
        {
            var p = FirstNonSpace(next);
            if (p >= ContentEnd(next))
            {
                blank++;
                continue;
            }
            if (_bytes[p] == (byte)'#' || Column(next, p) <= parentIndent) break;
            if (KeyAt(next, p) is not null || IsDash(next, p) && Column(next, p) <= parentIndent + 1) break;
            var partEnd = PlainEnd(next, p);
            parts.Add(blank > 0 ? new string('\n', blank) + text.Text(p, partEnd) : " " + text.Text(p, partEnd));
            blank = 0;
            last = partEnd;
            current = next;
        }
        _line = current + 1;
        var span = new Span(q, last);
        Leaves.Add(span);
        var plain = string.Concat(parts);
        return new TreeValue { Kind = ValueKind.Scalar, Style = ValueStyle.Plain, Span = span, Text = plain, Typed = YamlScalars.Typed(plain) };
    }

    private int PlainEnd(int line, int q)
    {
        var end = ContentEnd(line);
        var p = q;
        while (p < end)
        {
            if (_bytes[p] == (byte)'#' && p > q && _bytes[p - 1] is (byte)' ' or (byte)'\t') break;
            p++;
        }
        while (p > q && _bytes[p - 1] is (byte)' ' or (byte)'\t') p--;
        return p;
    }

    private TreeValue ParseBlockScalar(int line, int q, int parentIndent)
    {
        var literal = _bytes[q] == (byte)'|';
        var p = q + 1;
        var chomp = 'c';
        var explicitIndent = 0;
        while (p < ContentEnd(line) && _bytes[p] is not ((byte)' ' or (byte)'\t'))
        {
            var c = (char)_bytes[p];
            if (c is '+' or '-') chomp = c;
            else if (char.IsAsciiDigit(c)) explicitIndent = c - '0';
            else throw new YamlError(p, "A block scalar's header holds an unknown indicator.");
            p++;
        }
        ExpectLineEnd(line, p);
        var headerEnd = p;
        var indent = explicitIndent > 0 ? Math.Max(parentIndent, 0) + explicitIndent : -1;
        var contentLines = new List<int>();
        var trailingBlank = 0;
        var lastContent = -1;
        for (var next = line + 1; next < _end; next++)
        {
            var first = FirstNonSpace(next);
            var isBlank = first >= ContentEnd(next);
            var column = Column(next, first);
            if (isBlank)
            {
                contentLines.Add(next);
                trailingBlank++;
                continue;
            }
            if (indent < 0)
            {
                if (column <= parentIndent) break;
                indent = column;
            }
            if (column < indent) break;
            contentLines.Add(next);
            trailingBlank = 0;
            lastContent = next;
        }
        if (lastContent < 0)
        {
            _line = line + 1;
            var empty = new Span(q, headerEnd);
            Leaves.Add(empty);
            return new TreeValue { Kind = ValueKind.Scalar, Style = literal ? ValueStyle.Literal : ValueStyle.Folded, Span = empty, Text = "", Typed = "" };
        }
        var used = contentLines.Where(l => l <= lastContent).ToList();
        var texts = used.Select(l =>
        {
            var start = Math.Min(LineStart(l) + indent, ContentEnd(l));
            return text.Text(start, ContentEnd(l));
        }).ToList();
        var body = literal ? string.Join('\n', texts) : FoldBlock(texts);
        body = chomp switch
        {
            '-' => body,
            '+' => body + "\n" + new string('\n', trailingBlank),
            _ => body + "\n",
        };
        _line = lastContent + 1;
        var span = new Span(q, ContentEnd(lastContent));
        Leaves.Add(span);
        return new TreeValue { Kind = ValueKind.Scalar, Style = literal ? ValueStyle.Literal : ValueStyle.Folded, Span = span, Text = body, Typed = body };
    }

    private static string FoldBlock(List<string> lines)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (i > 0)
            {
                var previous = lines[i - 1];
                var moreIndented = line.StartsWith(' ') || previous.StartsWith(' ');
                builder.Append(line.Length == 0 || previous.Length == 0 || moreIndented ? "\n" : " ");
            }
            builder.Append(line);
        }
        return builder.ToString();
    }

    private TreeValue ParseFlow(int line, int q)
    {
        var depth = 0;
        var current = line;
        var p = q;
        while (true)
        {
            if (p >= ContentEnd(current))
            {
                current++;
                if (current >= _end) throw new YamlError(q, "A flow collection is never closed.");
                p = LineStart(current);
                continue;
            }
            var b = _bytes[p];
            if (b is (byte)'"' or (byte)'\'')
            {
                var quote = b;
                p++;
                while (true)
                {
                    if (p >= ContentEnd(current))
                    {
                        current++;
                        if (current >= _end) throw new YamlError(q, "A quoted scalar in a flow collection is never closed.");
                        p = LineStart(current);
                        continue;
                    }
                    if (quote == (byte)'"' && _bytes[p] == (byte)'\\') { p += 2; continue; }
                    if (_bytes[p] == quote)
                    {
                        if (quote == (byte)'\'' && p + 1 < ContentEnd(current) && _bytes[p + 1] == (byte)'\'') { p += 2; continue; }
                        break;
                    }
                    p++;
                }
                p++;
                continue;
            }
            if (b == (byte)'#' && p > q && _bytes[p - 1] is (byte)' ' or (byte)'\t')
            {
                p = ContentEnd(current);
                continue;
            }
            if (b is (byte)'[' or (byte)'{') depth++;
            if (b is (byte)']' or (byte)'}')
            {
                depth--;
                if (depth == 0)
                {
                    p++;
                    break;
                }
            }
            p++;
        }
        ExpectLineEnd(current, p);
        _line = current + 1;
        var span = new Span(q, p);
        Leaves.Add(span);
        var raw = text.Text(span);
        var parser = new FlowReader(raw);
        var flow = parser.Read();
        return new TreeValue
        {
            Kind = ValueKind.Scalar,
            Style = _bytes[q] == (byte)'[' ? ValueStyle.FlowSequence : ValueStyle.FlowMapping,
            Span = span,
            Text = raw,
            Flow = flow,
        };
    }
}
