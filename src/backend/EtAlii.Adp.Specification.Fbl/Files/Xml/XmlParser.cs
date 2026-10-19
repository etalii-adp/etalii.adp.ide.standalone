using System.Globalization;
using System.Text;
using EtAlii.Adp.Specification.Fbl.Rules;
using EtAlii.Adp.Specification.Fbl.Text;

namespace EtAlii.Adp.Specification.Fbl.Xml;

/// <summary>
/// The lossless reading of an XML 1.0 document over its bytes (FBL §4.5): elements with their tags'
/// spans, attributes and content, every tag, text and piece of unbound content a leaf. Carriage returns
/// are kept, never normalised; entity declarations are not processed.
/// </summary>
internal sealed class XmlParser(BodyText text)
{
    /// <summary>The document itself, whose one child is the document element.</summary>
    public XmlElement Root { get; private set; } = null!;

    public XmlElement? Document { get; private set; }

    public List<Entry> Entries { get; } = [];

    public List<Span> Leaves { get; } = [];

    /// <summary>Every comment, in the order it is written.</summary>
    public List<Span> Comments { get; } = [];

    public sealed class XmlError(int offset, string message) : Exception(message)
    {
        public int Offset { get; } = offset;
    }

    public void Parse()
    {
        var bytes = text.Bytes;
        Root = new XmlElement { IsRoot = true, Own = new Span(0, bytes.Length), Indent = 0 };
        var stack = new Stack<XmlElement>();
        var position = text.BomLength;
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
                Comments.Add(new Span(start, position));
            }
            else if (StartsWith(position, "<![CDATA["))
            {
                if (stack.Count == 0) throw new XmlError(position, "A CDATA section must be inside the document element.");
                var start = position;
                position = Skip(position, "]]>", "CDATA section");
                var content = text.Text(start + 9, position - 3);
                stack.Peek().Content.Add(new XmlTextRun(content));
            }
            else if (StartsWith(position, "<!"))
            {
                if (Document is not null) throw new XmlError(position, "A document type declaration must come before the document element.");
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
        if (Document is null) throw new XmlError(text.BomLength, "The body has no document element.");
    }

    private bool StartsWith(int position, string literal)
    {
        if (position + literal.Length > text.Length) return false;
        for (var i = 0; i < literal.Length; i++)
        {
            if (text.Bytes[position + i] != (byte)literal[i]) return false;
        }
        return true;
    }

    private int Find(int position, string literal)
    {
        for (var i = position; i + literal.Length <= text.Length; i++)
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
        Leaves.Add(new Span(position, end));
        return end;
    }

    private int SkipDeclaration(int position)
    {
        var depth = 0;
        byte quote = 0;
        for (var i = position + 2; i < text.Length; i++)
        {
            var b = text.Bytes[i];
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
                    Leaves.Add(new Span(position, i + 1));
                    return i + 1;
            }
        }
        throw new XmlError(position, "The declaration is not closed.");
    }

    private int ReadText(int position, XmlElement? parent)
    {
        var end = position;
        while (end < text.Length && text.Bytes[end] != (byte)'<') end++;
        var span = new Span(position, end);
        if (IsWhitespace(span))
        {
            parent?.Content.Add(new XmlTextRun(text.Text(span)));
            return end;
        }
        if (parent is null) throw new XmlError(position, "Text must be inside the document element.");
        Leaves.Add(span);
        parent.Content.Add(new XmlTextRun(Decode(span, parent)));
        return end;
    }

    /// <summary>Decodes the five predefined entities and character references; any other reference makes <paramref name="owner"/> unreadable.</summary>
    private string Decode(Span span, XmlElement? owner)
    {
        var raw = text.Text(span);
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
        while (end < text.Length && IsNameByte(text.Bytes[end], end == position)) end++;
        return end == position ? throw new XmlError(position, "A name was expected here.") : end;
    }

    private int SkipSpace(int position)
    {
        while (position < text.Length && text.Bytes[position] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') position++;
        return position;
    }

    private int ReadStartTag(int start, Stack<XmlElement> stack)
    {
        var parent = stack.Count > 0 ? stack.Peek() : null;
        if (parent is null && Document is not null) throw new XmlError(start, "The body has a second document element.");
        var nameEnd = ReadName(start + 1);
        var element = new XmlElement
        {
            Name = text.Text(start + 1, nameEnd),
            NameEnd = nameEnd,
            Own = new Span(start, nameEnd),
            Indent = start - text.Lines[text.LineIndexAt(start)].Start,
            Parent = parent ?? Root,
        };
        var position = nameEnd;
        while (true)
        {
            var spaced = SkipSpace(position);
            if (spaced >= text.Length) throw new XmlError(start, $"The start tag of '{element.Name}' is not closed.");
            if (StartsWith(spaced, "/>"))
            {
                element.SelfClosed = true;
                element.Close = new Span(spaced, spaced + 2);
                position = spaced + 2;
                break;
            }
            if (text.Bytes[spaced] == (byte)'>')
            {
                element.Close = new Span(spaced, spaced + 1);
                position = spaced + 1;
                break;
            }
            if (spaced == position) throw new XmlError(spaced, "Attributes must be separated by whitespace.");
            var attributeEnd = ReadName(spaced);
            var name = text.Text(spaced, attributeEnd);
            var equals = SkipSpace(attributeEnd);
            if (equals >= text.Length || text.Bytes[equals] != (byte)'=') throw new XmlError(equals, $"The attribute '{name}' has no value.");
            var open = SkipSpace(equals + 1);
            if (open >= text.Length || text.Bytes[open] is not ((byte)'"' or (byte)'\'')) throw new XmlError(open, $"The value of '{name}' must be quoted.");
            var quote = text.Bytes[open];
            var close = open + 1;
            while (close < text.Length && text.Bytes[close] != quote)
            {
                if (text.Bytes[close] == (byte)'<') throw new XmlError(close, "An attribute value cannot contain '<'.");
                close++;
            }
            if (close >= text.Length) throw new XmlError(open, $"The value of '{name}' is not closed.");
            if (element.Attribute(name) is not null) throw new XmlError(spaced, $"The attribute '{name}' is written twice.");
            var value = new Span(open + 1, close);
            element.Attributes.Add(new XmlAttribute(name, new Span(spaced, close + 1), value, Decode(value, element)));
            position = close + 1;
        }
        element.StartTag = new Span(start, position);
        element.Own = element.StartTag;
        Leaves.Add(element.StartTag);
        Entries.Add(element);
        if (parent is null)
        {
            Document = element;
            Root.Children.Add(element);
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
        var name = text.Text(start + 2, nameEnd);
        var close = SkipSpace(nameEnd);
        if (close >= text.Length || text.Bytes[close] != (byte)'>') throw new XmlError(start, $"The end tag of '{name}' is not closed.");
        if (stack.Count == 0 || stack.Peek().Name != name)
        {
            throw new XmlError(start, stack.Count == 0 ? $"The end tag '{name}' closes nothing." : $"The end tag '{name}' does not close '{stack.Peek().Name}'.");
        }
        var element = stack.Pop();
        element.EndTag = new Span(start, close + 1);
        element.Own = element.StartTag with { End = close + 1 };
        Leaves.Add(element.EndTag.Value);
        return close + 1;
    }

    private bool IsWhitespace(Span span)
    {
        for (var i = span.Start; i < span.End; i++)
        {
            if (text.Bytes[i] is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')) return false;
        }
        return true;
    }
}
