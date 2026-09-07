using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>Opens a <see cref="MindmapSession"/> per connection - the module's fourth registration seam.</summary>
public sealed class MindmapSessionFactory : IDiagramSessionFactory
{
    private readonly IMindmapDocumentStore _documents;
    private readonly MindmapViewState _views;
    private readonly MindmapElementMapper _mapper;
    private readonly Common.IHistoryStackStore _historyStacks;

    public MindmapSessionFactory(IMindmapDocumentStore documents, MindmapViewState views, MindmapElementMapper mapper, Common.IHistoryStackStore historyStacks)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(views);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(historyStacks);
        _documents = documents;
        _views = views;
        _mapper = mapper;
        _historyStacks = historyStacks;
    }

    public DiagramOrigin Origin => Diagram.Mindmap.Origin;

    public IDiagramSession Open(ShortGuid watchId, string rootPath, string bodyPath, string? registrationPath) =>
        // The project's history, so a drag-move on this session's canvas is one undo away.
        new MindmapSession(watchId, bodyPath, _documents, _views, _mapper, _historyStacks.Get(rootPath));
}
