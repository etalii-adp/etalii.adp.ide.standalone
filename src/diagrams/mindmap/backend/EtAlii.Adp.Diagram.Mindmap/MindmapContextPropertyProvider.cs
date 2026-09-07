using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Context;
using EtAlii.Adp.History;
using Serilog;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// What the property grid shows for a selected mindmap node, and what happens when one of
/// those values is edited.
/// </summary>
/// <remarks>
/// The same commands the context actions use, reached a shorter way: the grid's Text row and
/// "Rename…" are one edit and one undo entry. Nothing here writes to the document - every
/// change goes through the project's history like every other edit.
/// </remarks>
public sealed class MindmapContextPropertyProvider : IContextPropertyProvider
{
    public const string TextPropertyId = "mindmap.text";
    public const string NotesPropertyId = "mindmap.notes";
    public const string LinkPropertyId = "mindmap.link";
    public const string FoldedPropertyId = "mindmap.folded";
    public const string IdentifierPropertyId = "mindmap.identifier";

    private const string Gone = "That node is no longer in this mindmap.";

    private static readonly ILogger _logger = Log.ForContext<MindmapContextPropertyProvider>();

    private readonly IHistoryStackStore _historyStacks;
    private readonly IMindmapDocumentStore _documents;

    public MindmapContextPropertyProvider(IHistoryStackStore historyStacks, IMindmapDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(historyStacks);
        ArgumentNullException.ThrowIfNull(documents);
        _historyStacks = historyStacks;
        _documents = documents;
    }

    public ContextScope Scope => ContextScope.DiagramElement;

    public ValueTask<IReadOnlyList<ContextPropertyDefinition>> DescribeAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (Resolve(target) is not { } node)
        {
            return ValueTask.FromResult<IReadOnlyList<ContextPropertyDefinition>>([]);
        }

        var properties = new List<ContextPropertyDefinition>
        {
            new(TextPropertyId, "Text", node.Text),
            new(NotesPropertyId, "Notes", node.Notes, ContextPropertyEditor.Text),
            new(LinkPropertyId, "Link", node.Link ?? ""),
        };

        // Shown, and deliberately not editable here. Folding is per-connection view state: it
        // writes nothing and lands on no history (Requirements 9.4, 9.6), so a grid that
        // promises every edit is one undo away must not offer it. Worth showing all the same -
        // a reader looking at a node with hidden children wants to know that is why.
        if (node.HasChildren)
        {
            properties.Add(new ContextPropertyDefinition(
                FoldedPropertyId,
                "Collapsed",
                node.Folded ? "Yes" : "No",
                ContextPropertyEditor.Line,
                "Collapsing is a view setting rather than part of the document, so it is not undoable. Collapse from the canvas or the context menu.",
                "View"));
        }

        properties.Add(new ContextPropertyDefinition(
            IdentifierPropertyId,
            "Identifier",
            node.Id,
            ContextPropertyEditor.Line,
            "Freeplane gives every node its identifier and other files may link to it, so it is not ours to change.",
            "Model"));

        return ValueTask.FromResult<IReadOnlyList<ContextPropertyDefinition>>(properties);
    }

    public async ValueTask<ContextPropertyResult> SetAsync(
        ContextTarget target,
        string propertyId,
        string value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (Resolve(target) is not { } node)
        {
            return ContextPropertyResult.Failure(Gone);
        }

        var bodyPath = target.ResolvedFullPath;
        ICommand? command = propertyId switch
        {
            TextPropertyId => new SetNodeTextCommand(bodyPath, node.Id, value),
            NotesPropertyId => new SetNodeNotesCommand(bodyPath, node.Id, value),
            // An emptied link is an unlink, which is the same command with nothing in it rather
            // than a second one - so undo puts the old link back either way.
            LinkPropertyId => new SetNodeLinkCommand(bodyPath, node.Id, value.Length == 0 ? null : value),
            _ => null,
        };

        if (command is null)
        {
            return ContextPropertyResult.Failure($"'{propertyId}' is not a property of what is selected.");
        }

        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? ContextPropertyResult.Success : ContextPropertyResult.Failure(result.Error);
    }

    private MindmapNode? Resolve(ContextTarget target)
    {
        if (target.Scope != ContextScope.DiagramElement || target.ElementId.Length == 0)
        {
            return null;
        }

        // A provider is consulted for every diagram element in its scope, including other
        // types'; a file this module does not own is not its to parse.
        if (!target.ResolvedFullPath.EndsWith(Diagram.DocumentExtension, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return _documents.GetOrLoad(target.ResolvedFullPath).Find(target.ElementId);
        }
        catch (MindmapFormatException exception)
        {
            _logger.Warning(exception, "Cannot describe properties on {BodyPath}", target.ResolvedFullPath);
            return null;
        }
    }
}
