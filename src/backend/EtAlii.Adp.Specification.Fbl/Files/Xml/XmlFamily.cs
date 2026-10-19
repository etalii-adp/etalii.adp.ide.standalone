using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Rules;
using EtAlii.Adp.Specification.Fbl.Text;

namespace EtAlii.Adp.Specification.Fbl.Xml;

/// <summary>
/// The xml family (FBL §4.5): a lossless reading of an XML 1.0 document over its bytes. The prolog,
/// comments, processing instructions and a document type declaration are unbound content; carriage
/// returns are kept, never normalised; entity declarations are not processed.
/// </summary>
internal sealed partial class XmlFamily(BodyText text, FblBinding binding, FblOptions options) : FamilyReader(text, binding, options)
{
    private List<Entry> _entries = [];
    private List<Span> _leaves = [];
    private readonly Dictionary<int, Span> _commentLines = [];
    private XmlElement? _root;
    private XmlElement? _document;

    public override string FamilyName => "xml";

    public override IReadOnlyList<Entry> Entries => _entries;

    protected override IReadOnlyList<Span> Leaves => _leaves;

    protected override bool IsTrivia(Span gap) => IsWhitespace(gap);

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    // ---- reading ----

    public override void Parse()
    {
        var parser = new XmlParser(Text);
        _entries = parser.Entries;
        _leaves = parser.Leaves;
        try
        {
            parser.Parse();
        }
        catch (XmlParser.XmlError error)
        {
            Unreadable = (error.Offset, $"The body is not well-formed XML: {error.Message}");
            return;
        }
        finally
        {
            _root = parser.Root;
            _document = parser.Document;
        }
        foreach (var comment in parser.Comments) RememberComment(comment);
        foreach (var entry in _entries) entry.LineSpan = LineSpanOf(entry);
    }

    private void RememberComment(Span comment)
    {
        var line = Text.LineIndexAt(comment.Start);
        if (Text.LineIndexAt(comment.End - 1) == line && OnlyWhitespaceBefore(comment.Start) && OnlyTriviaAfter(comment.End))
        {
            _commentLines[line] = comment;
        }
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

    protected override int Step
    {
        get
        {
            foreach (var entry in _entries)
            {
                if (entry is { Parent: XmlElement { IsRoot: false, LineSpan: not null } parent, LineSpan: not null } && entry.Indent >= parent.Indent)
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
        foreach ((Entry entry, IReadOnlyDictionary<string, string> captures) in Selector.Match(_root, rule.At, AttributeEquals))
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

    public override object CelValue(Candidate candidate)
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
        foreach ((string name, string value) in candidate.Captures) path[name] = value;
        return ("path", path);
    }

    public override SlotRead Read(Candidate candidate, Slot slot)
    {
        var element = (XmlElement)candidate.Entry;
        if (slot.Kind is SlotKind.Capture(var capture))
        {
            return candidate.Captures.TryGetValue(capture, out var key)
                ? new SlotRead(key, null, true, false, "An element's name is not rewritten in this file.")
                : SlotRead.Absent;
        }
        var target = slot.Child is null ? element : Child(element, slot.Child);
        if (slot.Kind is SlotKind.XmlAttribute(var name))
        {
            return target is null ? SlotRead.Absent : ReadAttribute(target, name);
        }
        if (slot.Kind is SlotKind.Text)
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
        return paragraphs.Count == 0
            ? Collapse(AllText(body))
            : string.Join('\n', paragraphs.Select(p => Collapse(AllText(p))));
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
    private static string EscapeText(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);

    /// <summary>Attribute escaping (FBL §4.5): the text escapes, and <c>"</c>, LF and CR.</summary>
    private static string EscapeAttribute(string value) =>
        EscapeText(value).Replace("\"", "&quot;", StringComparison.Ordinal).Replace("\n", "&#xa;", StringComparison.Ordinal).Replace("\r", "&#xd;", StringComparison.Ordinal);

    /// <summary>Plain text as html paragraphs (FBL §4.5): one <c>&lt;p&gt;line&lt;/p&gt;</c> per line.</summary>
    private static string HtmlParagraphs(string value) =>
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

    protected override bool RemoveSlot(Plan plan, ReadElement element, SlotChange change)
    {
        if (!change.Read.Present) return false;
        switch (Node(change.Read))
        {
            case XmlAttribute attribute:
                plan.Add(SpliceOperation.RemoveKey, WhitespaceBefore(attribute.Own.Start), attribute.Own.End, "");
                return true;
            case XmlTextNode text when text.Element != element.Entry && (change.Binding.Create is not null || change.Read.Node is OverrideNode):
                RemoveChild(plan, (XmlElement)text.Element.Parent!, text.Element);
                return true;
            default:
                return false;
        }
    }

    protected override bool ReplaceSlot(Plan plan, SlotChange change)
    {
        var read = change.Read;
        switch (Node(read))
        {
            case XmlAttribute attribute:
                var value = Format(read, change.Binding, change.Value);
                if (value != Text.Text(attribute.Value)) plan.Add(SpliceOperation.ReplaceValue, attribute.Value, value);
                return true;
            case XmlTextNode { Span: { } span }:
                var written = Format(read, change.Binding, change.Value);
                if (written != Text.Text(span)) plan.Add(SpliceOperation.ReplaceValue, span, written);
                return true;
            case XmlTextNode text:
                Open(plan, text.Element, Format(read, change.Binding, change.Value), SpliceOperation.ReplaceValue, inline: true);
                return true;
            default:
                return false;
        }
    }

    protected override bool InsertSlot(Plan plan, ReadElement element, SlotChange change)
    {
        var read = change.Read;
        var binding = change.Binding;
        if (binding.Kind is SlotKind.XmlAttribute(var name))
        {
            if (Node(read) is not XmlElement target)
            {
                Plan.Refuse($"The {Binding.Name} file has no {binding.Child} to write \"{name}\" in.");
                return true;
            }
            // After the nearest attribute before it in the rule's own order, where the order
            // places it before another that is there; else after the last attribute (FBL §6.3).
            var order = change.Rule.WireOrder().ToList();
            var place = order.IndexOf(name);
            var before = place < 0 ? null : target.Attributes.LastOrDefault(attribute => order.IndexOf(attribute.Name) is >= 0 and var at && at < place);
            var offset = before is not null ? before.Own.End : target.Attributes.Count > 0 ? target.Attributes[^1].Own.End : target.NameEnd;
            plan.Add(SpliceOperation.InsertKey, offset, offset, $" {name}=\"{Format(read, binding, change.Value)}\"");
            return true;
        }
        if (binding is { Kind: SlotKind.Text, Create: { } create })
        {
            var owner = (XmlElement)element.Entry;
            var content = Format(read with { Node = new XmlTextNode(owner, null, binding.HtmlParagraphs) }, binding, change.Value);
            CreateChild(plan, owner, create, NewText.Render(create.Emit, placeholder => placeholder == "value" ? content : null));
            return true;
        }
        Plan.Refuse($"The {Binding.Name} file has no {binding.Child ?? "element"} to write the {change.Attribute} in.");
        return true;
    }

    /// <summary>The node a slot was read from, through an override to the node it stands for.</summary>
    private static object? Node(SlotRead read) => read.Node is OverrideNode over ? over.Node : read.Node;

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
            ChildPlace.First => element.Elements.FirstOrDefault(),
            ChildPlace.Before => element.Elements.FirstOrDefault(e => e.Name == create.Before),
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
    private void RemoveChild(Plan plan, XmlElement parent, XmlElement child, SpliceOperation operation = SpliceOperation.RemoveKey)
    {
        var line = Text.LineIndexAt(child.Own.Start);
        var span = child.LineSpan is not null && line > 0 ? child.Own with { Start = Text.Lines[line - 1].ContentEnd } : child.Own;
        if (span.Start < parent.ContentStart) span = span with { Start = parent.ContentStart };
        var closes = parent.EndTag is { } end
            && IsWhitespace(new Span(parent.ContentStart, span.Start))
            && IsWhitespace(new Span(span.End, end.Start));
        if (!closes)
        {
            plan.Add(operation, span, "");
            return;
        }
        plan.Add(SpliceOperation.SelfClose, parent.Close.Start, span.Start, "/>");
        plan.Add(operation, span, "");
        plan.Add(SpliceOperation.SelfClose, span.End, parent.EndTag!.Value.End, "");
    }

    public override void Insert(Plan plan, InsertRequest request)
    {
        var insert = request.Rule.Insert!;
        // FBL §5's place: {before: key} is not implemented here yet; refused, as the lines family does, rather than placed at the end.
        if (insert.Place == InsertPlace.Before) Plan.Refuse($"A {FamilyName} body cannot place a new entry '{WireName.Of(insert.Place)}'.");
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
        if (insert.Place == InsertPlace.Start && parent.Elements.FirstOrDefault() is { } first)
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
        if (rule.Id?.From?.Kind is SlotKind.XmlAttribute(var idAttribute) && request.Id is { } id) written.Add((idAttribute, id));
        foreach ((string attribute, AttributeBinding binding) in rule.Attributes)
        {
            if (binding is { Kind: SlotKind.XmlAttribute(var xml), Child: null } && request.Values.TryGetValue(attribute, out var value) && value is not null)
            {
                written.Add((xml, NewText.Plain(value, binding)));
            }
        }
        if (rule.Source?.Kind is SlotKind.XmlAttribute(var source) && request.Source is not null) written.Add((source, request.Source.Key));
        if (rule.Target?.Kind is SlotKind.XmlAttribute(var target) && request.Target is not null) written.Add((target, request.Target.Key));
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

        // The only child of an element that an insert may have opened for it (`last-child`, FBL §6.2),
        // or of one that goes when it is left empty: the parent is closed again, so that adding a
        // child and removing it leaves the bytes it started from. The
        // entry goes with the line break before it, and its parent becomes self-closed.
        if ((element.Rule.Remove?.RemoveContainerWhenEmpty == true || element.Rule.Insert?.Place == InsertPlace.LastChild) &&
            entry.Parent is XmlElement { SelfClosed: false } parent &&
            parent != _document &&
            parent.Elements.Count() == 1)
        {
            RemoveChild(plan, parent, entry, SpliceOperation.RemoveEntry);
            return;
        }

        plan.Add(SpliceOperation.RemoveEntry, entry.RemovalSpan, "");
    }
}
