using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Schema;

namespace EtAlii.Adp.Editor.Xml;

/// <summary>
/// A well-formed document against its XSD or its DTD (xml-editor Requirement 6): a validating
/// reader over the text as typed, each event a finding with the platform's severity and sentence.
/// A finding about an element sits on its name in the start tag - also when the platform reports
/// it at the end tag, as it does for a missing child - and one about a value sits on the value.
/// What the reader reads is never written back: attribute defaults the schema supplies stay in the
/// reader.
/// </summary>
internal static partial class SchemaValidation
{
    public const string Source = "Schema";

    /// <summary>Validates against <paramref name="schemas"/>, or against the document's DTD when it is null.</summary>
    public static IReadOnlyList<TextFinding> Validate(string content, string fullPath, ProjectXmlResolver resolver, TextPositions positions, XmlSchemaSet? schemas)
    {
        var findings = new List<TextFinding>();
        var starts = new Stack<int>();
        var reading = new Reading();

        void OnEvent(object? sender, ValidationEventArgs args)
        {
            var exception = args.Exception;
            var at = positions.Offset(exception.LineNumber, exception.LinePosition);
            (int from, int to) = RangeOf(content, positions, at);

            if (AttributeValue().Match(exception.Message) is { Success: true } attribute)
            {
                (from, to) = ValueAfter(content, at, attribute.Groups["name"].Value) ?? (from, to);
            }
            else if (reading.Reader is { NodeType: XmlNodeType.EndElement } && starts.Count > 0)
            {
                // Reported at the end tag: a missing child, or a value that does not fit. The
                // value is marked where it is; anything else goes to the element's start tag.
                (from, to) = ElementValue().IsMatch(exception.Message) && reading.LastText.From >= 0
                    ? reading.LastText
                    : RangeOf(content, positions, starts.Peek());
            }

            findings.Add(new TextFinding(
                from,
                to,
                args.Severity == XmlSeverityType.Error ? TextFindingSeverity.Error : TextFindingSeverity.Warning,
                Sentence(exception.Message),
                Source));
        }

        try
        {
            using var validating = XmlReaders.Document(content, fullPath, resolver, settings =>
            {
                if (schemas is null)
                {
                    settings.ValidationType = ValidationType.DTD;
                }
                else
                {
                    settings.ValidationType = ValidationType.Schema;
                    settings.Schemas = schemas;
                    settings.ValidationFlags = XmlSchemaValidationFlags.ReportValidationWarnings | XmlSchemaValidationFlags.ProcessIdentityConstraints;
                }

                settings.ValidationEventHandler += OnEvent;
            });
            reading.Reader = validating;
            var lines = (IXmlLineInfo)validating;
            while (validating.Read())
            {
                switch (validating.NodeType)
                {
                    case XmlNodeType.Element:
                        reading.LastText = (-1, -1);
                        if (!validating.IsEmptyElement)
                        {
                            starts.Push(positions.Offset(lines.LineNumber, lines.LinePosition));
                        }

                        break;
                    case XmlNodeType.Text:
                        var textFrom = positions.Offset(lines.LineNumber, lines.LinePosition);
                        var textTo = content.IndexOf('<', textFrom);
                        reading.LastText = Trimmed(content, textFrom, textTo < 0 ? content.Length : textTo);
                        break;
                }

                // Popped after the read that raised the end tag's events has returned, so an
                // event at the end tag still finds its element's start on top.
                if (validating.NodeType == XmlNodeType.EndElement && starts.Count > 0)
                {
                    starts.Pop();
                    reading.LastText = (-1, -1);
                }
            }
        }
        catch (XmlException)
        {
            // Well-formedness reported the reason already; validation stops where reading did.
        }

        return findings;
    }

    /// <summary>
    /// Where the reader is, for the validation events it raises: the events are handled inside a
    /// read, and need the node being read and the text last seen in the element.
    /// </summary>
    private sealed class Reading
    {
        public XmlReader? Reader { get; set; }

        public (int From, int To) LastText { get; set; } = (-1, -1);
    }

    /// <summary>The element name at <paramref name="offset"/>, past a <c>&lt;</c> or <c>&lt;/</c> the platform may point at.</summary>
    private static (int From, int To) RangeOf(string content, TextPositions positions, int offset)
    {
        while (offset < content.Length && content[offset] is '<' or '/')
        {
            offset++;
        }

        return positions.RangeAt(offset);
    }

    /// <summary>The value of the attribute named <paramref name="name"/> in the start tag around <paramref name="offset"/>.</summary>
    private static (int From, int To)? ValueAfter(string content, int offset, string name)
    {
        var tagStart = content.LastIndexOf('<', Math.Min(offset, content.Length - 1));
        var tagEnd = content.IndexOf('>', Math.Max(tagStart, 0));
        if (tagStart < 0 || tagEnd < 0)
        {
            return null;
        }

        var local = name.Contains(':', StringComparison.Ordinal) ? name[(name.IndexOf(':', StringComparison.Ordinal) + 1)..] : name;
        var match = Regex.Match(content[tagStart..tagEnd], $"""(?:^|\s)(?:[\w.-]+:)?{Regex.Escape(local)}\s*=\s*(["'])(?<value>[^"']*)\1""");
        return match.Success ? (tagStart + match.Groups["value"].Index, tagStart + match.Groups["value"].Index + match.Groups["value"].Length) : null;
    }

    private static (int From, int To) Trimmed(string content, int from, int to)
    {
        while (from < to && char.IsWhiteSpace(content[from]))
        {
            from++;
        }

        while (to > from && char.IsWhiteSpace(content[to - 1]))
        {
            to--;
        }

        return (from, to);
    }

    private static string Sentence(string message) => TrailingPosition().Replace(message, "").Trim();

    [GeneratedRegex(@"\s*Line \d+, position \d+\.\s*$")]
    private static partial Regex TrailingPosition();

    [GeneratedRegex("^The '(?<name>[^']+)' attribute is invalid")]
    private static partial Regex AttributeValue();

    [GeneratedRegex("^The '[^']+' element is invalid")]
    private static partial Regex ElementValue();
}
