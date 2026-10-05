using EtAlii.Adp.Specification.Fbl.Rules;

namespace EtAlii.Adp.Specification.Fbl.Xml;

/// <summary>An element (FBL §4.5): an entry whose own span runs from its start tag's <c>&lt;</c> to its end tag's <c>&gt;</c>.</summary>
internal sealed class XmlElement : Entry
{
    public bool IsRoot { get; init; }

    public Span StartTag { get; set; }

    /// <summary>The end of the element's name in its start tag, where a first attribute is added.</summary>
    public int NameEnd { get; init; }

    /// <summary>The <c>&gt;</c> that ends the start tag, or the <c>/&gt;</c> of a self-closed tag.</summary>
    public Span Close { get; set; }

    public bool SelfClosed { get; set; }

    public Span? EndTag { get; set; }

    public int ContentStart => StartTag.End;

    public int ContentEnd => EndTag?.Start ?? StartTag.End;

    public List<XmlAttribute> Attributes { get; } = [];

    /// <summary>Text runs and child elements in document order.</summary>
    public List<object> Content { get; } = [];

    /// <summary>A reference to an entity other than the five predefined ones: the element is an unreadable entry.</summary>
    public string? Unreadable { get; set; }

    public IEnumerable<XmlElement> Elements => Content.OfType<XmlElement>();

    public XmlAttribute? Attribute(string name) => Attributes.FirstOrDefault(a => a.Name == name);
}
