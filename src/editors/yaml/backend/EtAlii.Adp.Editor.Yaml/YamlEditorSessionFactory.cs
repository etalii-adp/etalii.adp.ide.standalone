using EtAlii.Adp.Files;
using JetBrains.Annotations;

namespace EtAlii.Adp.Editor.Yaml;

/// <summary>Opens plain-text sessions; the whole family's contract, offered for any file (Requirement 3.3).</summary>
[UsedImplicitly] // Made by the container, which gives it the project roots' event sources.
public sealed class YamlEditorSessionFactory(ProjectFileEvents? events = null) : IEditorSessionFactory
{
    public string EditorId => Editor.Yaml.Id;

    public IEditorSession Open(ShortGuid watchId, string rootPath, string filePath) => new YamlEditorSession(filePath, events);
}
