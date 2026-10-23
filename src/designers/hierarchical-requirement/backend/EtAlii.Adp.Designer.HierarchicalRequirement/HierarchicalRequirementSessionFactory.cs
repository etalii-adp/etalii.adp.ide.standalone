using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using EtAlii.Adp.Sessions;

namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// Opens a session per connection for a requirement document, with its registration when it has one (spec 029 R4): each
/// connection has its own focus and narrowing, which are the client's, over the one pair of files.
/// </summary>
/// <remarks>
/// An edit of the text is written through the project's history, which is what makes it one undoable step among the
/// project's others: undo and redo are the application's, not the page's.
/// </remarks>
internal sealed class HierarchicalRequirementSessionFactory(
    IFileWriter writer,
    IHistoryStackStore histories,
    IHierarchicalRequirementDocumentStore documents,
    ProjectFileEvents? events = null) : IDesignerSessionFactory
{
    public string Origin => Designer.Origin;

    public IDesignerSession Open(ShortGuid watchId, ProjectRoot root, string? registrationPath, string bodyPath) =>
        new HierarchicalRequirementSession(bodyPath, registrationPath, writer, command => histories.Get(root).ExecuteAsync(command), documents, events: events);
}
