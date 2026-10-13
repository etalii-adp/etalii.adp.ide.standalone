using System.Text.RegularExpressions;
using System.Xml;

namespace EtAlii.Adp.Editor.Xml;

/// <summary>A schema a document names: its namespace (empty for none), where it is, and where the name stands in the text.</summary>
internal sealed record SchemaReference(string Namespace, string Location, int From, int To);

/// <summary>
/// Which schemas a document names, read from its prolog and its root's start tag only
/// (xml-editor Requirement 5): <c>xsi:noNamespaceSchemaLocation</c>, the pairs of
/// <c>xsi:schemaLocation</c>, and the <c>DOCTYPE</c>'s system identifier with its internal subset.
/// An <c>xml-model</c> instruction is named and not supported; a root that is <c>xs:schema</c>
/// makes the document a schema itself.
/// </summary>
internal sealed partial record SchemaAssociation(
    IReadOnlyList<SchemaReference> Xsd,
    SchemaReference? Dtd,
    bool HasDoctype,
    bool IsSchema,
    IReadOnlyList<TextFinding> Unsupported)
{
    private const string XmlSchemaNamespace = "http://www.w3.org/2001/XMLSchema";
    private const string InstanceNamespace = "http://www.w3.org/2001/XMLSchema-instance";

    private static readonly Dictionary<string, string> SchemaLanguages = new(StringComparer.Ordinal)
    {
        ["http://relaxng.org/ns/structure/1.0"] = "RELAX NG",
        ["http://purl.oclc.org/dsdl/schematron"] = "Schematron",
        ["http://www.ascc.net/xml/schematron"] = "Schematron",
        [XmlSchemaNamespace] = "XSD named by xml-model",
    };

    /// <summary>Whether any schema, XSD or DTD, is named.</summary>
    public bool NamesASchema => Xsd.Count > 0 || Dtd is not null || HasDoctype;

    public static SchemaAssociation Read(string content, string fullPath, ProjectXmlResolver resolver, TextPositions positions)
    {
        var xsd = new List<SchemaReference>();
        var unsupported = new List<TextFinding>();
        var isSchema = false;

        var doctype = Doctype().Match(content);
        SchemaReference? dtd = null;
        if (doctype.Success && doctype.Groups["system"].Success)
        {
            var system = doctype.Groups["system"];
            dtd = new SchemaReference("", system.Value, system.Index, system.Index + system.Length);
        }

        try
        {
            // The DTD is not read here: only the names the prolog and the root's start tag hold
            // are wanted, and a DOCTYPE was found above by its text.
            using var reader = XmlReaders.Document(content, fullPath, resolver, settings => settings.DtdProcessing = DtdProcessing.Ignore);
            var lines = (IXmlLineInfo)reader;
            while (reader.Read())
            {
                if (reader is { NodeType: XmlNodeType.ProcessingInstruction, Name: "xml-model" })
                {
                    var start = positions.Offset(lines.LineNumber, lines.LinePosition);
                    unsupported.Add(UnsupportedFinding(reader.Value, start, start + reader.Name.Length));
                }

                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                isSchema = reader is { LocalName: "schema", NamespaceURI: XmlSchemaNamespace };
                if (reader.MoveToAttribute("noNamespaceSchemaLocation", InstanceNamespace))
                {
                    (int from, int to) = ValueRange(content, positions.Offset(lines.LineNumber, lines.LinePosition));
                    xsd.Add(new SchemaReference("", content[from..to].Trim(), from, to));
                }

                if (reader.MoveToAttribute("schemaLocation", InstanceNamespace))
                {
                    (int from, int to) = ValueRange(content, positions.Offset(lines.LineNumber, lines.LinePosition));
                    var words = Word().Matches(content[from..to]).ToArray();
                    for (var index = 0; index + 1 < words.Length; index += 2)
                    {
                        var location = words[index + 1];
                        xsd.Add(new SchemaReference(words[index].Value, location.Value, from + location.Index, from + location.Index + location.Length));
                    }
                }

                break;
            }
        }
        catch (XmlException)
        {
            // What was read before the text stopped is what is known; well-formedness says why.
        }

        return new SchemaAssociation(xsd, dtd, doctype.Success, isSchema, unsupported);
    }

    private static TextFinding UnsupportedFinding(string instruction, int from, int to)
    {
        var type = PseudoAttribute().Matches(instruction)
            .FirstOrDefault(match => match.Groups["name"].Value == "schematypens")?.Groups["value"].Value ?? "";
        var language = SchemaLanguages.GetValueOrDefault(type, type.Length > 0 ? type : "a schema");
        return new TextFinding(from, to, TextFindingSeverity.Info, $"An xml-model instruction names {language}, which this editor does not check: it checks XSD 1.0 and DTD named by the document.", WellFormedness.Source);
    }

    /// <summary>The range of an attribute's value, given the offset of the attribute's name.</summary>
    private static (int From, int To) ValueRange(string content, int nameOffset)
    {
        var equals = content.IndexOf('=', nameOffset);
        var quote = equals < 0 ? -1 : content.IndexOfAny(['"', '\''], equals);
        if (quote < 0)
        {
            return (nameOffset, nameOffset);
        }

        var end = content.IndexOf(content[quote], quote + 1);
        return (quote + 1, end < 0 ? content.Length : end);
    }

    [GeneratedRegex("""<!DOCTYPE\s+[^\s\[>]+(?:\s+(?:SYSTEM|PUBLIC\s+(["'])[^"']*\1)\s+(["'])(?<system>[^"']*)\2)?""")]
    private static partial Regex Doctype();

    [GeneratedRegex(@"\S+")]
    private static partial Regex Word();

    [GeneratedRegex("""(?<name>[\w:-]+)\s*=\s*(?<quote>["'])(?<value>[^"']*)\k<quote>""")]
    private static partial Regex PseudoAttribute();
}
