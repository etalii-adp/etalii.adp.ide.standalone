using EtAlii.Adp.Files;
using JetBrains.Annotations;

namespace EtAlii.Adp.Editor.Xml;

/// <summary>Opens xml sessions - the same contract every editor offers (Requirement 10.2).</summary>
[UsedImplicitly] // Made by the container, which gives it the project roots' event sources.
public sealed class XmlEditorSessionFactory(ProjectFileEvents? events = null) : IEditorSessionFactory
{
    public string EditorId => Editor.Xml.Id;

    public IEditorSession Open(ShortGuid watchId, string rootPath, string filePath) => new XmlEditorSession(filePath, events);
}
