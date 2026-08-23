using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// A parsed <c>.mm</c> file: the tree of <see cref="MindmapNode"/>s and, behind it, the
/// <see cref="XDocument"/> it came from. Every edit mutates the XML, and saving writes that
/// XML back in Freeplane's own layout, so a file ADP did not change comes back byte for byte
/// and a file it did change differs only where it was changed (Requirements 3.2, 3.3).
/// </summary>
public sealed class MindmapDocument
{
    private const string MapElement = "map";

    private readonly XDocument _xml;
    private readonly bool _endsWithNewline;

    private MindmapDocument(XDocument xml, bool endsWithNewline)
    {
        _xml = xml;
        _endsWithNewline = endsWithNewline;
    }

    /// <summary>
    /// Parses <paramref name="text"/>. With <paramref name="assignMissingIds"/> every node that
    /// has no <c>ID</c> is given one in memory, to be written on the next save - never on open,
    /// which is why this is a flag rather than always on: a round-trip test wants the file back
    /// exactly, and the store wants ids (Requirement 3.4).
    /// </summary>
    /// <exception cref="MindmapFormatException">The text is not a well-formed Freeplane map.</exception>
    public static MindmapDocument Parse(string text, bool assignMissingIds = true)
    {
        ArgumentNullException.ThrowIfNull(text);

        XDocument xml;
        try
        {
            xml = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException exception)
        {
            throw new MindmapFormatException($"The map is not well-formed XML: {exception.Message}", exception);
        }

        if (xml.Root is null || xml.Root.Name.LocalName != MapElement)
        {
            throw new MindmapFormatException("The file does not start with a <map> element.");
        }

        var roots = xml.Root.Elements(MindmapNode.ElementName).Count();
        if (roots != 1)
        {
            throw new MindmapFormatException($"A map has exactly one root node; this one has {roots}.");
        }

        var document = new MindmapDocument(xml, text.EndsWith('\n'));
        if (assignMissingIds)
        {
            document.AssignMissingIds();
        }

        return document;
    }

    /// <summary>The single root node.</summary>
    public MindmapNode Root => new(_xml.Root!.Element(MindmapNode.ElementName)!);

    /// <summary>Every node, root first, in document order.</summary>
    public IEnumerable<MindmapNode> Nodes => _xml.Root!.Descendants(MindmapNode.ElementName).Select(element => new MindmapNode(element));

    /// <summary>The node with <paramref name="id"/>, or null.</summary>
    public MindmapNode? Find(string id) =>
        _xml.Root!.Descendants(MindmapNode.ElementName)
            .Where(element => element.Attribute(MindmapNode.IdAttribute)?.Value == id)
            .Select(element => new MindmapNode(element))
            .FirstOrDefault();

    /// <summary>True when <paramref name="node"/> is <paramref name="ancestor"/> or lies beneath it.</summary>
    public static bool IsWithin(MindmapNode node, MindmapNode ancestor)
    {
        for (var current = node.Element; current is not null; current = current.Parent)
        {
            if (current == ancestor.Element)
            {
                return true;
            }
        }

        return false;
    }

    // ---- structural edits ------------------------------------------------------------------

    /// <summary>Appends a new node as the last child of <paramref name="parent"/> and returns it.</summary>
    public MindmapNode AddChild(MindmapNode parent, string text)
    {
        var element = NewNodeElement(text);
        AppendChild(parent.Element, element);
        return new MindmapNode(element);
    }

    /// <summary>Inserts a new node right after <paramref name="sibling"/> under the same parent and returns it.</summary>
    public MindmapNode AddSibling(MindmapNode sibling, string text)
    {
        if (sibling.IsRoot)
        {
            throw new InvalidOperationException("The root has no siblings.");
        }

        var element = NewNodeElement(text);
        sibling.Element.AddAfterSelf(new XText("\n"), element);
        return new MindmapNode(element);
    }

    /// <summary>
    /// Moves <paramref name="node"/> and everything under it to be the child at
    /// <paramref name="index"/> of <paramref name="newParent"/>.
    /// </summary>
    public void Move(MindmapNode node, MindmapNode newParent, int index)
    {
        if (node.IsRoot)
        {
            throw new InvalidOperationException("The root cannot be moved.");
        }

        if (IsWithin(newParent, node))
        {
            throw new InvalidOperationException("A node cannot be moved into its own subtree.");
        }

        var element = Detach(node.Element);
        InsertAt(newParent.Element, element, index);
    }

    /// <summary>
    /// Removes <paramref name="node"/> with its subtree and returns the detached XML, which
    /// <see cref="Restore"/> puts back exactly - ids, text, notes, links and all
    /// (Requirement 7.4).
    /// </summary>
    public XElement Remove(MindmapNode node)
    {
        if (node.IsRoot)
        {
            throw new InvalidOperationException("The root cannot be removed.");
        }

        return Detach(node.Element);
    }

    /// <summary>Puts a subtree removed by <see cref="Remove"/> back under <paramref name="parent"/> at <paramref name="index"/>.</summary>
    public MindmapNode Restore(XElement subtree, MindmapNode parent, int index)
    {
        InsertAt(parent.Element, subtree, index);
        return new MindmapNode(subtree);
    }

    public void SetText(MindmapNode node, string text) => node.SetText(text);

    public void SetNotes(MindmapNode node, string notes) => node.SetNotes(notes);

    public void SetLink(MindmapNode node, string? link) => node.SetLink(link);

    // ---- serialization -----------------------------------------------------------------------

    /// <summary>The document as Freeplane would write it. See <see cref="FreeplaneXmlWriter"/>.</summary>
    public string ToText()
    {
        var builder = new StringBuilder();
        FreeplaneXmlWriter.Write(_xml, builder);
        if (_endsWithNewline && (builder.Length == 0 || builder[^1] != '\n'))
        {
            builder.Append('\n');
        }

        return builder.ToString();
    }

    // ---- helpers -----------------------------------------------------------------------------

    private void AssignMissingIds()
    {
        foreach (var node in Nodes)
        {
            if (node.Id.Length == 0)
            {
                node.SetId(NewId());
            }
        }
    }

    /// <summary>Freeplane's own ids are <c>ID_</c> plus digits; ours keep the prefix and use a ShortGuid, per tech.md's identity rule.</summary>
    internal static string NewId() => "ID_" + ShortGuid.NewShortGuid();

    private static XElement NewNodeElement(string text)
    {
        var element = new XElement(MindmapNode.ElementName);
        element.SetAttributeValue(MindmapNode.TextAttribute, text);
        element.SetAttributeValue(MindmapNode.IdAttribute, NewId());
        return element;
    }

    /// <summary>
    /// Removes an element together with the newline that follows it, so the layout of the
    /// surviving siblings is what it was - one node per line, nothing doubled up.
    /// </summary>
    private static XElement Detach(XElement element)
    {
        if (element.NextNode is XText { Value: var trailing } text && trailing.All(char.IsWhiteSpace))
        {
            text.Remove();
        }

        element.Remove();
        return element;
    }

    private static void InsertAt(XElement parent, XElement element, int index)
    {
        var children = parent.Elements(MindmapNode.ElementName).ToArray();
        if (index < 0 || index >= children.Length)
        {
            AppendChild(parent, element);
        }
        else
        {
            children[index].AddBeforeSelf(element, new XText("\n"));
        }
    }

    /// <summary>
    /// Freeplane lays child nodes out one per line, each followed by a newline before the
    /// closing tag; a node appended at the end follows that so the file stays readable.
    /// </summary>
    private static void AppendChild(XElement parent, XElement element)
    {
        if (parent.LastNode is XText { Value: var trailing } && trailing.All(char.IsWhiteSpace) && trailing.Contains('\n'))
        {
            parent.Add(element, new XText("\n"));
        }
        else
        {
            parent.Add(new XText("\n"), element, new XText("\n"));
        }
    }
}
