using EtAlii.Adp.Files;
using EtAlii.Adp.Sessions;
using JetBrains.Annotations;

namespace EtAlii.Adp.Editor.Json;

/// <summary>Opens JSON sessions - the same contract every editor offers (json-editor Requirement 8).</summary>
[UsedImplicitly] // Made by the container, which gives it the project roots' event sources.
public sealed class JsonEditorSessionFactory(ProjectFileEvents? events = null) : IEditorSessionFactory
{
    public string EditorId => Editor.Json.Id;

    public IEditorSession Open(ShortGuid watchId, string rootPath, string filePath) => new JsonEditorSession(filePath, events);
}
