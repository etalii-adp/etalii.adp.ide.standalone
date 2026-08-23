using System.Text;
using System.Xml.Linq;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// Writes an <see cref="XDocument"/> the way Freeplane writes a map, which the framework's
/// <see cref="System.Xml.XmlWriter"/> cannot: no XML declaration, <c>/&gt;</c> with no space
/// before it, attributes in the order they were read, and whitespace text exactly as loaded.
/// With the document parsed under <see cref="LoadOptions.PreserveWhitespace"/>, a map that
/// was not edited comes back byte for byte (Requirement 3.3).
/// </summary>
/// <remarks>
/// What it escapes is the minimum XML requires plus what Freeplane escapes: <c>&amp;</c>,
/// <c>&lt;</c> and <c>&gt;</c> everywhere, <c>&quot;</c> and a newline inside an attribute.
/// Anything else - accents, dashes, emoji - is written as itself in UTF-8, which is what a
/// Freeplane-saved file contains. If a real save turns out to differ (see the fixture readme),
/// this is the one place to teach it.
/// </remarks>
internal static class FreeplaneXmlWriter
{
    public static void Write(XDocument document, StringBuilder output)
    {
        foreach (var node in document.Nodes())
        {
            WriteNode(node, output);
        }
    }

    private static void WriteNode(XNode node, StringBuilder output)
    {
        switch (node)
        {
            case XElement element:
                WriteElement(element, output);
                break;

            case XText text:
                AppendEscapedText(text.Value, output);
                break;

            case XComment comment:
                output.Append("<!--").Append(comment.Value).Append("-->");
                break;

            case XProcessingInstruction instruction:
                output.Append("<?").Append(instruction.Target).Append(' ').Append(instruction.Data).Append("?>");
                break;

            case XDocumentType documentType:
                output.Append("<!DOCTYPE ").Append(documentType.Name).Append('>');
                break;
        }
    }

    private static void WriteElement(XElement element, StringBuilder output)
    {
        output.Append('<').Append(element.Name.LocalName);
        foreach (var attribute in element.Attributes())
        {
            output.Append(' ').Append(attribute.Name.LocalName).Append("=\"");
            AppendEscapedAttribute(attribute.Value, output);
            output.Append('"');
        }

        if (!element.Nodes().Any())
        {
            output.Append("/>");
            return;
        }

        output.Append('>');
        foreach (var child in element.Nodes())
        {
            WriteNode(child, output);
        }

        output.Append("</").Append(element.Name.LocalName).Append('>');
    }

    private static void AppendEscapedText(string value, StringBuilder output)
    {
        foreach (var character in value)
        {
            switch (character)
            {
                case '&': output.Append("&amp;"); break;
                case '<': output.Append("&lt;"); break;
                case '>': output.Append("&gt;"); break;
                default: output.Append(character); break;
            }
        }
    }

    private static void AppendEscapedAttribute(string value, StringBuilder output)
    {
        foreach (var character in value)
        {
            switch (character)
            {
                case '&': output.Append("&amp;"); break;
                case '<': output.Append("&lt;"); break;
                case '>': output.Append("&gt;"); break;
                case '"': output.Append("&quot;"); break;
                case '\n': output.Append("&#xa;"); break;
                default: output.Append(character); break;
            }
        }
    }
}
