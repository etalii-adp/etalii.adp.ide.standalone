using System.Globalization;
using System.Text;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Fbl.Rules;
using EtAlii.Adp.Specification.Fbl.Text;
using YamlDotNet.Core;
using YamlDotNet.Core.Tokens;

namespace EtAlii.Adp.Specification.Fbl.Yaml;

/// <summary>
/// The lossless reading of a YAML body's first document (FBL §4.3), in one pass: YamlDotNet's parser
/// judges the whole stream while the tokens its scanner hands it are kept, and the tree is built from
/// those tokens, every node with its span as written. Scalar values are YamlDotNet's. The tokens' marks
/// count characters, so they are mapped to byte offsets here.
/// </summary>
internal sealed class YamlParser(BodyText text)
{
    private static readonly FlowValue NoFlowValue = new(null, "", "plain", Empty(0));

    private readonly byte[] _bytes = text.Bytes;
    private readonly List<Token> _tokens = [];
    private readonly Dictionary<string, TreeValue> _anchors = new(StringComparer.Ordinal);
    private int[] _byteAt = [];
    private int _next;

    public List<Span> Leaves { get; } = [];

    /// <summary>Duplicate keys found: the key spans of the second and later occurrences.</summary>
    public List<(string Name, Span Span)> Duplicates { get; } = [];

    public sealed class YamlError(int offset, string message) : Exception(message)
    {
        public int Offset { get; } = offset;
    }

    /// <summary>The first document's root; a syntax error anywhere in the stream makes the body unreadable (FBL §4.3, §7.5).</summary>
    public TreeValue ParseDocument()
    {
        Scan();
        while (Peek is StreamStart or VersionDirective or TagDirective) _next++;
        if (Peek is DocumentStart) _next++;
        var root = Peek is DocumentEnd or DocumentStart or StreamEnd ? Empty(text.BomLength) : Node(text.BomLength, false);
        // Everything after the first document is unbound (FBL §4.3).
        var documentEnd = Peek is DocumentEnd or DocumentStart ? text.Lines[text.LineIndexAt(ByteOf(Peek.Start))].Start : text.Length;
        if (documentEnd < text.Length) Leaves.Add(new Span(documentEnd, text.Length));
        return root;
    }

    private void Scan()
    {
        var content = text.Text(text.BomLength, text.Length);
        _byteAt = ByteOffsets(content);
        try
        {
            var parser = new Parser(new RecordingScanner(new Scanner(new StringReader(content)), _tokens));
            while (parser.MoveNext())
            {
            }
        }
        catch (YamlException e)
        {
            var index = (int)Math.Clamp(e.Start.Index, 0, content.Length);
            throw new YamlError(text.BomLength + Encoding.UTF8.GetByteCount(content.AsSpan(0, index)), $"The body is not well-formed YAML: {e.Message}");
        }
        catch (InvalidOperationException)
        {
            // YamlDotNet's scanner throws this rather than a YamlException on some bodies it cannot
            // finish, such as a flow sequence left open before the next key. It names no place, so the
            // body is reported unreadable at its end.
            throw new YamlError(text.Length, "The body is not well-formed YAML: the reader could not finish it.");
        }
    }

    // ---- positions ----

    /// <summary>The byte offset of every character index of <paramref name="content"/>, a surrogate pair counted as one code point.</summary>
    private static int[] ByteOffsets(string content)
    {
        var map = new int[content.Length + 1];
        var b = 0;
        for (var i = 0; i < content.Length; i++)
        {
            map[i] = b;
            if (char.IsHighSurrogate(content[i]) && i + 1 < content.Length && char.IsLowSurrogate(content[i + 1]))
            {
                map[++i] = b;
                b += 4;
            }
            else b += content[i] < 0x80 ? 1 : content[i] < 0x800 ? 2 : 3;
        }
        map[content.Length] = b;
        return map;
    }

    private int ByteOf(Mark mark) => text.BomLength + _byteAt[(int)Math.Clamp(mark.Index, 0, _byteAt.Length - 1)];

    private Span SpanOf(Token token) => new(ByteOf(token.Start), ByteOf(token.End));

    private Token Peek => _tokens[Math.Min(_next, _tokens.Count - 1)];

    private int Column(int offset)
    {
        var line = text.LineIndexAt(offset);
        return offset - (line == 0 ? Math.Max(text.Lines[0].Start, text.BomLength) : text.Lines[line].Start);
    }

    private int SkipSpaces(int p)
    {
        while (p < text.Length && _bytes[p] is (byte)' ' or (byte)'\t') p++;
        return p;
    }

    private static TreeValue Empty(int offset) => new() { Kind = ValueKind.Scalar, Style = ValueStyle.Empty, Span = new Span(offset, offset), Typed = null };

    private static int End(TreeValue value, int fallback) => value.Style == ValueStyle.Empty ? fallback : value.Span.End;

    // ---- block structure ----

    /// <summary>A node with its anchor and tag; an empty one sits at <paramref name="emptyAt"/>, or after its anchor or tag.</summary>
    private TreeValue Node(int emptyAt, bool indentless)
    {
        string? anchor = null;
        var properties = -1;
        while (Peek is Anchor or Tag)
        {
            if (Peek is Anchor a) anchor = a.Value.Value;
            properties = ByteOf(Peek.End);
            _next++;
        }
        var value = Peek switch
        {
            BlockMappingStart => BlockMapping(),
            BlockSequenceStart => BlockSequence(),
            BlockEntry when indentless => Sequence(),
            FlowSequenceStart or FlowMappingStart => Flow(),
            Scalar scalar => ScalarValue(scalar),
            AnchorAlias alias => Alias(alias),
            _ => Empty(properties >= 0 ? SkipSpaces(properties) : emptyAt),
        };
        if (anchor is not null) _anchors[anchor] = value;
        return value;
    }

    private TreeValue BlockMapping()
    {
        var value = new TreeValue { Kind = ValueKind.Mapping, Style = ValueStyle.Block, Span = new Span(ByteOf(Peek.Start), ByteOf(Peek.Start)) };
        _next++;
        var names = new HashSet<string>(StringComparer.Ordinal);
        var column = -1;
        while (Peek is Key key)
        {
            // An explicit key's indicator is the one key token with a width.
            if (key.End.Index > key.Start.Index) throw new YamlError(ByteOf(key.Start), "Explicit keys ('? ') are not read by this host.");
            _next++;
            var start = ByteOf(key.Start);
            (string name, Span keySpan) = KeyOf(start);
            if (column < 0) column = Column(start);
            var colon = keySpan.End;
            var member = Empty(colon);
            if (Peek is Value indicator)
            {
                colon = ByteOf(indicator.Start);
                _next++;
                member = Node(colon + 1, true);
            }
            var entry = new TreeEntry { Name = name, KeySpan = keySpan, Value = member, Own = new Span(start, End(member, colon + 1)), Indent = column };
            if (name == "<<" && member.Merged.Count > 0) value.Merged.AddRange(member.Merged);
            if (names.Add(name)) value.Entries.Add(entry);
            else Duplicates.Add((name, keySpan));
        }
        if (Peek is BlockEnd) _next++;
        if (value.Entries.Count > 0) value.Span = new Span(value.Entries[0].Own.Start, value.Entries[^1].Own.End);
        return value;
    }

    /// <summary>A block mapping's key: a scalar or an alias, an anchor or a tag allowed; an empty key or a collection is not read.</summary>
    private (string Name, Span Span) KeyOf(int start)
    {
        string? anchor = null;
        while (Peek is Anchor or Tag)
        {
            if (Peek is Anchor a) anchor = a.Value.Value;
            _next++;
        }
        var key = Peek switch
        {
            Scalar written => ScalarValue(written),
            AnchorAlias alias => Alias(alias),
            Value => throw new YamlError(start, "A mapping key is empty."),
            _ => throw new YamlError(ByteOf(Peek.Start), "A mapping key must be a scalar."),
        };
        if (anchor is not null) _anchors[anchor] = key;
        return (key.Text, key.Span);
    }

    private TreeValue BlockSequence()
    {
        _next++;
        var value = Sequence();
        if (Peek is BlockEnd) _next++;
        return value;
    }

    /// <summary>A block sequence's entries; one at its parent key's indentation has no start or end token, and ends at the first token that is not an entry.</summary>
    private TreeValue Sequence()
    {
        var value = new TreeValue { Kind = ValueKind.Sequence, Style = ValueStyle.Block, Span = new Span(ByteOf(Peek.Start), ByteOf(Peek.Start)) };
        var column = Column(ByteOf(Peek.Start));
        while (Peek is BlockEntry entry)
        {
            var dash = ByteOf(entry.Start);
            _next++;
            var item = Node(dash + 1, false);
            value.Entries.Add(new TreeEntry { Value = item, Own = new Span(dash, End(item, dash + 1)), Indent = column });
        }
        value.Span = new Span(value.Entries[0].Own.Start, value.Entries[^1].Own.End);
        return value;
    }

    // ---- values ----

    private TreeValue ScalarValue(Scalar scalar)
    {
        _next++;
        var span = SpanOf(scalar);
        switch (scalar.Style)
        {
            case ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted:
                Leaves.Add(span);
                return new TreeValue { Kind = ValueKind.Scalar, Style = scalar.Style == ScalarStyle.DoubleQuoted ? ValueStyle.Double : ValueStyle.Single, Span = span, Text = scalar.Value, Typed = scalar.Value };
            case ScalarStyle.Literal or ScalarStyle.Folded:
                span = BlockScalarSpan(span);
                Leaves.Add(span);
                return new TreeValue { Kind = ValueKind.Scalar, Style = scalar.Style == ScalarStyle.Literal ? ValueStyle.Literal : ValueStyle.Folded, Span = span, Text = scalar.Value, Typed = scalar.Value };
            default:
                Leaves.Add(span);
                return new TreeValue { Kind = ValueKind.Scalar, Style = ValueStyle.Plain, Span = span, Text = scalar.Value, Typed = YamlScalars.Typed(scalar.Value) };
        }
    }

    /// <summary>
    /// A block scalar's token runs on over the line breaks and blank lines after its content, which a
    /// replacement must leave; its span ends with its last content line, or with its header's
    /// indicators when it has no content.
    /// </summary>
    private Span BlockScalarSpan(Span token)
    {
        var p = token.End;
        while (p > token.Start && _bytes[p - 1] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') p--;
        var header = text.LineIndexAt(token.Start);
        var last = text.LineIndexAt(Math.Max(token.Start, p - 1));
        if (last != header) return token with { End = text.Lines[last].ContentEnd };
        var end = token.Start + 1;
        while (end < text.Length && _bytes[end] is (byte)'+' or (byte)'-' or >= (byte)'0' and <= (byte)'9') end++;
        return token with { End = end };
    }

    /// <summary>An alias reads as its anchor's value, read-only (FBL §4.3); one of a mapping is merged where a <c>&lt;&lt;</c> key names it.</summary>
    private TreeValue Alias(AnchorAlias alias)
    {
        _next++;
        var name = alias.Value.Value;
        var span = SpanOf(alias);
        if (!_anchors.TryGetValue(name, out var target)) throw new YamlError(span.Start, $"The alias *{name} names no anchor.");
        Leaves.Add(span);
        var value = new TreeValue
        {
            Kind = ValueKind.Scalar,
            Style = ValueStyle.Plain,
            Span = span,
            Text = target.Text,
            Typed = target.Typed,
            Flow = target.Kind == ValueKind.Scalar ? target.Flow : TreeFamily.Cel(target),
            FlowText = target.Kind == ValueKind.Scalar ? target.FlowText : null,
            FlowStyle = target.Kind == ValueKind.Scalar ? target.FlowStyle : null,
            ViaAlias = true,
        };
        if (target.Kind == ValueKind.Mapping) value.Merged.Add(target);
        return value;
    }

    // ---- flow collections ----

    /// <summary>
    /// A flow collection is one value: its members are readable, while its only writable span is the
    /// whole collection (FBL §4.3). Its members are read three ways in one walk: each member's typed
    /// value, its written text (FBL §4.1.5) and the name of the style it is written in (FBL §2.4).
    /// The items of a flow sequence are entries for the rules' selectors too (FBL 0.10 §4.3), each with
    /// the span of a json item, its separator the <c>,</c> after it, and an item that is a mapping with
    /// its members as entries, so that a slot of an item can be read.
    /// </summary>
    private TreeValue Flow()
    {
        var start = ByteOf(Peek.Start);
        var flow = FlowNode();
        // A flow collection's end token is the closing bracket, marked where it starts.
        var span = new Span(start, ByteOf(_tokens[_next - 1].Start) + 1);
        Leaves.Add(span);
        var value = new TreeValue
        {
            Kind = ValueKind.Scalar,
            Style = _bytes[start] == (byte)'[' ? ValueStyle.FlowSequence : ValueStyle.FlowMapping,
            Span = span,
            Text = text.Text(span),
            Flow = flow.Typed,
            FlowText = flow.Text,
            FlowStyle = flow.Style,
        };
        if (value.Style == ValueStyle.FlowSequence) value.Entries.AddRange(flow.Tree.Entries);
        return value;
    }

    private FlowValue FlowNode()
    {
        while (Peek is Anchor or Tag) _next++;
        var start = ByteOf(Peek.Start);
        switch (Peek)
        {
            case FlowSequenceStart:
                _next++;
                var typed = new List<object?>();
                var texts = new List<object?>();
                var styles = new List<object?>();
                var sequence = new TreeValue { Kind = ValueKind.Sequence, Style = ValueStyle.FlowSequence, Span = new Span(start, start) };
                while (Peek is not FlowSequenceEnd)
                {
                    if (Peek is FlowEntry separator)
                    {
                        if (sequence.Entries.Count > 0 && sequence.Entries[^1].Separator < 0) sequence.Entries[^1].Separator = ByteOf(separator.Start);
                        _next++;
                        continue;
                    }
                    var at = ByteOf(Peek.Start);
                    var item = Peek is Key ? FlowPair() : FlowNode();
                    typed.Add(item.Typed);
                    texts.Add(item.Text);
                    styles.Add(item.Style);
                    sequence.Entries.Add(new TreeEntry { Value = item.Tree, Own = item.Tree.Span with { Start = at }, Indent = Column(at), InFlow = true });
                }
                sequence.Span = new Span(start, ByteOf(Peek.Start) + 1);
                _next++;
                return new FlowValue(typed, texts, styles, sequence);
            case FlowMappingStart:
                _next++;
                var typedMap = new CelMap();
                var textMap = new CelMap();
                var styleMap = new CelMap();
                var mapping = new TreeValue { Kind = ValueKind.Mapping, Style = ValueStyle.FlowMapping, Span = new Span(start, start) };
                while (Peek is not FlowMappingEnd)
                {
                    if (Peek is FlowEntry)
                    {
                        _next++;
                        continue;
                    }
                    FlowMember(typedMap, textMap, styleMap, mapping);
                }
                mapping.Span = new Span(start, ByteOf(Peek.Start) + 1);
                _next++;
                return new FlowValue(typedMap, textMap, styleMap, mapping);
            case Scalar scalar:
                _next++;
                var written = SpanOf(scalar);
                return scalar.Style switch
                {
                    ScalarStyle.Plain => new FlowValue(YamlScalars.Typed(scalar.Value), scalar.Value, "plain", FlowScalar(written, ValueStyle.Plain, scalar.Value, YamlScalars.Typed(scalar.Value))),
                    ScalarStyle.DoubleQuoted => new FlowValue(scalar.Value, scalar.Value, "double", FlowScalar(written, ValueStyle.Double, scalar.Value, scalar.Value)),
                    _ => new FlowValue(scalar.Value, scalar.Value, "single", FlowScalar(written, ValueStyle.Single, scalar.Value, scalar.Value)),
                };
            case AnchorAlias alias:
                // Inside a flow collection an alias reads as it is written.
                _next++;
                var name = "*" + alias.Value.Value;
                return new FlowValue(name, name, "plain", FlowScalar(SpanOf(alias), ValueStyle.Plain, name, name));
            default:
                return NoFlowValue with { Tree = Empty(start) };
        }
    }

    /// <summary>A scalar inside a flow collection, as the tree of its item holds it (FBL 0.10 §4.3).</summary>
    private static TreeValue FlowScalar(Span span, ValueStyle style, string written, object? typed) =>
        new() { Kind = ValueKind.Scalar, Style = style, Span = span, Text = written, Typed = typed };

    /// <summary>A flow mapping's member: <c>key: value</c>, a key alone, or <c>? key</c>; the last of a repeated key wins.</summary>
    private void FlowMember(CelMap typed, CelMap texts, CelMap styles, TreeValue mapping)
    {
        if (Peek is Key) _next++;
        var start = ByteOf(Peek.Start);
        var key = FlowNode();
        var last = _tokens[_next - 1];
        // A collection as a key, as in a template's {{ .Release.Name }}, is named by its text as written.
        var written = key.Text as string ?? text.Text(start, last is FlowSequenceEnd or FlowMappingEnd ? ByteOf(last.Start) + 1 : ByteOf(last.End));
        var value = NoFlowValue with { Tree = Empty(key.Tree.Span.End) };
        if (Peek is Value)
        {
            _next++;
            value = FlowNode();
        }
        var name = key.Text is string ? Convert.ToString(key.Typed, CultureInfo.InvariantCulture) ?? "" : written;
        typed[name] = value.Typed;
        texts[written] = value.Text;
        styles[name] = value.Style;
        var member = new TreeEntry { Name = name, KeySpan = key.Tree.Span, Value = value.Tree, Own = new Span(start, Math.Max(key.Tree.Span.End, value.Tree.Span.End)), Indent = Column(start), InFlow = true };
        mapping.Entries.RemoveAll(entry => entry.Name == name);
        mapping.Entries.Add(member);
    }

    /// <summary>A single pair in a flow sequence (<c>[a: b]</c>) is a mapping of one member.</summary>
    private FlowValue FlowPair()
    {
        var typed = new CelMap();
        var texts = new CelMap();
        var styles = new CelMap();
        var start = ByteOf(Peek.Start);
        var mapping = new TreeValue { Kind = ValueKind.Mapping, Style = ValueStyle.FlowMapping, Span = new Span(start, start) };
        FlowMember(typed, texts, styles, mapping);
        mapping.Span = mapping.Entries.Count > 0 ? new Span(start, mapping.Entries[^1].Own.End) : mapping.Span;
        return new FlowValue(typed, texts, styles, mapping);
    }

    /// <summary>
    /// A member of a flow collection, or the collection itself, read three ways: typed, as written, and by style; and
    /// as the tree its items are entries of (FBL 0.10 §4.3).
    /// </summary>
    private sealed record FlowValue(object? Typed, object? Text, object? Style, TreeValue Tree);

    /// <summary>Hands YamlDotNet's parser the scanner's tokens and keeps each one it reads, so the body is scanned once.</summary>
    private sealed class RecordingScanner(Scanner scanner, List<Token> tokens) : IScanner
    {
        public Mark CurrentPosition => scanner.CurrentPosition;

        public Token? Current => scanner.Current;

        public bool MoveNext() => Record(scanner.MoveNext());

        public bool MoveNextWithoutConsuming() => Record(scanner.MoveNextWithoutConsuming());

        public void ConsumeCurrent() => scanner.ConsumeCurrent();

        private bool Record(bool moved)
        {
            if (moved && scanner.Current is { } token && (tokens.Count == 0 || !ReferenceEquals(tokens[^1], token))) tokens.Add(token);
            return moved;
        }
    }
}
