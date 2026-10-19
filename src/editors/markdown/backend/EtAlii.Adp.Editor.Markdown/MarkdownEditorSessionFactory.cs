using EtAlii.Adp.Files;
using EtAlii.Adp.Sessions;
using JetBrains.Annotations;

namespace EtAlii.Adp.Editor.Markdown;

/// <summary>Opens markdown sessions - the same contract every editor offers (Requirement 10.2).</summary>
[UsedImplicitly] // Made by the container, which gives it the project roots' event sources.
public sealed class MarkdownEditorSessionFactory(ProjectFileEvents? events = null) : IEditorSessionFactory
{
    public string EditorId => Editor.Markdown.Id;

    public IEditorSession Open(ShortGuid watchId, ProjectPath file) => new MarkdownEditorSession(file.FullPath, events);
}
