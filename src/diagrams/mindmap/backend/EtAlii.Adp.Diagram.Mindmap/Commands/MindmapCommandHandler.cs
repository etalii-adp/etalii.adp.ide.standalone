using EtAlii.Adp.Backend;


using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// What every mindmap command handler does before its own work: find the document, find the
/// node, answer a missing one with a message rather than an exception (Requirement 6.3). A
/// handler re-checks all of this on every run because undo and redo dispatch it again long
/// after the click, against whatever the map is by then (Requirement 6.4).
/// </summary>
internal abstract class MindmapCommandHandler
{
    protected MindmapCommandHandler(IMindmapDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        Documents = documents;
    }

    protected IMindmapDocumentStore Documents { get; }

    /// <summary>The document and node a command names, or the failure to hand back when either is gone.</summary>
    protected bool TryResolve(string bodyPath, string nodeId, out MindmapDocument document, out MindmapNode node, out CommandResult failure)
    {
        node = null!;
        document = null!;

        if (string.IsNullOrWhiteSpace(bodyPath))
        {
            failure = CommandResult.Failure("No map was given.");
            return false;
        }

        try
        {
            document = Documents.GetOrLoad(bodyPath);
        }
        catch (MindmapFormatException exception)
        {
            failure = CommandResult.Failure(exception.Message);
            return false;
        }

        var found = document.Find(nodeId);
        if (found is null)
        {
            failure = CommandResult.Failure("The node no longer exists.");
            return false;
        }

        node = found;
        failure = null!;
        return true;
    }
}
