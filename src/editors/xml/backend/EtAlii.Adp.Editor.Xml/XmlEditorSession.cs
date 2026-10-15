namespace EtAlii.Adp.Editor.Xml;

/// <summary>
/// One open XML file: a <see cref="TextEditorSession"/>, everything the plain editor does. What
/// makes XML worth its own module - colour, well-formedness, the schema - is the client's grammar
/// and the module's <see cref="IEditorAnalyzer"/>; this session holds nothing XML-specific, so a
/// file the reader would decode differently is still given back byte for byte (xml-editor
/// Requirement 8.1). A type of its own so its records name XML.
/// </summary>
public sealed class XmlEditorSession(string path) : TextEditorSession(path);
