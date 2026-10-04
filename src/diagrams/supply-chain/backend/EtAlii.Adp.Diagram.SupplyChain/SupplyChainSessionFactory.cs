using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>Opens a session on a <c>.supply</c> document, with the project's history for its moves.</summary>
public sealed class SupplyChainSessionFactory : IDiagramSessionFactory
{
    private readonly ISupplyChainDocumentStore _documents;
    private readonly SupplyChainElementMapper _mapper;
    private readonly SupplyChainSelections _selections;
    private readonly IHistoryStackStore _historyStacks;

    public SupplyChainSessionFactory(
        ISupplyChainDocumentStore documents,
        SupplyChainElementMapper mapper,
        SupplyChainSelections selections,
        IHistoryStackStore historyStacks)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(selections);
        ArgumentNullException.ThrowIfNull(historyStacks);
        _documents = documents;
        _mapper = mapper;
        _selections = selections;
        _historyStacks = historyStacks;
    }

    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.SupplyChain.Origin;

    /// <inheritdoc />
    public IDiagramSession Open(ShortGuid watchId, string rootPath, string bodyPath, string? registrationPath)
    {
        _ = registrationPath;
        return new SupplyChainSession(watchId, bodyPath, _documents, _mapper, _selections, _historyStacks.Get(rootPath));
    }
}
