using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>Opens a session on a <c>.skv</c> document, with the project's history for its moves.</summary>
public sealed class SankeySessionFactory : IDiagramSessionFactory
{
    private readonly ISankeyDocumentStore _documents;
    private readonly SankeyElementMapper _mapper;
    private readonly IHistoryStackStore _historyStacks;

    public SankeySessionFactory(ISankeyDocumentStore documents, SankeyElementMapper mapper, IHistoryStackStore historyStacks)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(historyStacks);
        _documents = documents;
        _mapper = mapper;
        _historyStacks = historyStacks;
    }

    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.Sankey.Origin;

    /// <inheritdoc />
    public IDiagramSession Open(ShortGuid watchId, string rootPath, string bodyPath, string? registrationPath)
    {
        _ = watchId;
        _ = registrationPath;
        return new SankeySession(bodyPath, _documents, _mapper, _historyStacks.Get(rootPath));
    }
}
