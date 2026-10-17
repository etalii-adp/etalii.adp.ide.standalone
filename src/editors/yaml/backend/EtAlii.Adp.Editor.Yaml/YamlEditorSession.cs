using EtAlii.Adp.Files;
using EtAlii.Adp.Sessions;

namespace EtAlii.Adp.Editor.Yaml;

/// <summary>
/// One open YAML file: a <see cref="TextEditorSession"/>, held to the family's contract exactly
/// as the plain editor's session is; nothing here knows YAML. A type of its own so its records
/// name YAML.
/// </summary>
public sealed class YamlEditorSession(string path, ProjectFileEvents? events = null) : TextEditorSession(path, events);
