namespace EtAlii.Adp.Editor.Xml;

/// <summary>Opens xml sessions - the same contract every editor offers (Requirement 10.2).</summary>
public sealed class XmlEditorSessionFactory : IEditorSessionFactory
{
    public string EditorId => Editor.Xml.Id;

    public IEditorSession Open(ShortGuid watchId, string rootPath, string filePath) => new XmlEditorSession(filePath);
}
