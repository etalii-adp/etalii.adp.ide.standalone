using EtAlii.Adp.Common;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>
/// Opens a <see cref="ShaclSession"/> for the shapes reading (shacl-diagram Requirement 2.1).
/// </summary>
/// <remarks>
/// No registration header is read here, deliberately: approved Requirement 4.5 declines the
/// anchor's item-10 facility, because a shapes graph constrains any number of data graphs and
/// binding one registration to one of them would misstate the medium.
/// </remarks>
public sealed class ShaclSessionFactory : IDiagramSessionFactory
{
    private readonly IRdfDocumentStore _documents;
    private readonly ShaclElementMapper _mapper;
    private readonly IHistoryStackStore _historyStacks;

    public ShaclSessionFactory(
        DiagramOrigin origin,
        IRdfDocumentStore documents,
        ShaclElementMapper mapper,
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

        return new ShaclSession(
            bodyPath,
            registrationPath,
            _documents,
            _mapper,
            _historyStacks.Get(rootPath));
    }
}
