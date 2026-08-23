using EtAlii.Adp.Backend.Diagrams;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>Opens a <see cref="MindmapSession"/> per connection - the module's fourth registration seam.</summary>
public sealed class MindmapSessionFactory : IDiagramSessionFactory
{
    private readonly IMindmapDocumentStore _documents;
    private readonly MindmapViewState _views;
    private readonly MindmapElementMapper _mapper;

    public MindmapSessionFactory(IMindmapDocumentStore documents, MindmapViewState views, MindmapElementMapper mapper)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(views);
        ArgumentNullException.ThrowIfNull(mapper);
        _documents = documents;
        _views = views;
        _mapper = mapper;
    }

    public DiagramOrigin Origin => Diagram.Definition.Origin;

    public IDiagramSession Open(ShortGuid watchId, string rootPath, string bodyPath) =>
        new MindmapSession(watchId, bodyPath, _documents, _views, _mapper);
}
