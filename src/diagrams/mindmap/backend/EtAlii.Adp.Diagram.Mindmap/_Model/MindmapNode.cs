using System.Xml.Linq;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// One node of a mindmap, as a view over its <c>&lt;node&gt;</c> element. Reading goes to the
/// element and writing goes to the element, so the XML the document was parsed from is the
/// single source of truth and saving is "write it back" rather than "regenerate it" - which
/// is what keeps everything ADP does not understand exactly as it was (Requirement 3.2).
/// </summary>
public sealed class MindmapNode
{
    internal const string ElementName = "node";
    internal const string IdAttribute = "ID";
    internal const string TextAttribute = "TEXT";
    internal const string LinkAttribute = "LINK";
    internal const string FoldedAttribute = "FOLDED";
    internal const string PositionAttribute = "POSITION";
    private const string RichContentElement = "richcontent";
    private const string RichContentTypeAttribute = "TYPE";
    private const string NoteType = "NOTE";
    private const string NodeTextType = "NODE";

    internal MindmapNode(XElement element)
    {
        Element = element;
    }

    internal XElement Element { get; }

    /// <summary>The node's <c>ID</c> attribute, which every node has once the document is loaded (Requirement 3.4).</summary>
    public string Id => Element.Attribute(IdAttribute)?.Value ?? "";

    /// <summary>
    /// The node's primary text. Freeplane keeps it in the <c>TEXT</c> attribute, or - for a
    /// node whose text is formatted - as an HTML body in a <c>richcontent TYPE="NODE"</c>
    /// child; either way this is the plain text a user reads.
    /// </summary>
    public string Text
    {
        get
        {
            var rich = RichContent(NodeTextType);
            return rich is not null ? PlainTextOf(rich) : Element.Attribute(TextAttribute)?.Value ?? "";
        }
    }

    /// <summary>Free-form notes, from the <c>richcontent TYPE="NOTE"</c> child; empty when there are none.</summary>
    public string Notes
    {
        get
        {
            var rich = RichContent(NoteType);
            return rich is null ? "" : PlainTextOf(rich);
        }
    }

    /// <summary>Whether the file says this branch starts folded - the seed for per-connection fold state (Requirement 9.3).</summary>
    public bool Folded => string.Equals(Element.Attribute(FoldedAttribute)?.Value, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>The <c>LINK</c> attribute exactly as stored - map-relative, per Freeplane - or null when the node is not linked.</summary>
    public string? Link => Element.Attribute(LinkAttribute)?.Value;

    /// <summary>Freeplane's <c>POSITION</c> on a first-level node: <c>left</c> or <c>right</c>, or null when unspecified.</summary>
    public string? Position => Element.Attribute(PositionAttribute)?.Value;

    public MindmapNode? Parent
    {
        get
        {
            var parent = Element.Parent;
            return parent is not null && parent.Name.LocalName == ElementName ? new MindmapNode(parent) : null;
        }
    }

    public IReadOnlyList<MindmapNode> Children =>
        Element.Elements(ElementName).Select(child => new MindmapNode(child)).ToArray();

    public bool HasChildren => Element.Elements(ElementName).Any();

    public bool IsRoot => Parent is null;

    /// <summary>This node's index among its parent's children, or 0 for the root.</summary>
    public int IndexInParent =>
        Element.ElementsBeforeSelf(ElementName).Count();

    // ---- edits: each writes the element, which is what the document saves ----------------

    internal void SetText(string text)
    {
        // Plain text replaces formatted text outright: the user typed a string, and keeping a
        // stale HTML body beside it would have Freeplane show the old text.
        RichContent(NodeTextType)?.Remove();
        Element.SetAttributeValue(TextAttribute, text);
    }

    internal void SetNotes(string notes)
    {
        var existing = RichContent(NoteType);
        if (notes.Length == 0)
        {
            if (existing is not null)
            {
                // With the newline that followed it, so a node whose only content was the
                // note collapses back to the self-closing form it had before.
                if (existing.NextNode is XText { Value: var trailing } text && trailing.All(char.IsWhiteSpace))
                {
                    text.Remove();
                }

                existing.Remove();

                // Only whitespace left means the note was the node's only content: drop the
                // whitespace too, so it is self-closing again rather than an empty pair of tags.
                if (Element.Nodes().All(node => node is XText whitespace && whitespace.Value.All(char.IsWhiteSpace)))
                {
                    Element.RemoveNodes();
                }
            }

            return;
        }

        var body = new XElement("body", notes.Split('\n').Select(line => new XElement("p", line)));
        var html = new XElement("html", new XElement("head"), body);
        if (existing is null)
        {
            // Before the first child node, which is where Freeplane puts it - and on its own
            // line, which is how Freeplane lays a node's content out.
            var firstChild = Element.Elements(ElementName).FirstOrDefault();
            var content = new XElement(RichContentElement, new XAttribute(RichContentTypeAttribute, NoteType), html);
            if (firstChild is not null)
            {
                firstChild.AddBeforeSelf(content, new XText("\n"));
            }
            else if (Element.LastNode is XText { Value: var trailing } && trailing.All(char.IsWhiteSpace) && trailing.Contains('\n'))
            {
                Element.Add(content, new XText("\n"));
            }
            else
            {
                Element.Add(new XText("\n"), content, new XText("\n"));
            }
        }
        else
        {
            existing.ReplaceNodes(html);
        }
    }

    internal void SetLink(string? link)
    {
        Element.SetAttributeValue(LinkAttribute, link);
    }

    internal void SetId(string id)
    {
        Element.SetAttributeValue(IdAttribute, id);
    }

    private XElement? RichContent(string type) =>
        Element.Elements(RichContentElement)
            .FirstOrDefault(candidate => string.Equals(candidate.Attribute(RichContentTypeAttribute)?.Value, type, StringComparison.OrdinalIgnoreCase));

    /// <summary>The readable text of an HTML body: one line per paragraph, markup dropped, whitespace tidied.</summary>
    private static string PlainTextOf(XElement richContent)
    {
        var body = richContent.Descendants().FirstOrDefault(element => element.Name.LocalName == "body") ?? richContent;
        var paragraphs = body.Elements().Any(element => element.Name.LocalName == "p")
            ? body.Elements().Where(element => element.Name.LocalName == "p").Select(Collapse)
            : [Collapse(body)];
        return string.Join("\n", paragraphs);
    }

    private static string Collapse(XElement element) =>
        string.Join(" ", element.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
