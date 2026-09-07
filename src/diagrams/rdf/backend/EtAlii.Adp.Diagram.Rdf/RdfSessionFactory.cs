using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// Opens an <see cref="RdfSession"/> for the data-graph reading. The sibling readings joining
/// this family register their own factories over the same store, and read their item-10 headers
/// through <see cref="RdfRegistrationHeaders"/> the way this one would.
/// </summary>
public sealed class RdfSessionFactory : IDiagramSessionFactory
{
    private readonly IRdfDocumentStore _documents;
    private readonly RdfElementMapper _mapper;
    private readonly IHistoryStackStore _historyStacks;

    public RdfSessionFactory(
        DiagramOrigin origin,
        IRdfDocumentStore documents,
        RdfElementMapper mapper,
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

        return new RdfSession(
            bodyPath,
            registrationPath,
            _documents,
            _mapper,
            // The project's history, so a drag on this canvas is one undo away like every other edit.
            _historyStacks.Get(rootPath));
    }
}
