using EtAlii.Adp.Specification.Fbl.Links;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// A diagram as the workspace's index holds it (DISL 0.12 §4.12, FBL 17.3): each element of a linkable type with its attributes
/// of a text type, its name property first, and the links its attributes of type <c>link</c> hold, each by the element that
/// holds it.
/// </summary>
public static class DislLinks
{
    /// <summary>
    /// <paramref name="diagram"/>, read from the body at <paramref name="path"/> with the document id <paramref name="documentId"/>
    /// (null while it holds none), as the index holds it.
    /// </summary>
    public static IndexedDocument Indexed(DislDiagram diagram, string path, string? documentId)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        ArgumentNullException.ThrowIfNull(path);

        var elements = new List<IndexedElement>();
        string? nameProperty = null;
        foreach (var element in diagram.Nodes)
        {
            if (element.IsDerived || !element.Type.Linkable) continue;
            nameProperty ??= element.Type.NameProperty;
            var names = element.Type.Attributes.Values.Where(Names).Select(attribute => attribute.Name).ToList();
            if (element.Type.NameProperty is { } named && names.Remove(named)) names.Insert(0, named);
            elements.Add(new IndexedElement(element.Id, [.. names.Select(name => new KeyValuePair<string, string>(name, element.ValueOf(name) as string ?? ""))]));
        }

        var links = new List<HeldLink>();
        foreach (var element in diagram.Elements)
        {
            if (element.IsDerived) continue;
            foreach (var attribute in element.Type.Attributes.Values.Where(attribute => attribute.Type == "link"))
            {
                foreach (var text in Texts(element.ValueOf(attribute.Name))) links.Add(new HeldLink(element.Id, text));
            }
        }
        return new IndexedDocument(path, documentId, nameProperty, elements, links);
    }

    /// <summary>Whether an attribute holds a text an element can be named by (FBL 17.1): a single string or text.</summary>
    private static bool Names(DislAttribute attribute) => !attribute.Many && attribute.Type is "string" or "text";

    /// <summary>The links a value holds: itself when it is a text, the texts of a list, none when it is empty.</summary>
    private static IEnumerable<string> Texts(object? value) => value switch
    {
        string { Length: > 0 } text => [text],
        IEnumerable<object?> items => items.OfType<string>().Where(text => text.Length > 0),
        _ => [],
    };
}
