using JetBrains.Annotations;

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

    IEditorSession Open(
        [UsedImplicitly] ShortGuid watchId, // The module seam docs/creating-an-editor-module.md documents, mirroring IDiagramSessionFactory.Open; DiagramService.Open passes it, no editor reads it yet.
        [UsedImplicitly] string rootPath, // Same seam: the root the interface's summary promises a module, which no editor reads yet.
        string filePath);
}
