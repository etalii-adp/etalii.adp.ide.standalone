namespace EtAlii.Adp.Specification.Fbl.Xml;

/// <summary>An attribute as written (FBL §4.5): its own span from name to closing quote, and its value span between the quotes.</summary>
internal sealed record XmlAttribute(string Name, Span Own, Span Value, string Text);
