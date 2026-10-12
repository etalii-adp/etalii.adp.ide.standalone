namespace EtAlii.Adp.Editor.Json;

/// <summary>Opens JSON sessions - the same contract every editor offers (json-editor Requirement 8).</summary>
public sealed class JsonEditorSessionFactory : IEditorSessionFactory
{
    public string EditorId => Editor.Json.Id;

    public IEditorSession Open(ShortGuid watchId, string rootPath, string filePath) => new JsonEditorSession(filePath);
}
