namespace EtAlii.Adp.Editor;

/// <summary>
/// How a module opens its sessions, keyed by the editor's own id - never by a diagram
/// concept. Core resolves which factory serves a file; the module never learns the project
/// layout beyond the root and path it is handed.
/// </summary>
public interface IEditorSessionFactory
{
    /// <summary>The <see cref="EditorDefinition.Id"/> this factory serves.</summary>
    string EditorId { get; }

    IEditorSession Open(ShortGuid watchId, string rootPath, string filePath);
}
