using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>Opens a <see cref="SparqlSession"/> for a query file.</summary>
public sealed class SparqlSessionFactory : IDiagramSessionFactory
{
    private readonly ISparqlDocumentStore _documents;
    private readonly SparqlElementMapper _mapper;
    private readonly IHistoryStackStore _historyStacks;

    public SparqlSessionFactory(
        DiagramOrigin origin,
        ISparqlDocumentStore documents,
        SparqlElementMapper mapper,
        IHistoryStackStore historyStacks)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(historyStacks);

        Origin = origin;
        _documents = documents;
        _mapper = mapper;
        _historyStacks = historyStacks;
    }

    public DiagramOrigin Origin { get; }

    public IDiagramSession Open(ShortGuid watchId, string rootPath, string bodyPath, string? registrationPath)
    {
        _ = watchId;

        return new SparqlSession(
            bodyPath,
            registrationPath,
            _documents,
            _mapper,
            // The project's history, so the one gesture this diagram has is one undo away.
            _historyStacks.Get(rootPath));
    }
}
