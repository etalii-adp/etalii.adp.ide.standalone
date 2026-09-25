using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>Opens a session on an <c>.fdg</c> document, with the project's history for its moves.</summary>
public sealed class FdgSessionFactory : IDiagramSessionFactory
{
    private readonly IFdgDocumentStore _documents;
    private readonly FdgElementMapper _mapper;
    private readonly IHistoryStackStore _historyStacks;

    public FdgSessionFactory(IFdgDocumentStore documents, FdgElementMapper mapper, IHistoryStackStore historyStacks)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(historyStacks);
        _documents = documents;
        _mapper = mapper;
        _historyStacks = historyStacks;
    }

    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.FunctionalDecompositionGraph.Origin;

    /// <inheritdoc />
    public IDiagramSession Open(ShortGuid watchId, string rootPath, string bodyPath, string? registrationPath)
    {
        _ = watchId;
        _ = registrationPath;

        // The project's history, so a drag is one undo away like every other edit.
        return new FdgSession(bodyPath, _documents, _mapper, _historyStacks.Get(rootPath));
    }
}
