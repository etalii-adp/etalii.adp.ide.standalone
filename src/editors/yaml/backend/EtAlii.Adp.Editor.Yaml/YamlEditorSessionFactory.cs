namespace EtAlii.Adp.Editor.Yaml;

/// <summary>Opens plain-text sessions; the whole family's contract, offered for any file (Requirement 3.3).</summary>
public sealed class YamlEditorSessionFactory : IEditorSessionFactory
{
    public string EditorId => Editor.Yaml.Id;

    public IEditorSession Open(ShortGuid watchId, string rootPath, string filePath) => new YamlEditorSession(filePath);
}
