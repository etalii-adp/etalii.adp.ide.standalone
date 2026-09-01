namespace EtAlii.Adp.Editor.Markdown;

/// <summary>Opens markdown sessions - the same contract every editor offers (Requirement 10.2).</summary>
public sealed class MarkdownEditorSessionFactory : IEditorSessionFactory
{
    public string EditorId => Editor.Markdown.Id;

    public IEditorSession Open(ShortGuid watchId, string rootPath, string filePath) => new MarkdownEditorSession(filePath);
}
