using System.Globalization;
using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Rules;
using EtAlii.Adp.Specification.Fbl.Text;

namespace EtAlii.Adp.Specification.Fbl.Json;

/// <summary>
/// The <c>json</c> family (FBL §4.4): an RFC 8259 text read byte for byte, members and items with
/// their own spans and the separator after each, comments refused, duplicate names reported.
/// </summary>
internal sealed class JsonFamily(BodyText text, FblBinding binding, FblOptions options) : TreeFamily(text, binding, options)
{
    private readonly List<Span> _leaves = [];
    private int _position;

    public override string FamilyName => "json";

    public override IReadOnlyList<Span> Leaves => _leaves;

    public override bool IsTrivia(Span gap)
    {
        for (var i = gap.Start; i < gap.End; i++)
        {
            if (Text.Bytes[i] is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or (byte)'{' or (byte)'}' or (byte)'[' or (byte)']' or (byte)',' or (byte)':')) return false;
        }
        return true;
    }

    private sealed class SyntaxError(int offset, string message) : Exception(message)
    {
        public int Offset { get; } = offset;
    }

    public override void Parse()
    {
        _position = Text.BomLength;
        try
        {
            SkipWhitespace();
            var value = ParseValue();
            SkipWhitespace();
            if (_position < Text.Length) throw new SyntaxError(_position, "The body has more than one JSON value.");
            Root = new TreeEntry { IsRoot = true, Own = value.Span, Value = value, Indent = Column(value.Span.Start) };
            Index(Root);
            foreach (var entry in AllEntries.OfType<TreeEntry>().Where(e => !e.IsRoot)) entry.LineSpan = LineSpanOf(entry);
        }
        catch (SyntaxError error)
        {
            Unreadable = (error.Offset, error.Message);
        }
    }

    private int Column(int offset)
    {
        var line = Text.Lines[Text.LineIndexAt(offset)];
        return offset - Math.Max(line.Start, Text.LineIndexAt(offset) == 0 ? Text.BomLength : 0);
    }

    private byte Current => _position < Text.Length ? Text.Bytes[_position] : (byte)0;

    private void SkipWhitespace()
    {
        while (_position < Text.Length)
        {
            var b = Text.Bytes[_position];
            if (b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') _position++;
            else if (b == (byte)'/') throw new SyntaxError(_position, "A JSON body cannot hold comments.");
            else break;
        }
    }

    private void Expect(byte expected)
    {
        if (Current != expected) throw new SyntaxError(_position, $"'{(char)expected}' was expected here.");
        _position++;
    }

    private TreeValue ParseValue()
    {
        if (_position >= Text.Length) throw new SyntaxError(_position, "The body ends where a value was expected.");
        return Current switch
        {
            (byte)'{' => ParseObject(),
            (byte)'[' => ParseArray(),
            (byte)'"' => ParseStringValue(),
            _ => ParseLiteral(),
        };
    }

    private TreeValue ParseObject()
    {
        var start = _position;
        _position++;
        var entries = new List<TreeEntry>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        SkipWhitespace();
        if (Current == (byte)'}')
        {
            _position++;
            return Container(ValueKind.Mapping, start, entries);
        }
        while (true)
        {
            SkipWhitespace();
            if (Current != (byte)'"') throw new SyntaxError(_position, "A member name in quotes was expected here.");
            (string name, Span keySpan) = ParseString();
            SkipWhitespace();
            Expect((byte)':');
            SkipWhitespace();
            var value = ParseValue();
            var member = new TreeEntry { Name = name, KeySpan = keySpan, Value = value, Own = new Span(keySpan.Start, value.Span.End), Indent = Column(keySpan.Start) };
            if (names.Add(name)) entries.Add(member);
            else Report(FindingCodes.DuplicateKey, FindingSeverity.Warning, $"The member \"{name}\" appears again in this object; only the first is read.", keySpan);
            SkipWhitespace();
            if (Current == (byte)',')
            {
                member.Separator = _position;
                _position++;
                continue;
            }
            Expect((byte)'}');
            break;
        }
        return Container(ValueKind.Mapping, start, entries);
    }

    private TreeValue ParseArray()
    {
        var start = _position;
        _position++;
        var entries = new List<TreeEntry>();
        SkipWhitespace();
        if (Current == (byte)']')
        {
            _position++;
            return Container(ValueKind.Sequence, start, entries);
        }
        while (true)
        {
            SkipWhitespace();
            var value = ParseValue();
            var item = new TreeEntry { Value = value, Own = value.Span, Indent = Column(value.Span.Start) };
            entries.Add(item);
            SkipWhitespace();
            if (Current == (byte)',')
            {
                item.Separator = _position;
                _position++;
                continue;
            }
            Expect((byte)']');
            break;
        }
        return Container(ValueKind.Sequence, start, entries);
    }

    private TreeValue Container(ValueKind kind, int start, List<TreeEntry> entries)
    {
        var value = new TreeValue { Kind = kind, Style = ValueStyle.Block, Span = new Span(start, _position) };
        value.Entries.AddRange(entries);
        return value;
    }

    private TreeValue ParseStringValue()
    {
        (string text, Span span) = ParseString();
        return new TreeValue { Kind = ValueKind.Scalar, Style = ValueStyle.JsonString, Span = span, Text = text, Typed = text };
    }

    private (string Text, Span Span) ParseString()
    {
        var start = _position;
        _position++;
        var builder = new StringBuilder();
        var runStart = _position;
        while (true)
        {
            if (_position >= Text.Length) throw new SyntaxError(start, "A string is never closed.");
            var b = Text.Bytes[_position];
            if (b == (byte)'"') break;
            if (b < 0x20) throw new SyntaxError(_position, "A string holds a control character that is not escaped.");
            if (b != (byte)'\\')
            {
                _position++;
                continue;
            }
            builder.Append(Text.Text(runStart, _position));
            _position++;
            var escape = Current;
            _position++;
            switch (escape)
            {
                case (byte)'"': builder.Append('"'); break;
                case (byte)'\\': builder.Append('\\'); break;
                case (byte)'/': builder.Append('/'); break;
                case (byte)'b': builder.Append('\b'); break;
                case (byte)'f': builder.Append('\f'); break;
                case (byte)'n': builder.Append('\n'); break;
                case (byte)'r': builder.Append('\r'); break;
                case (byte)'t': builder.Append('\t'); break;
                case (byte)'u':
                    if (_position + 4 > Text.Length || !int.TryParse(Text.Text(_position, _position + 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                    {
                        throw new SyntaxError(_position, "A \\u escape needs four hexadecimal digits.");
                    }
                    builder.Append((char)code);
                    _position += 4;
                    break;
                default:
                    throw new SyntaxError(_position - 1, "This escape is not one JSON knows.");
            }
            runStart = _position;
        }
        builder.Append(Text.Text(runStart, _position));
        _position++;
        var span = new Span(start, _position);
        _leaves.Add(span);
        return (builder.ToString(), span);
    }

    private TreeValue ParseLiteral()
    {
        var start = _position;
        while (_position < Text.Length && Text.Bytes[_position] is (>= (byte)'a' and <= (byte)'z') or (>= (byte)'0' and <= (byte)'9') or (byte)'-' or (byte)'+' or (byte)'.' or (byte)'E') _position++;
        var raw = Text.Text(start, _position);
        object? typed = raw switch
        {
            "true" => true,
            "false" => false,
            "null" => null,
            _ when long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l) => l,
            _ when double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && raw.Length > 0 && raw[0] != '+' => d,
            _ => throw new SyntaxError(start, raw.Length == 0 ? "A value was expected here." : $"'{raw}' is not a JSON value."),
        };
        var span = new Span(start, _position);
        _leaves.Add(span);
        return new TreeValue { Kind = ValueKind.Scalar, Style = ValueStyle.JsonOther, Span = span, Text = raw, Typed = typed };
    }

    /// <summary>
    /// An entry starts its line when only whitespace precedes it, and ends it when only its
    /// separator and whitespace follow; its line span then takes the separator with it.
    /// </summary>
    private Span? LineSpanOf(TreeEntry entry)
    {
        if (!OnlyWhitespaceBefore(entry.Own.Start)) return null;
        var i = entry.Own.End;
        var line = Text.Lines[Text.LineIndexAt(Math.Max(entry.Own.Start, i - 1))];
        while (i < line.ContentEnd && Text.Bytes[i] is (byte)' ' or (byte)'\t') i++;
        if (i < line.ContentEnd && Text.Bytes[i] == (byte)',') i++;
        while (i < line.ContentEnd && Text.Bytes[i] is (byte)' ' or (byte)'\t') i++;
        if (i != line.ContentEnd) return null;
        return new Span(Text.Lines[Text.LineIndexAt(entry.Own.Start)].Start, line.End);
    }

    // ---- writing ----

    public override string Format(SlotRead read, AttributeBinding? binding, object? value)
    {
        var wire = binding is null ? null : NewText.Wire(binding, value, read.Wire);
        return wire is not null ? Quote(wire) : Write(value, binding);
    }

    public static string Write(object? value, AttributeBinding? binding) => value switch
    {
        null => "null",
        string s => Quote(s),
        bool b => b ? "true" : "false",
        _ when NewText.TryNumber(value, out var n) => NewText.Number(n, binding?.Decimals),
        IEnumerable<object?> list => "[" + string.Join(", ", list.Select(v => Write(v, binding))) + "]",
        _ => Quote(Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""),
    };

    /// <summary>A JSON string with the escapes RFC 8785 uses (FBL §6.3).</summary>
    public static string Quote(string value) => "\"" + Escape(value) + "\"";

    public static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (c < 0x20) builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else builder.Append(c);
                    break;
            }
        }
        return builder.ToString();
    }

    public override void Write(Plan plan, ReadElement element, IReadOnlyList<SlotChange> changes)
    {
        foreach (var change in changes)
        {
            var read = change.Read;
            if (change is { IsEmpty: true, Binding.Empty: "remove" })
            {
                if (read.Node is TreeEntry member) plan.Add(SpliceOperation.RemoveKey, RemovalSpan(member), "");
                continue;
            }
            if (read is { Present: true, Span: { } span })
            {
                var written = Format(read, change.Binding, change.Value);
                if (written != Text.Text(span)) plan.Add(SpliceOperation.ReplaceValue, span, written);
                continue;
            }
            var mapping = Mapping((TreeEntry)element.Entry, change.Binding.Child);
            if (mapping is null || change.Binding.Key is not { } key)
            {
                Plan.Refuse($"The {Binding.Name} file has no object to write \"{change.Binding.Key}\" in.");
                return;
            }
            (int offset, string text) = NewMember(mapping, $"{Quote(key)}: {Format(read, change.Binding, change.Value)}");
            plan.Add(SpliceOperation.InsertKey, offset, offset, text);
        }
    }

    /// <summary>A new member or item after a container's last entry, with the previous entry's separator (FBL §6.3).</summary>
    private (int Offset, string Text) NewMember(TreeValue container, string text)
    {
        if (container.Entries.Count == 0) return (container.Span.Start + 1, text);
        var last = container.Entries[^1];
        if (last.LineSpan is not null)
        {
            return (last.Own.End, "," + NewlineAt(last.Own.End) + Indentation(last.Indent) + text);
        }
        return (last.Own.End, ", " + text);
    }

    public override void Insert(Plan plan, InsertRequest request)
    {
        var insert = request.Rule.Insert!;
        var container = insert.Container is null ? null : Container(insert.Container, request.Parent?.Entry, CapturesOf(plan));
        if (container is null || container.Value.Kind == ValueKind.Scalar)
        {
            Plan.Refuse($"The {Binding.Name} file has no {insert.Container ?? "container"} to add the {request.Rule.Type} to.");
            return;
        }
        string text;
        if (insert.Emit is { } emit)
        {
            text = NewText.Render(emit, name => name switch
            {
                "id" => request.Id is null ? null : Escape(request.Id),
                "source" => request.Source is null ? null : Escape(request.Source.Key),
                "target" => request.Target is null ? null : Escape(request.Target.Key),
                _ => request.Values.TryGetValue(name, out var v) && v is not null ? Escape(NewText.Plain(v, request.Rule.Attribute(name))) : null,
            });
        }
        else
        {
            var members = new List<string>();
            foreach (var key in insert.Keys)
            {
                if (WireValue(request, key) is { } value) members.Add($"{Quote(key)}: {value}");
            }
            text = "{ " + string.Join(", ", members) + " }";
        }
        if (insert.Place == "start" && container.Value.Entries.Count > 0)
        {
            var first = container.Value.Entries[0];
            var newline = first.LineSpan is not null;
            plan.Add(SpliceOperation.InsertEntry, first.Own.Start, first.Own.Start, text + (newline ? "," + NewlineAt(first.Own.Start) + Indentation(first.Indent) : ", "));
            return;
        }
        (int offset, string inserted) = NewMember(container.Value, text);
        plan.Add(SpliceOperation.InsertEntry, offset, offset, inserted);
    }

    private static string? WireValue(InsertRequest request, string key)
    {
        if (request.Rule.Id?.From?.Key == key && request.Id is { } id) return Quote(id);
        if (request.Rule.Source?.Key == key && request.Source is { } source) return Quote(source.Key);
        if (request.Rule.Target?.Key == key && request.Target is { } target) return Quote(target.Key);
        foreach ((string name, AttributeBinding binding) in request.Rule.Attributes)
        {
            if (binding.Key == key && binding.Child is null && request.Values.TryGetValue(name, out var value) && value is not null) return Write(value, binding);
        }
        return null;
    }

    /// <summary>
    /// What removing an entry takes (FBL §6.2): its line span when it has one and is not the last of
    /// several, else its own span with one separator: the one after it, or, for the last entry, the
    /// one before it and the whitespace in between.
    /// </summary>
    private Span RemovalSpan(TreeEntry entry)
    {
        var siblings = ((TreeEntry)entry.Parent!).Value.Entries;
        var index = siblings.IndexOf(entry);
        var isLast = index == siblings.Count - 1;
        if (isLast && index > 0)
        {
            var previous = siblings[index - 1];
            return new Span(previous.Separator, entry.Own.End);
        }
        if (entry.LineSpan is { } line) return line;
        if (!isLast) return new Span(entry.Own.Start, siblings[index + 1].Own.Start);
        return entry.Own;
    }

    public override void Remove(Plan plan, ReadElement element, IReadOnlySet<ReadElement> removed)
    {
        var entry = (TreeEntry)element.Entry;
        if (entry.IsRoot)
        {
            Plan.Refuse($"The whole {Binding.Name} file cannot be removed.");
            return;
        }
        plan.Add(SpliceOperation.RemoveEntry, RemovalSpan(entry), "");
    }
}
