using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Editor.Json;

/// <summary>
/// One open JSON file: a <see cref="TextEditorSession"/>, the family's contract and nothing
/// JSON-specific (json-editor Requirement 8). Colour, findings, the schema and completion are all
/// client-side. A type of its own so its records name JSON.
/// </summary>
public sealed class JsonEditorSession(string path, ProjectFileEvents? events = null) : TextEditorSession(path, events);
