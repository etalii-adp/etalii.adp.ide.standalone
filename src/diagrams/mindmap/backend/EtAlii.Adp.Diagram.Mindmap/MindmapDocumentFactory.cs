using System.Text;
using System.Xml.Linq;
using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// The empty mindmap a new diagram starts as: one root node named after the file, nothing
/// else (Requirement 1.4). What it emits parses through <see cref="MindmapDocument"/> and
/// opens in Freeplane.
/// </summary>
public sealed class MindmapDocumentFactory : IDiagramDocumentFactory
{
    /// <summary>The version a map written by ADP declares; the newest Freeplane format the parser was written against.</summary>
    private const string FormatVersion = "freeplane 1.11.5";

    public DiagramOrigin Origin => Diagram.Mindmap.Origin;

    public string CreateEmptyDocument(string baseName)
    {
        ArgumentNullException.ThrowIfNull(baseName);

        // Built as XML rather than as a string so the text is escaped once, correctly, by the
        // same writer that saves every other map.
        var root = new XElement(MindmapNode.ElementName);
        root.SetAttributeValue(MindmapNode.TextAttribute, baseName);
        root.SetAttributeValue(MindmapNode.IdAttribute, MindmapDocument.NewId());

        var map = new XElement("map", new XAttribute("version", FormatVersion), new XText("\n"), root, new XText("\n"));
        var output = new StringBuilder();
        FreeplaneXmlWriter.Write(new XDocument(map), output);
        return output.ToString();
    }
}
