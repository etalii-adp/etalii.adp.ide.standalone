using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// What every mindmap command handler does before its own work: find the document, find the
/// node, answer a missing one with a message rather than an exception (Requirement 6.3). A
/// handler re-checks all of this on every run because undo and redo dispatch it again long
/// after the click, against whatever the map is by then (Requirement 6.4).
/// </summary>
/// <remarks>
/// Not on <see cref="DocumentEditHandler{TCommand, TStore, TBody, TModel}"/> (code-structure FR-033):
/// that base edits a body whose model is read from it and announces only a path, where a map is one
/// tree and each save names the structural change it made. A handler's result is still
/// <see cref="CommandResult.FromSave"/>, so a warning of the save reaches it as it does on the base.
/// </remarks>
internal abstract class MindmapCommandHandler
{
    protected MindmapCommandHandler(IMindmapDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        Documents = documents;
    }

    protected IMindmapDocumentStore Documents { get; }

    /// <summary>The document and node a command names, or the failure to hand back when either is gone.</summary>
    protected async ValueTask<Resolution> ResolveAsync(string bodyPath, string nodeId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bodyPath))
        {
            return new Resolution(CommandResult.Failure("No map was given."));
        }

        MindmapDocument document;
        try
        {
            document = await Documents.GetOrLoadAsync(bodyPath, cancellationToken);
        }
        catch (MindmapFormatException exception)
        {
            return new Resolution(CommandResult.Failure(exception.Message));
        }

        return document.Find(nodeId) is { } node
            ? new Resolution(null, document, node)
            : new Resolution(CommandResult.Failure("The node no longer exists."));
    }

    /// <summary>What <see cref="ResolveAsync"/> found: the document and node, or the failure when either is gone.</summary>
    protected sealed record Resolution(CommandResult? Failure, MindmapDocument Document = null!, MindmapNode Node = null!);
}
