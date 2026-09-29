using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>Opens a session for a <c>.cld</c> file.</summary>
public sealed class CausalLoopSessionFactory : IDiagramSessionFactory
{
    private readonly ICausalLoopDocumentStore _documents;
    private readonly CausalLoopElementMapper _mapper;
    private readonly IHistoryStackStore _historyStacks;

    public CausalLoopSessionFactory(
        DiagramOrigin origin,
        ICausalLoopDocumentStore documents,
        CausalLoopElementMapper mapper,
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

    /// <inheritdoc />
    public DiagramOrigin Origin { get; }

    /// <inheritdoc />
    public IDiagramSession Open(ShortGuid watchId, string rootPath, string bodyPath, string? registrationPath)
    {
        _ = watchId;
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);

        return new CausalLoopSession(bodyPath, registrationPath, _documents, _mapper, _historyStacks.Get(rootPath));
    }
}
