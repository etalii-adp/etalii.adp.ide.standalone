namespace EtAlii.Adp.Specification.Fbl.Xml;

/// <summary>Character data of an element, with its span and its text with references decoded.</summary>
internal sealed record XmlTextRun(Span Span, string Text);
