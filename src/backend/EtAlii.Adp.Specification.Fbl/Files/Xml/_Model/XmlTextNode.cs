namespace EtAlii.Adp.Specification.Fbl.Xml;

/// <summary>The text of an element as a slot reads it: where it is written, and whether it is html paragraphs.</summary>
internal sealed record XmlTextNode(XmlElement Element, Span? Span, bool Html);
