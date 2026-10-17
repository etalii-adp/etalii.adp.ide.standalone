using EtAlii.Adp.Files;

namespace EtAlii.Adp.Editor.Plain;

/// <summary>Opens plain-text sessions; the whole family's contract, offered for any file (Requirement 3.3).</summary>
public sealed class PlainEditorSessionFactory(ProjectFileEvents? events = null) : IEditorSessionFactory
{
    public string EditorId => Editor.Plain.Id;

    public IEditorSession Open(ShortGuid watchId, string rootPath, string filePath) => new TextEditorSession(filePath, events);
}
