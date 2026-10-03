using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Expressions;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Rules;
using EtAlii.Adp.Specification.Fbl.Text;

namespace EtAlii.Adp.Specification.Fbl.Xml;

/// <summary>An attribute as written (FBL §4.5): its own span from name to closing quote, and its value span between the quotes.</summary>
internal sealed record XmlAttribute(string Name, Span Own, Span Value, string Text);

/// <summary>Character data of an element, with its span and its text with references decoded.</summary>
internal sealed record XmlTextRun(Span Span, string Text);

/// <summary>The text of an element as a slot reads it: where it is written, and whether it is html paragraphs.</summary>
internal sealed record XmlTextNode(XmlElement Element, Span? Span, bool Html);

/// <summary>An element (FBL §4.5): an entry whose own span runs from its start tag's <c>&lt;</c> to its end tag's <c>&gt;</c>.</summary>
internal sealed class XmlElement : Entry
{
    public bool IsRoot { get; init; }

    public Span StartTag { get; set; }

    /// <summary>The end of the element's name in its start tag, where a first attribute is added.</summary>
    public int NameEnd { get; init; }

    /// <summary>The <c>&gt;</c> that ends the start tag, or the <c>/&gt;</c> of a self-closed tag.</summary>
    public Span Close { get; set; }

    public bool SelfClosed { get; set; }

    public Span? EndTag { get; set; }

    public int ContentStart => StartTag.End;

    public int ContentEnd => EndTag?.Start ?? StartTag.End;

    public List<XmlAttribute> Attributes { get; } = [];

    /// <summary>Text runs and child elements in document order.</summary>
    public List<object> Content { get; } = [];

    /// <summary>A reference to an entity other than the five predefined ones: the element is an unreadable entry.</summary>
    public string? Unreadable { get; set; }

    public IEnumerable<XmlElement> Elements => Content.OfType<XmlElement>();

    public XmlAttribute? Attribute(string name) => Attributes.FirstOrDefault(a => a.Name == name);
}

/// <summary>
/// The xml family (FBL §4.5): a lossless reading of an XML 1.0 document over its bytes. The prolog,
/// comments, processing instructions and a document type declaration are unbound content; carriage
/// returns are kept, never normalised; entity declarations are not processed.
/// </summary>
internal sealed partial class XmlFamily(BodyText text, FblBinding binding, FblOptions options) : FamilyReader(text, binding, options)
{
    private readonly List<Entry> _entries = [];
    private readonly List<Span> _leaves = [];
    private readonly Dictionary<int, Span> _commentLines = [];
    private XmlElement? _root;
    private XmlElement? _document;

    public override string FamilyName => "xml";

    public override IReadOnlyList<Entry> Entries => _entries;

    public override IReadOnlyList<Span> Leaves => _leaves;

    public override bool IsTrivia(Span gap) => IsWhitespace(gap);

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    // ---- reading ----

    private sealed class XmlError(int offset, string message) : Exception(message)
    {
        public int Offset { get; } = offset;
    }

    public override void Parse()
    {
        try
        {
            ParseDocument();
        }
        catch (XmlError error)
        {
            Unreadable = (error.Offset, $"The body is not well-formed XML: {error.Message}");
        }
    }

    private void ParseDocument()
    {
        var bytes = Text.Bytes;
        _root = new XmlElement { IsRoot = true, Own = new Span(0, bytes.Length), Indent = 0 };
        var stack = new Stack<XmlElement>();
        var position = Text.BomLength;
        while (position < bytes.Length)
        {
            if (bytes[position] != (byte)'<')
            {
                position = ReadText(position, stack.Count > 0 ? stack.Peek() : null);
                continue;
            }
            if (StartsWith(position, "<?"))
            {
                position = Skip(position, "?>", "processing instruction");
            }
            else if (StartsWith(position, "<!--"))
            {
                var start = position;
                position = Skip(position, "-->", "comment");
                RememberComment(new Span(start, position));
            }
            else if (StartsWith(position, "<![CDATA["))
            {
                if (stack.Count == 0) throw new XmlError(position, "A CDATA section must be inside the document element.");
                var start = position;
                position = Skip(position, "]]>", "CDATA section");
                var content = Text.Text(start + 9, position - 3);
                stack.Peek().Content.Add(new XmlTextRun(new Span(start, position), content));
            }
            else if (StartsWith(position, "<!"))
            {
                if (_document is not null) throw new XmlError(position, "A document type declaration must come before the document element.");
                position = SkipDeclaration(position);
            }
            else if (StartsWith(position, "</"))
            {
                position = ReadEndTag(position, stack);
            }
            else
            {
                position = ReadStartTag(position, stack);
            }
        }
        if (stack.Count > 0) throw new XmlError(bytes.Length, $"The element '{stack.Peek().Name}' is not closed.");
        if (_document is null) throw new XmlError(Text.BomLength, "The body has no document element.");
        foreach (var entry in _entries) entry.LineSpan = LineSpanOf(entry);
    }

    private bool StartsWith(int position, string literal)
    {
        if (position + literal.Length > Text.Length) return false;
        for (var i = 0; i < literal.Length; i++)
        {
            if (Text.Bytes[position + i] != (byte)literal[i]) return false;
        }
        return true;
    }

    private int Find(int position, string literal)
    {
        for (var i = position; i + literal.Length <= Text.Length; i++)
        {
            if (StartsWith(i, literal)) return i;
        }
        return -1;
    }

    private int Skip(int position, string terminator, string what)
    {
        var end = Find(position + 2, terminator);
        if (end < 0) throw new XmlError(position, $"The {what} is not closed.");
        end += terminator.Length;
        _leaves.Add(new Span(position, end));
        return end;
    }

    private int SkipDeclaration(int position)
    {
        var depth = 0;
        byte quote = 0;
        for (var i = position + 2; i < Text.Length; i++)
        {
            var b = Text.Bytes[i];
            if (quote != 0)
            {
                if (b == quote) quote = 0;
                continue;
            }
            switch (b)
            {
                case (byte)'"' or (byte)'\'': quote = b; break;
                case (byte)'[': depth++; break;
                case (byte)']': depth--; break;
                case (byte)'>' when depth == 0:
                    _leaves.Add(new Span(position, i + 1));
                    return i + 1;
            }
        }
        throw new XmlError(position, "The declaration is not closed.");
    }

    private void RememberComment(Span comment)
    {
        var line = Text.LineIndexAt(comment.Start);
        if (Text.LineIndexAt(comment.End - 1) == line && OnlyWhitespaceBefore(comment.Start) && OnlyTriviaAfter(comment.End))
        {
            _commentLines[line] = comment;
        }
    }

    private int ReadText(int position, XmlElement? parent)
    {
        var end = position;
        while (end < Text.Length && Text.Bytes[end] != (byte)'<') end++;
        var span = new Span(position, end);
        if (IsWhitespace(span))
        {
            parent?.Content.Add(new XmlTextRun(span, Text.Text(span)));
            return end;
        }
        if (parent is null) throw new XmlError(position, "Text must be inside the document element.");
        _leaves.Add(span);
        parent.Content.Add(new XmlTextRun(span, Decode(span, parent)));
        return end;
    }

    /// <summary>Decodes the five predefined entities and character references; any other reference makes <paramref name="owner"/> unreadable.</summary>
    private string Decode(Span span, XmlElement? owner)
    {
        var raw = Text.Text(span);
        if (!raw.Contains('&')) return raw;
        var builder = new StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] != '&')
            {
                builder.Append(raw[i]);
                continue;
            }
            var semicolon = raw.IndexOf(';', i + 1);
            if (semicolon < 0) throw new XmlError(span.Start, "An '&' must start a reference ending in ';'.");
            var name = raw[(i + 1)..semicolon];
            switch (name)
            {
                case "amp": builder.Append('&'); break;
                case "lt": builder.Append('<'); break;
                case "gt": builder.Append('>'); break;
                case "quot": builder.Append('"'); break;
                case "apos": builder.Append('\''); break;
                default:
                    if (name.StartsWith('#'))
                    {
                        var hex = name.StartsWith("#x", StringComparison.Ordinal);
                        var digits = hex ? name[2..] : name[1..];
                        if (!int.TryParse(digits, hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None, CultureInfo.InvariantCulture, out var code)
                            || code is < 1 or > 0x10FFFF or (>= 0xD800 and <= 0xDFFF))
                        {
                            throw new XmlError(span.Start, $"'&{name};' is not a valid character reference.");
                        }
                        builder.Append(char.ConvertFromUtf32(code));
                    }
                    else
                    {
                        if (owner is { Unreadable: null }) owner.Unreadable = $"It refers to the entity '&{name};', which is not one of XML's five predefined entities.";
                        builder.Append('&').Append(name).Append(';');
                    }
                    break;
            }
            i = semicolon;
        }
        return builder.ToString();
    }

    private static bool IsNameByte(byte b, bool first) =>
        b >= 0x80 || char.IsAsciiLetter((char)b) || b is (byte)'_' or (byte)':' || (!first && (char.IsAsciiDigit((char)b) || b is (byte)'-' or (byte)'.'));

    private int ReadName(int position)
    {
        var end = position;
        while (end < Text.Length && IsNameByte(Text.Bytes[end], end == position)) end++;
        if (end == position) throw new XmlError(position, "A name was expected here.");
        return end;
    }

    private int SkipSpace(int position)
    {
        while (position < Text.Length && Text.Bytes[position] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') position++;
        return position;
    }

    private int ReadStartTag(int start, Stack<XmlElement> stack)
    {
        var parent = stack.Count > 0 ? stack.Peek() : null;
        if (parent is null && _document is not null) throw new XmlError(start, "The body has a second document element.");
        var nameEnd = ReadName(start + 1);
        var element = new XmlElement
        {
            Name = Text.Text(start + 1, nameEnd),
            NameEnd = nameEnd,
            Own = new Span(start, nameEnd),
            Indent = start - Text.Lines[Text.LineIndexAt(start)].Start,
            Parent = parent ?? _root,
        };
        var position = nameEnd;
        while (true)
        {
            var spaced = SkipSpace(position);
            if (spaced >= Text.Length) throw new XmlError(start, $"The start tag of '{element.Name}' is not closed.");
            if (StartsWith(spaced, "/>"))
            {
                element.SelfClosed = true;
                element.Close = new Span(spaced, spaced + 2);
                position = spaced + 2;
                break;
            }
            if (Text.Bytes[spaced] == (byte)'>')
            {
                element.Close = new Span(spaced, spaced + 1);
                position = spaced + 1;
                break;
            }
            if (spaced == position) throw new XmlError(spaced, "Attributes must be separated by whitespace.");
            var attributeEnd = ReadName(spaced);
            var name = Text.Text(spaced, attributeEnd);
            var equals = SkipSpace(attributeEnd);
            if (equals >= Text.Length || Text.Bytes[equals] != (byte)'=') throw new XmlError(equals, $"The attribute '{name}' has no value.");
            var open = SkipSpace(equals + 1);
            if (open >= Text.Length || Text.Bytes[open] is not ((byte)'"' or (byte)'\'')) throw new XmlError(open, $"The value of '{name}' must be quoted.");
            var quote = Text.Bytes[open];
            var close = open + 1;
            while (close < Text.Length && Text.Bytes[close] != quote)
            {
                if (Text.Bytes[close] == (byte)'<') throw new XmlError(close, "An attribute value cannot contain '<'.");
                close++;
            }
            if (close >= Text.Length) throw new XmlError(open, $"The value of '{name}' is not closed.");
            if (element.Attribute(name) is not null) throw new XmlError(spaced, $"The attribute '{name}' is written twice.");
            var value = new Span(open + 1, close);
            element.Attributes.Add(new XmlAttribute(name, new Span(spaced, close + 1), value, Decode(value, element)));
            position = close + 1;
        }
        element.StartTag = new Span(start, position);
        element.Own = element.StartTag;
        _leaves.Add(element.StartTag);
        _entries.Add(element);
        if (parent is null)
        {
            _document = element;
            _root!.Children.Add(element);
        }
        else
        {
            parent.Children.Add(element);
            parent.Content.Add(element);
        }
        if (!element.SelfClosed) stack.Push(element);
        return position;
    }

    private int ReadEndTag(int start, Stack<XmlElement> stack)
    {
        var nameEnd = ReadName(start + 2);
        var name = Text.Text(start + 2, nameEnd);
        var close = SkipSpace(nameEnd);
        if (close >= Text.Length || Text.Bytes[close] != (byte)'>') throw new XmlError(start, $"The end tag of '{name}' is not closed.");
        if (stack.Count == 0 || stack.Peek().Name != name)
        {
            throw new XmlError(start, stack.Count == 0 ? $"The end tag '{name}' closes nothing." : $"The end tag '{name}' does not close '{stack.Peek().Name}'.");
        }
        var element = stack.Pop();
        element.EndTag = new Span(start, close + 1);
        element.Own = new Span(element.StartTag.Start, close + 1);
        _leaves.Add(element.EndTag.Value);
        return close + 1;
    }

    /// <summary>The line span (FBL §4.1.1): whole lines, extended upwards over comment lines at the same indentation.</summary>
    private Span? LineSpanOf(Entry entry)
    {
        if (!OnlyWhitespaceBefore(entry.Own.Start) || !OnlyTriviaAfter(entry.Own.End)) return null;
        var first = Text.LineIndexAt(entry.Own.Start);
        var last = Text.LineIndexAt(entry.Own.End - 1);
        while (first > 0 && _commentLines.TryGetValue(first - 1, out var comment) && comment.Start - Text.Lines[first - 1].Start == entry.Indent) first--;
        return new Span(Text.Lines[first].Start, Text.Lines[last].End);
    }

    public override int Step
    {
        get
        {
            foreach (var entry in _entries)
            {
                if (entry.Parent is XmlElement { IsRoot: false } parent && entry.LineSpan is not null && parent.LineSpan is not null && entry.Indent >= parent.Indent)
                {
                    return entry.Indent - parent.Indent;
                }
            }
            return Binding.Text.Indent == 0 ? 1 : Binding.Text.Indent;
        }
    }

    private bool AttributeEquals(Entry entry, string attribute, string value) => ((XmlElement)entry).Attribute(attribute)?.Text == value;

    private XmlElement? Child(XmlElement element, string selector) =>
        Selector.Match(element, selector, AttributeEquals).FirstOrDefault().Entry as XmlElement;

    public override IEnumerable<Candidate> Candidates(Rule rule)
    {
        if (rule.At is null || _root is null) yield break;
        foreach (var (entry, captures) in Selector.Match(_root, rule.At, AttributeEquals))
        {
            var element = (XmlElement)entry;
            if (element.Unreadable is { } reason)
            {
                if (!Findings.Any(f => f.Code == FindingCodes.UnreadableEntry && f.Location?.Line == Locate(element.Own).Line))
                {
                    Report(FindingCodes.UnreadableEntry, FindingSeverity.Warning, $"This entry cannot be read: {reason}", element.StartTag);
                }
                continue;
            }
            yield return new Candidate(rule, null, entry, captures);
        }
    }

    public override IEnumerable<Entry> Enclosing(Entry entry)
    {
        for (var parent = entry.Parent; parent is XmlElement { IsRoot: false }; parent = parent.Parent) yield return parent;
    }

    /// <summary>An element's own character data, references decoded.</summary>
    private static string OwnText(XmlElement element) => string.Concat(element.Content.OfType<XmlTextRun>().Select(t => t.Text));

    /// <summary>All character data inside an element, its descendants' included.</summary>
    private static string AllText(XmlElement element) =>
        string.Concat(element.Content.Select(c => c is XmlTextRun run ? run.Text : AllText((XmlElement)c)));

    public override object? CelValue(Candidate candidate)
    {
        var element = (XmlElement)candidate.Entry;
        var map = new CelMap();
        foreach (var attribute in element.Attributes) map[attribute.Name] = attribute.Text;
        map["text"] = OwnText(element);
        return map;
    }

    public override (string Name, object? Value) CelExtra(Candidate candidate)
    {
        var path = new CelMap();
        foreach (var (name, value) in candidate.Captures) path[name] = value;
        return ("path", path);
    }

    public override SlotRead Read(Candidate candidate, Slot slot)
    {
        var element = (XmlElement)candidate.Entry;
        if (slot.Capture is { } capture)
        {
            return candidate.Captures.TryGetValue(capture, out var key)
                ? new SlotRead(key, null, true, false, "An element's name is not rewritten in this file.")
                : SlotRead.Absent;
        }
        var target = slot.Child is null ? element : Child(element, slot.Child);
        if (slot.XmlAttribute is { } name)
        {
            if (target is null) return SlotRead.Absent;
            return ReadAttribute(target, name);
        }
        if (slot.Text)
        {
            if (target is null) return SlotRead.Absent;
            var html = slot is AttributeBinding { HtmlParagraphs: true };
            var writable = target.Unreadable is null;
            if (html)
            {
                var body = Child(target, "html/body") ?? target;
                var paragraphs = Paragraphs(body);
                return new SlotRead(paragraphs, body.SelfClosed ? null : new Span(body.ContentStart, body.ContentEnd), true, writable && !body.SelfClosed)
                {
                    Node = new XmlTextNode(target, body.SelfClosed ? null : new Span(body.ContentStart, body.ContentEnd), true),
                };
            }
            if (target.SelfClosed) return new SlotRead("", null, true, writable) { Node = new XmlTextNode(target, null, false) };
            var firstChild = target.Elements.FirstOrDefault();
            var span = new Span(target.ContentStart, firstChild?.Own.Start ?? target.ContentEnd);
            var value = string.Concat(target.Content.TakeWhile(c => c is XmlTextRun).Cast<XmlTextRun>().Select(t => t.Text));
            return new SlotRead(value, span, true, writable) { Node = new XmlTextNode(target, span, false), Wire = value };
        }
        return SlotRead.ReadOnlyAbsent($"An xml entry has no {slot}.");
    }

    private static SlotRead ReadAttribute(XmlElement element, string name)
    {
        if (element.Attribute(name) is { } attribute)
        {
            return new SlotRead(attribute.Text, attribute.Value, true, element.Unreadable is null) { Node = attribute, Wire = attribute.Text };
        }
        return SlotRead.Absent with { Node = element };
    }

    /// <summary>An html body as plain text (FBL §4.5): one line per <c>p</c> element, whitespace inside a paragraph collapsed.</summary>
    private static string Paragraphs(XmlElement body)
    {
        var paragraphs = Descendants(body).Where(e => e.Name == "p").ToList();
        if (paragraphs.Count == 0) return Collapse(AllText(body));
        return string.Join('\n', paragraphs.Select(p => Collapse(AllText(p))));
    }

    private static IEnumerable<XmlElement> Descendants(XmlElement element)
    {
        foreach (var child in element.Elements)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static string Collapse(string text) => Whitespace().Replace(text, " ").Trim();

    public override SlotRead ReadRaw(Entry entry, string name) => entry is XmlElement element ? ReadAttribute(element, name) : SlotRead.Absent;

    public override bool HeaderHolds(HeaderSettings header)
    {
        if (header.Line is null) return true;
        return _document is not null && Regex(header.Line, false).IsMatch(Text.Text(_document.StartTag));
    }

    // ---- writing (FBL §6) ----

    /// <summary>Text escaping (FBL §4.5): <c>&amp;</c>, <c>&lt;</c> and <c>&gt;</c>.</summary>
    public static string EscapeText(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);

    /// <summary>Attribute escaping (FBL §4.5): the text escapes, and <c>"</c>, LF and CR.</summary>
    public static string EscapeAttribute(string value) =>
        EscapeText(value).Replace("\"", "&quot;", StringComparison.Ordinal).Replace("\n", "&#xa;", StringComparison.Ordinal).Replace("\r", "&#xd;", StringComparison.Ordinal);

    /// <summary>Plain text as html paragraphs (FBL §4.5): one <c>&lt;p&gt;line&lt;/p&gt;</c> per line.</summary>
    public static string HtmlParagraphs(string value) =>
        value.Length == 0 ? "" : string.Concat(value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => $"<p>{EscapeText(line)}</p>"));

    public override string Format(SlotRead read, AttributeBinding? binding, object? value)
    {
        var plain = binding is null ? NewText.Plain(value, null) : NewText.Wire(binding, value, read.Wire) ?? NewText.Plain(value, binding);
        var node = read.Node is OverrideNode over ? over.Node : read.Node;
        return node switch
        {
            XmlTextNode { Html: true } => HtmlParagraphs(plain),
            XmlTextNode => EscapeText(plain),
            _ => EscapeAttribute(plain),
        };
    }

    public override void Write(Plan plan, ReadElement element, IReadOnlyList<SlotChange> changes)
    {
        var owner = (XmlElement)element.Entry;
        foreach (var change in changes)
        {
            var read = change.Read;
            var binding = change.Binding;
            var node = read.Node is OverrideNode over ? over.Node : read.Node;
            switch (node)
            {
                case XmlAttribute attribute when read.Present:
                    if (change.IsEmpty && binding.Empty == "remove")
                    {
                        plan.Add(SpliceOperation.RemoveKey, WhitespaceBefore(attribute.Own.Start), attribute.Own.End, "");
                    }
                    else
                    {
                        var written = Format(read, binding, change.Value);
                        if (written != Text.Text(attribute.Value)) plan.Add(SpliceOperation.ReplaceValue, attribute.Value, written);
                    }
                    continue;
                case XmlTextNode text when read.Present:
                    var removesChild = text.Element != owner && (binding.Create is not null || read.Node is OverrideNode);
                    if (change.IsEmpty && binding.Empty == "remove" && removesChild)
                    {
                        RemoveChild(plan, (XmlElement)text.Element.Parent!, text.Element);
                    }
                    else if (text.Span is { } span)
                    {
                        var written = Format(read, binding, change.Value);
                        if (written != Text.Text(span)) plan.Add(SpliceOperation.ReplaceValue, span, written);
                    }
                    else
                    {
                        Open(plan, text.Element, Format(read, binding, change.Value), SpliceOperation.ReplaceValue, inline: true);
                    }
                    continue;
            }
            if (binding.XmlAttribute is { } name)
            {
                if (node is not XmlElement target)
                {
                    Plan.Refuse($"The {Binding.Name} file has no {binding.Child} to write \"{name}\" in.");
                    return;
                }
                var offset = target.Attributes.Count > 0 ? target.Attributes[^1].Own.End : target.NameEnd;
                plan.Add(SpliceOperation.InsertKey, offset, offset, $" {name}=\"{Format(read, binding, change.Value)}\"");
                continue;
            }
            if (binding.Text && binding.Create is { } create)
            {
                var content = Format(read with { Node = new XmlTextNode(owner, null, binding.HtmlParagraphs) }, binding, change.Value);
                CreateChild(plan, owner, create, NewText.Render(create.Emit, placeholder => placeholder == "value" ? content : null));
                continue;
            }
            Plan.Refuse($"The {Binding.Name} file has no {binding.Child ?? "element"} to write the {change.Attribute} in.");
        }
    }

    /// <summary>The start of the whitespace directly before <paramref name="offset"/>.</summary>
    private int WhitespaceBefore(int offset)
    {
        while (offset > 0 && Text.Bytes[offset - 1] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') offset--;
        return offset;
    }

    /// <summary>The bytes from the start of an element's line to its first byte.</summary>
    private string IndentOf(Entry entry) => Text.Text(Text.Lines[Text.LineIndexAt(entry.Own.Start)].Start, entry.Own.Start);

    private string ChildIndent(XmlElement parent) => IndentOf(parent) + Indentation(Step);

    /// <summary>
    /// Opens a self-closed element to hold <paramref name="content"/> (FBL §6.1 <c>self-close</c>):
    /// its <c>/&gt;</c> becomes <c>&gt;</c>, the content follows on its own line, and a new end tag
    /// closes it on the line after, at the element's indentation.
    /// </summary>
    private void Open(Plan plan, XmlElement element, string content, SpliceOperation operation, bool inline = false)
    {
        var at = element.Close.End;
        plan.Add(SpliceOperation.SelfClose, element.Close, ">");
        if (inline || element.LineSpan is null)
        {
            plan.Add(operation, at, at, content);
            plan.Add(SpliceOperation.SelfClose, at, at, $"</{element.Name}>");
            return;
        }
        var newline = NewlineAt(element.Close.Start);
        plan.Add(operation, at, at, newline + ChildIndent(element) + content);
        plan.Add(SpliceOperation.SelfClose, at, at, newline + IndentOf(element) + $"</{element.Name}>");
    }

    /// <summary>Appends <paramref name="content"/> as the element's last child, on its own line when the last child is on one (FBL §6.3).</summary>
    private void Append(Plan plan, XmlElement element, string content, SpliceOperation operation)
    {
        if (element.SelfClosed)
        {
            Open(plan, element, content, operation);
            return;
        }
        if (element.Elements.LastOrDefault() is { } last)
        {
            if (last.LineSpan is { } line)
            {
                var lineOfLast = Text.Lines[Text.LineIndexAt(last.Own.End - 1)];
                var ending = lineOfLast.Ending.Length > 0 ? lineOfLast.Ending : NewlineAt(last.Own.End);
                plan.Add(operation, line.End, line.End, IndentOf(last) + content + ending);
            }
            else
            {
                plan.Add(operation, last.Own.End, last.Own.End, content);
            }
            return;
        }
        var end = element.EndTag!.Value;
        if (OnlyWhitespaceBefore(end.Start) && Text.LineIndexAt(end.Start) != Text.LineIndexAt(element.StartTag.End))
        {
            var lineStart = Text.Lines[Text.LineIndexAt(end.Start)].Start;
            plan.Add(operation, lineStart, lineStart, ChildIndent(element) + content + NewlineAt(lineStart));
            return;
        }
        plan.Add(operation, end.Start, end.Start, content);
    }

    /// <summary>A child element holding a value (FBL §5.2 <c>create</c>), placed first, last or before the first child of a name.</summary>
    private void CreateChild(Plan plan, XmlElement element, CreateChild create, string content)
    {
        if (element.SelfClosed)
        {
            Open(plan, element, content, SpliceOperation.InsertKey);
            return;
        }
        var before = create.Place switch
        {
            "first" => element.Elements.FirstOrDefault(),
            "before" => element.Elements.FirstOrDefault(e => e.Name == create.Before),
            _ => null,
        };
        if (before is null)
        {
            Append(plan, element, content, SpliceOperation.InsertKey);
            return;
        }
        if (before.LineSpan is not null)
        {
            var offset = Text.Lines[Text.LineIndexAt(before.Own.Start) - 1].ContentEnd;
            plan.Add(SpliceOperation.InsertKey, offset, offset, NewlineAt(offset) + IndentOf(before) + content);
            return;
        }
        plan.Add(SpliceOperation.InsertKey, before.Own.Start, before.Own.Start, content);
    }

    /// <summary>
    /// Removes a child element holding a value with the line break before it (<c>remove-key</c>), and
    /// closes the parent again when only whitespace is left in it (<c>self-close</c>).
    /// </summary>
    private void RemoveChild(Plan plan, XmlElement parent, XmlElement child)
    {
        var line = Text.LineIndexAt(child.Own.Start);
        var span = child.LineSpan is not null && line > 0 ? new Span(Text.Lines[line - 1].ContentEnd, child.Own.End) : child.Own;
        if (span.Start < parent.ContentStart) span = new Span(parent.ContentStart, span.End);
        var closes = parent.EndTag is { } end
            && IsWhitespace(new Span(parent.ContentStart, span.Start))
            && IsWhitespace(new Span(span.End, end.Start));
        if (!closes)
        {
            plan.Add(SpliceOperation.RemoveKey, span, "");
            return;
        }
        plan.Add(SpliceOperation.SelfClose, parent.Close.Start, span.Start, "/>");
        plan.Add(SpliceOperation.RemoveKey, span, "");
        plan.Add(SpliceOperation.SelfClose, span.End, parent.EndTag!.Value.End, "");
    }

    public override void Insert(Plan plan, InsertRequest request)
    {
        var insert = request.Rule.Insert!;
        var parent = request.Parent?.Entry as XmlElement
            ?? (insert.Container is { } container && _root is not null ? Selector.Match(Selector.Start(container, _root, null), container, AttributeEquals).FirstOrDefault().Entry as XmlElement : null)
            ?? _document;
        if (parent is null)
        {
            Plan.Refuse($"The {Binding.Name} file has no element to add the {request.Rule.Type} to.");
            return;
        }
        var text = insert.Emit is { } emit
            ? NewText.Render(emit, name => name switch
            {
                "id" => request.Id is null ? null : EscapeAttribute(request.Id),
                "source" => request.Source is null ? null : EscapeAttribute(request.Source.Key),
                "target" => request.Target is null ? null : EscapeAttribute(request.Target.Key),
                _ => request.Values.TryGetValue(name, out var v) && v is not null ? EscapeAttribute(NewText.Plain(v, request.Rule.Attribute(name))) : null,
            })
            : NewElement(request);
        if (insert.Place == "start" && parent.Elements.FirstOrDefault() is { } first)
        {
            if (first.LineSpan is not null)
            {
                var lineStart = Text.Lines[Text.LineIndexAt(first.Own.Start)].Start;
                plan.Add(SpliceOperation.InsertEntry, lineStart, lineStart, IndentOf(first) + text + NewlineAt(lineStart));
            }
            else
            {
                plan.Add(SpliceOperation.InsertEntry, first.Own.Start, first.Own.Start, text);
            }
            return;
        }
        Append(plan, parent, text, SpliceOperation.InsertEntry);
    }

    /// <summary>A new element without an <c>emit</c>: the rule's element name with its attributes in <c>insert.keys</c> order, then binding order (FBL §6.3).</summary>
    private static string NewElement(InsertRequest request)
    {
        var rule = request.Rule;
        var last = rule.At!.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1];
        var bracket = last.IndexOf('[', StringComparison.Ordinal);
        var name = bracket > 0 ? last[..bracket] : last;
        var written = new List<(string Name, string Value)>();
        if (rule.Id?.From?.XmlAttribute is { } idAttribute && request.Id is { } id) written.Add((idAttribute, id));
        foreach (var (attribute, binding) in rule.Attributes)
        {
            if (binding.XmlAttribute is { } xml && binding.Child is null && request.Values.TryGetValue(attribute, out var value) && value is not null)
            {
                written.Add((xml, NewText.Plain(value, binding)));
            }
        }
        if (rule.Source?.XmlAttribute is { } source && request.Source is not null) written.Add((source, request.Source.Key));
        if (rule.Target?.XmlAttribute is { } target && request.Target is not null) written.Add((target, request.Target.Key));
        var keys = rule.Insert!.Keys;
        var ordered = written.OrderBy(w => keys.Contains(w.Name) ? keys.ToList().IndexOf(w.Name) : keys.Count).ToList();
        return $"<{name}" + string.Concat(ordered.Select(w => $" {w.Name}=\"{EscapeAttribute(w.Value)}\"")) + "/>";
    }

    public override void Remove(Plan plan, ReadElement element, IReadOnlySet<ReadElement> removed)
    {
        var entry = (XmlElement)element.Entry;
        if (entry == _document)
        {
            Plan.Refuse($"The document element of the {Binding.Name} file cannot be removed.");
            return;
        }
        plan.Add(SpliceOperation.RemoveEntry, entry.RemovalSpan, "");
    }
}
