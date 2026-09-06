using EtAlii.Adp.Backend;
using EtAlii.Adp.Common;
using EtAlii.Adp.Diagram;
namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// Opens an <see cref="OwlSession"/> for the ontology reading - over the same document store the
/// anchor's factory opens over, which is what makes two readings of one file one document with
/// one history (Requirement 8.4).
/// </summary>
public sealed class OwlSessionFactory : IDiagramSessionFactory
{
    private readonly IRdfDocumentStore _documents;
    private readonly OwlElementMapper _mapper;
    private readonly RdfElementMapper _differ;
    private readonly IHistoryStackStore _historyStacks;

    public OwlSessionFactory(
        DiagramOrigin origin,
        IRdfDocumentStore documents,
        OwlElementMapper mapper,
        RdfElementMapper differ,
        IHistoryStackStore historyStacks)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(differ);
        ArgumentNullException.ThrowIfNull(historyStacks);

        Origin = origin;
        _documents = documents;
        _mapper = mapper;
        _differ = differ;
        _historyStacks = historyStacks;
    }

    public DiagramOrigin Origin { get; }

    public IDiagramSession Open(ShortGuid watchId, string rootPath, string bodyPath, string? registrationPath)
    {
        _ = watchId;

        return new OwlSession(
            bodyPath,
            registrationPath,
            _documents,
            _mapper,
            _differ,
            _historyStacks.Get(rootPath));
    }
}
