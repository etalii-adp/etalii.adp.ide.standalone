namespace EtAlii.Adp.Editor.Xml;

/// <summary>Where the caret stands, as far as completion is concerned (xml-editor Requirement 7).</summary>
internal enum CaretKind
{
    /// <summary>A comment, a CDATA section, an instruction, a declaration or an end tag: nothing is offered.</summary>
    Unknown,

    /// <summary>Between tags, where an element may start or an element's text is typed.</summary>
    Content,

    /// <summary>Directly after a <c>&lt;</c>, on the name of the element being typed.</summary>
    ElementName,

    /// <summary>Inside a start tag after its name, where an attribute may be named.</summary>
    AttributeName,

    /// <summary>Inside the quotes of an attribute's value.</summary>
    AttributeValue,
}

/// <summary>One attribute written in the start tag around the caret.</summary>
internal sealed record WrittenAttribute(string Name, string Value, int NameFrom, int NameTo);

/// <summary>
/// Where the caret is, decided by a scan of the text around it rather than by a reader: a
/// document being typed is rarely well-formed at the caret, and a reader stops there. The
/// schema's view of the position comes afterwards, from the text before <see cref="Cut"/>.
/// </summary>
/// <param name="Kind">What kind of place the caret is in.</param>
/// <param name="Cut">Where the text that is fed to the validator ends: the tag's <c>&lt;</c>, or the end of the last markup before the caret.</param>
/// <param name="From">The start of what a completion replaces.</param>
/// <param name="To">The end of what a completion replaces.</param>
/// <param name="ElementName">The start tag's name, as written, prefix included; empty in content.</param>
/// <param name="IsWrittenTag">Whether the start tag at the caret is already closed by a <c>&gt;</c>, so only a name is to be inserted.</param>
/// <param name="Attributes">The attributes written in the start tag, before and after the caret.</param>
/// <param name="AttributeName">The attribute whose value holds the caret.</param>
internal sealed record CaretContext(
    CaretKind Kind,
    int Cut,
    int From,
    int To,
    string ElementName,
    bool IsWrittenTag,
    IReadOnlyList<WrittenAttribute> Attributes,
    string AttributeName)
{
    private static readonly CaretContext Nowhere = new(CaretKind.Unknown, 0, 0, 0, "", false, [], "");

    /// <summary>Decides where <paramref name="offset"/> is in <paramref name="content"/>.</summary>
    public static CaretContext At(string content, int offset)
    {
        ArgumentNullException.ThrowIfNull(content);
        offset = Math.Clamp(offset, 0, content.Length);
        var before = content.AsSpan(0, offset);

        // Requirement 7.7: inside something whose own syntax hides the document's structure.
        if (IsOpen(before, "<!--", "-->") || IsOpen(before, "<![CDATA[", "]]>") || IsOpen(before, "<?", "?>"))
        {
            return Nowhere;
        }

        // A '<' never stands in a value or in text of a well-formed document, so the last one
        // before the caret starts the markup the caret is in or after.
        var open = before.LastIndexOf('<');
        if (open < 0)
        {
            return Content(content, offset, 0);
        }

        var tagEnd = TagEnd(content, open + 1, offset);
        if (tagEnd >= 0)
        {
            return Content(content, offset, tagEnd + 1);
        }

        return InStartTag(content, offset, open);
    }

    private static CaretContext Content(string content, int offset, int cut)
    {
        // A word of the element's text that the caret is in or after: what a value replaces.
        var from = offset;
        while (from > cut && !char.IsWhiteSpace(content[from - 1]))
        {
            from--;
        }

        var to = offset;
        while (to < content.Length && content[to] != '<' && !char.IsWhiteSpace(content[to]))
        {
            to++;
        }

        return Nowhere with { Kind = CaretKind.Content, Cut = cut, From = from, To = to };
    }

    private static CaretContext InStartTag(string content, int offset, int open)
    {
        var first = open + 1;
        if (first < content.Length && content[first] is '/' or '!' or '?')
        {
            // An end tag is the client's (Requirement 3.6); a declaration has nothing to offer.
            return Nowhere;
        }

        var nameEnd = first;
        while (nameEnd < content.Length && IsNameChar(content[nameEnd]))
        {
            nameEnd++;
        }

        var name = content[first..nameEnd];
        (List<WrittenAttribute> attributes, bool closed, WrittenAttribute? caretAttribute, bool inValue, int valueFrom, int valueTo) = ScanAttributes(content, nameEnd, offset);
        if (offset <= nameEnd)
        {
            return Nowhere with
            {
                Kind = CaretKind.ElementName,
                Cut = open,
                From = first,
                To = nameEnd,
                ElementName = name,
                IsWrittenTag = closed,
                Attributes = attributes,
            };
        }

        if (name.Length == 0)
        {
            return Nowhere;
        }

        if (inValue)
        {
            return new CaretContext(CaretKind.AttributeValue, open, valueFrom, valueTo, name, closed, attributes, caretAttribute?.Name ?? "");
        }

        // Between attributes, or on the name of one: only there is an attribute name offered.
        if (caretAttribute is null && offset > 0 && !char.IsWhiteSpace(content[offset - 1]))
        {
            return Nowhere;
        }

        return Nowhere with
        {
            Kind = CaretKind.AttributeName,
            Cut = open,
            From = caretAttribute?.NameFrom ?? offset,
            To = caretAttribute?.NameTo ?? offset,
            ElementName = name,
            IsWrittenTag = closed,
            Attributes = attributes,
        };
    }

    /// <summary>
    /// Reads the attributes of a start tag from the end of its name to its <c>&gt;</c>, or to
    /// where the tag plainly stops being written, and says which of them the caret is on.
    /// </summary>
    private static (List<WrittenAttribute> Attributes, bool Closed, WrittenAttribute? AtCaret, bool InValue, int ValueFrom, int ValueTo) ScanAttributes(string content, int start, int offset)
    {
        var attributes = new List<WrittenAttribute>();
        WrittenAttribute? atCaret = null;
        var inValue = false;
        var valueFrom = 0;
        var valueTo = 0;
        var at = start;
        while (at < content.Length)
        {
            var character = content[at];
            if (character is '>' or '<' || (character == '/' && at + 1 < content.Length && content[at + 1] == '>'))
            {
                return (attributes, character != '<', atCaret, inValue, valueFrom, valueTo);
            }

            if (!IsNameChar(character))
            {
                at++;
                continue;
            }

            var nameFrom = at;
            while (at < content.Length && IsNameChar(content[at]))
            {
                at++;
            }

            var nameTo = at;
            while (at < content.Length && char.IsWhiteSpace(content[at]))
            {
                at++;
            }

            var value = "";
            int? quoteFrom = null;
            var quoteTo = at;
            if (at < content.Length && content[at] == '=')
            {
                at++;
                while (at < content.Length && char.IsWhiteSpace(content[at]))
                {
                    at++;
                }

                if (at < content.Length && content[at] is '"' or '\'')
                {
                    var quote = content[at];
                    quoteFrom = at + 1;
                    var close = content.IndexOf(quote, at + 1);

                    // A value left open runs to the end of the line it was started on, so a
                    // value being typed does not swallow the rest of the document.
                    var lineEnd = content.IndexOf('\n', at + 1);
                    quoteTo = close < 0 ? (lineEnd < 0 ? content.Length : lineEnd) : close;
                    value = content[(at + 1)..quoteTo];
                    at = close < 0 ? quoteTo : close + 1;
                }
            }

            var attribute = new WrittenAttribute(content[nameFrom..nameTo], value, nameFrom, nameTo);
            attributes.Add(attribute);
            if (offset >= nameFrom && offset <= nameTo)
            {
                atCaret = attribute;
            }
            else if (quoteFrom is { } from && offset >= from && offset <= quoteTo)
            {
                atCaret = attribute;
                inValue = true;
                valueFrom = from;
                valueTo = quoteTo;
            }
        }

        return (attributes, false, atCaret, inValue, valueFrom, valueTo);
    }

    /// <summary>The <c>&gt;</c> that ends the markup opened at <paramref name="from"/>, if it comes before <paramref name="offset"/>; quotes are honoured.</summary>
    private static int TagEnd(string content, int from, int offset)
    {
        char? quote = null;
        for (var at = from; at < offset; at++)
        {
            var character = content[at];
            if (quote is { } open)
            {
                if (character == open)
                {
                    quote = null;
                }
            }
            else if (character is '"' or '\'')
            {
                quote = character;
            }
            else if (character == '>')
            {
                return at;
            }
        }

        return -1;
    }

    private static bool IsOpen(ReadOnlySpan<char> before, string start, string end)
    {
        var opened = before.LastIndexOf(start, StringComparison.Ordinal);
        return opened >= 0 && before[opened..].IndexOf(end, StringComparison.Ordinal) < 0;
    }

    private static bool IsNameChar(char character) =>
        char.IsLetterOrDigit(character) || character is '_' or ':' or '-' or '.';
}
