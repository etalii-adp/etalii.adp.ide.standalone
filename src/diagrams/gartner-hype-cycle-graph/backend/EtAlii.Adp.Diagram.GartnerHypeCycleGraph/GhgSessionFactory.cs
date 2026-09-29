using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Opens a session on an <c>.ghg</c> document, with the project's history for its moves.</summary>
public sealed class GhgSessionFactory : IDiagramSessionFactory
{
    private readonly IGhgDocumentStore _documents;
    private readonly GhgElementMapper _mapper;
    private readonly IHistoryStackStore _historyStacks;

    public GhgSessionFactory(IGhgDocumentStore documents, GhgElementMapper mapper, IHistoryStackStore historyStacks)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(historyStacks);
        _documents = documents;
        _mapper = mapper;
        _historyStacks = historyStacks;
    }

    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.HypeCycleGraph.Origin;

    /// <inheritdoc />
    public IDiagramSession Open(ShortGuid watchId, string rootPath, string bodyPath, string? registrationPath)
    {
        _ = watchId;
        _ = registrationPath;

        // The project's history, so a drag is one undo away like every other edit.
        return new GhgSession(bodyPath, _documents, _mapper, _historyStacks.Get(rootPath));
    }
}
